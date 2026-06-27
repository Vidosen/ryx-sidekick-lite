// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.Providers.Claude;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Ryx.Sidekick.Editor.Infrastructure.Providers.Claude
{
    /// <summary>
    /// Persistent Claude Code session over bidirectional stream-json.
    ///
    /// <para>
    /// Unlike the legacy per-prompt <see cref="ProviderRuntimeTransport.CliProcess"/> flow
    /// (where <c>ProcessManager</c> spawns a fresh <c>claude</c> process for every turn and the
    /// process exits after the <c>result</c> event), this client keeps a single long-lived
    /// <see cref="CliProcessHost"/> alive between turns with stdin held open. The first turn
    /// starts the process, sends the <c>initialize</c> control handshake, then writes a stream-json
    /// user message; subsequent turns simply write a new user message into the already-open stdin.
    /// The <c>result</c> event completes the current turn's <see cref="PersistentTurnStartAck.CompletionTask"/>
    /// but the process stays alive and waits for the next user message.
    /// </para>
    ///
    /// <para>
    /// Stream parsing is delegated to a <see cref="StreamJsonEventRouter"/> (the same parser used
    /// in the per-prompt path) and control-request handling to a <see cref="ControlRequestHandler"/>;
    /// their events are re-broadcast through the <see cref="ISessionRuntimeClient"/> surface that
    /// <c>ProcessManager</c> already subscribes to.
    /// </para>
    /// </summary>
    internal sealed class ClaudePersistentSessionClient
        : ISessionRuntimeClient, IPersistentTurnStarter, IRuntimeModeSwitch, IProcessHostFactoryAware, IReconnectableSessionClient
    {
        private const string InitializeRequestLine =
            "{\"type\":\"control_request\",\"request_id\":\"req_init\",\"request\":{\"subtype\":\"initialize\"}}";

        private IProcessHost _host;
        private readonly IStreamEventParser _router;
        private readonly ControlRequestHandler _controlHandler = new();

        // True when a concrete host was injected by a test (the host ctor param). In that case the
        // IProcessHostFactory must NOT replace it. False when the host was default-created from the
        // (parameterless-provider) factory and may be swapped by SetProcessHostFactory before any start.
        private readonly bool _hostExplicitlyInjected;
        private bool _processStarted;

        private string _currentSessionId;
        private string _activeProcessSessionId;
        private string _mcpConfigPath;
        private TaskCompletionSource<bool> _turnCompletionTcs;
        private bool _interruptRequested;
        private bool _explicitStop;

        public event Action<string> OnRawOutput;
        public event Action<StreamEvent> OnStreamEvent;
        public event Action OnAssistantMessageStarted;
        public event Action<string> OnTextDelta;
        public event Action<ToolUse> OnToolUse;
        public event Action<string, string> OnToolResult;
        public event Action<PendingPermission> OnPermissionRequest;
        public event Action<ResultEvent> OnResult;
        public event Action OnThinkingStarted;
        public event Action<string> OnThinkingDelta;
        public event Action<string> OnThinkingCompleted;
        public event Action<int, int> OnContextUsageUpdated;
        public event Action<string> OnSessionIdReceived;
        public event Action<string> OnError;
        public event Action OnProcessStarted;
        public event Action<int> OnProcessExited;

        public bool IsRunning => _host.IsRunning;
        public string CurrentSessionId => _currentSessionId;

        public ClaudePersistentSessionClient(IProcessHost host = null, IStreamEventParser router = null, IProcessHostFactory hostFactory = null)
        {
            // When no explicit host is injected (tests do), the factory decides in-process vs. the
            // out-of-process daemon host. A null factory uses the in-process default (unchanged).
            // _hostExplicitlyInjected pins a test-provided host so a later SetProcessHostFactory
            // injection (the production wiring) cannot replace it.
            _hostExplicitlyInjected = host != null;
            _host = host ?? (hostFactory ?? new DefaultProcessHostFactory()).Create();
            _router = router ?? new ClaudeCliProvider().CreateEventParser();

            WireHost(_host);

            // Re-broadcast parser events to the ISessionRuntimeClient surface.
            _router.OnRawLine += line => OnRawOutput?.Invoke(line);
            _router.OnStreamEvent += evt => OnStreamEvent?.Invoke(evt);
            _router.OnTextDelta += text => OnTextDelta?.Invoke(text);
            _router.OnToolUse += tool => OnToolUse?.Invoke(tool);
            _router.OnToolResult += (id, content) => OnToolResult?.Invoke(id, content);
            _router.OnPermissionRequest += perm => OnPermissionRequest?.Invoke(perm);
            _router.OnSessionIdReceived += HandleSessionId;
            _router.OnControlRequest += HandleControlRequest;
            _router.OnResult += HandleResult;
            _router.OnThinkingStarted += () => OnThinkingStarted?.Invoke();
            _router.OnThinkingDelta += chunk => OnThinkingDelta?.Invoke(chunk);
            _router.OnThinkingCompleted += text => OnThinkingCompleted?.Invoke(text);

            // can_use_tool control requests surface as interactive permission prompts.
            _controlHandler.OnPermissionRequired += perm => OnPermissionRequest?.Invoke(perm);
        }

        public async Task<bool> RunTurnAsync(
            string prompt,
            string sessionId,
            IReadOnlyList<ImageAttachment> attachments,
            IReadOnlyList<IContextAttachment> contextAttachments,
            CliInvocationSettings settings,
            IReadOnlyDictionary<string, JObject> mcpServers = null)
        {
            var ack = await ((IPersistentTurnStarter)this).StartTurnAsync(
                prompt, sessionId, attachments, contextAttachments, settings, mcpServers);
            return ack.IsStarted && await ack.CompletionTask;
        }

        async Task<PersistentTurnStartAck> IPersistentTurnStarter.StartTurnAsync(
            string prompt,
            string sessionId,
            IReadOnlyList<ImageAttachment> attachments,
            IReadOnlyList<IContextAttachment> contextAttachments,
            CliInvocationSettings settings,
            IReadOnlyDictionary<string, JObject> mcpServers)
        {
            try
            {
                // The live process already owns its own session context. Restart only when the
                // process is down, or when the caller targets a *different* prior conversation.
                var needsRestart = !_host.IsRunning
                    || (!string.IsNullOrEmpty(sessionId)
                        && !string.IsNullOrEmpty(_activeProcessSessionId)
                        && !string.Equals(sessionId, _activeProcessSessionId, StringComparison.Ordinal));

                if (needsRestart)
                {
                    await StartProcessAsync(sessionId, settings, mcpServers);
                }

                ResetTurnState();
                _turnCompletionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                var userMessage = ControlRequestHandler.BuildUserMessage(prompt, attachments, contextAttachments);
                if (!_host.WriteLineToStdin(userMessage))
                {
                    OnError?.Invoke("Claude session stdin is not available.");
                    _turnCompletionTcs.TrySetResult(false);
                    return PersistentTurnStartAck.Rejected("Claude session stdin is not available.");
                }

                // For an active live session the resolved id is whatever the process reported; for a
                // fresh process it is not known until the system/init event arrives.
                var resolvedSessionId = _currentSessionId ?? sessionId;
                return PersistentTurnStartAck.Started(resolvedSessionId, _turnCompletionTcs.Task);
            }
            catch (OperationCanceledException)
            {
                _turnCompletionTcs?.TrySetResult(false);
                return PersistentTurnStartAck.Rejected("Claude session startup was cancelled.");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Claude session error: {ex.Message}");
                _turnCompletionTcs?.TrySetResult(false);
                return PersistentTurnStartAck.Rejected($"Claude session error: {ex.Message}");
            }
        }

        public void SendApprovalResponse(PendingPermission permission, bool allow, string message = null, bool remember = false)
        {
            if (permission == null)
            {
                return;
            }

            // A plain approval echoes the permission's original input back as the control_response.
            SendControlResponse(permission.RequestId, permission.ToolUseId, allow, permission.Input, message);
        }

        public void SendControlResponse(string requestId, string toolUseId, bool allow, JToken updatedInput, string message)
        {
            if (string.IsNullOrEmpty(requestId))
            {
                return;
            }

            // The live process is owned by THIS client, so the response goes to its stdin — carrying
            // updatedInput verbatim (e.g. the AskUserQuestion answers), not the original tool input.
            var json = ControlRequestHandler.BuildControlResponse(
                requestId,
                toolUseId,
                allow,
                updatedInput,
                message);
            WriteToStdin(json);
        }

        public void SendUserInputResponse(PendingPermission permission, JObject response)
        {
            // Claude does not use the ACP-style session/user-input channel; approval responses
            // for can_use_tool flow through SendApprovalResponse / control_response.
        }

        public async Task InterruptAsync()
        {
            if (!_host.IsRunning)
            {
                return;
            }

            // Prefer a graceful interrupt control_request — the process stays alive for the
            // next user message (mirrors query.interrupt() in the SDK).
            var interruptRequest = new JObject
            {
                ["type"] = "control_request",
                ["request_id"] = $"req_interrupt_{Guid.NewGuid():N}",
                ["request"] = new JObject
                {
                    ["subtype"] = "interrupt"
                }
            }.ToString(Newtonsoft.Json.Formatting.None);

            if (_host.WriteLineToStdin(interruptRequest))
            {
                _turnCompletionTcs?.TrySetResult(false);
                return;
            }

            // Fallback: close stdin / kill, like CursorAcpClient.
            _interruptRequested = true;
            await _host.InterruptAsync();
        }

        public Task SetPermissionModeAsync(string mode)
        {
            if (string.IsNullOrEmpty(mode) || !_host.IsRunning)
            {
                // Idle session: nothing to switch live; the persisted mode applies on next start.
                return Task.CompletedTask;
            }

            var request = BuildControlRequest("set-mode-", new JObject
            {
                ["subtype"] = "set_permission_mode",
                ["mode"] = mode
            });
            WriteToStdin(request);
            return Task.CompletedTask;
        }

        public Task SetModelAsync(string model)
        {
            if (string.IsNullOrEmpty(model) || !_host.IsRunning)
            {
                return Task.CompletedTask;
            }

            var request = BuildControlRequest("set-model-", new JObject
            {
                ["subtype"] = "set_model",
                ["model"] = model
            });
            WriteToStdin(request);
            return Task.CompletedTask;
        }

        private static string BuildControlRequest(string requestIdPrefix, JObject requestBody)
        {
            return new JObject
            {
                ["type"] = "control_request",
                ["request_id"] = $"{requestIdPrefix}{Guid.NewGuid():N}",
                ["request"] = requestBody
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        public void Stop()
        {
            _explicitStop = true;
            _turnCompletionTcs?.TrySetResult(false);
            _host.Stop();
            ResetProcessState();
            DeleteMcpConfigFile();
        }

        public void Dispose()
        {
            _explicitStop = true;
            _host.Dispose();
            DeleteMcpConfigFile();
        }

        private async Task StartProcessAsync(
            string sessionId,
            CliInvocationSettings settings,
            IReadOnlyDictionary<string, JObject> mcpServers)
        {
            if (_host.IsRunning)
            {
                _host.Stop();
                _host.Cleanup();
                ResetProcessState();
            }

            var mcpConfigPath = WriteMcpConfigFile(mcpServers);
            var arguments = BuildLaunchArguments(sessionId, settings, mcpConfigPath);
            _interruptRequested = false;
            _explicitStop = false;

            if (!_host.StartStreaming(arguments))
            {
                throw new InvalidOperationException("Failed to start Claude session process.");
            }

            _processStarted = true;
            _activeProcessSessionId = string.IsNullOrEmpty(sessionId) ? null : sessionId;
            _currentSessionId = _activeProcessSessionId;
            _router.Reset();
            _controlHandler.CurrentSessionId = _currentSessionId;

            // Drive the control handshake. Initialize also primes capabilities, but the live
            // capability fetch keeps using the separate short-lived ClaudeCliCapabilitiesClient.
            if (!_host.WriteLineToStdin(InitializeRequestLine))
            {
                throw new InvalidOperationException("Failed to send Claude initialize request.");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Builds the launch argument string for the persistent process. The selected permission mode
        /// is applied via <c>--permission-mode</c> at launch (plan collaboration mode and bypass have
        /// special handling); mid-session changes go through live set_permission_mode control requests.
        /// </summary>
        private static string BuildLaunchArguments(
            string sessionId,
            CliInvocationSettings settings,
            string mcpConfigPath)
        {
            var args = new StringBuilder();
            args.Append("-p --input-format stream-json --output-format stream-json ");
            args.Append("--include-partial-messages --verbose ");
            args.Append("--permission-prompt-tool stdio ");

            if (!string.IsNullOrEmpty(settings?.Model))
            {
                args.Append($"--model {settings.Model} ");
            }

            if (!string.IsNullOrEmpty(settings?.ReasoningEffort))
            {
                args.Append($"--effort {settings.ReasoningEffort} ");
            }

            // Permission mode at launch:
            //  - plan collaboration mode → --permission-mode plan; the selected permission mode is
            //    preserved and applied when the user exits plan.
            //  - bypassPermissions → launch directly in bypass WITH --dangerously-skip-permissions,
            //    because live-switching into bypass is rejected by the CLI without the dangerous flag,
            //    so the VM relaunches the process for this mode (see ProviderSelectorViewModel).
            //  - default / acceptEdits / auto → launch in the selected mode. This is essential: the
            //    session must start in the chosen mode (e.g. auto), otherwise it falls back to default
            //    and prompts for every action until the user re-picks the mode. Live set_permission_mode
            //    control_requests handle mid-session changes without a restart.
            if (string.Equals(settings?.CollaborationMode, "plan", StringComparison.Ordinal))
            {
                args.Append("--permission-mode plan ");
            }
            else if (string.Equals(settings?.PermissionMode, "bypassPermissions", StringComparison.Ordinal))
            {
                args.Append("--permission-mode bypassPermissions --dangerously-skip-permissions ");
            }
            else if (!string.IsNullOrEmpty(settings?.PermissionMode))
            {
                args.Append($"--permission-mode {settings.PermissionMode} ");
            }

            // Thinking / max-turns mirror the legacy CliProcess BuildArguments; CliInvocationSettings
            // does not carry these, so read them from the live settings (same source the legacy path uses).
            var userSettings = SidekickSettings.instance;
            if (userSettings != null)
            {
                if (userSettings.EnableThinking && userSettings.MaxThinkingTokens > 0)
                {
                    args.Append($"--max-thinking-tokens {userSettings.MaxThinkingTokens} ");
                }

                if (userSettings.MaxTurns > 0)
                {
                    args.Append($"--max-turns {userSettings.MaxTurns} ");
                }
            }

            if (!string.IsNullOrEmpty(mcpConfigPath))
            {
                args.Append($"--mcp-config \"{mcpConfigPath}\" ");
            }

            if (!string.IsNullOrEmpty(sessionId))
            {
                args.Append($"-r \"{sessionId}\" ");
            }

            return args.ToString().Trim();
        }

        /// <summary>
        /// Serializes the loaded MCP server map to a temp file in the same
        /// <c>{"mcpServers": {...}}</c> shape that <see cref="McpConfigManager"/> writes, and returns
        /// its path (or null when there are no servers). The file is owned by this client and deleted
        /// on the next start, Stop, and Dispose.
        /// </summary>
        private string WriteMcpConfigFile(IReadOnlyDictionary<string, JObject> mcpServers)
        {
            DeleteMcpConfigFile();

            if (mcpServers == null || mcpServers.Count == 0)
            {
                return null;
            }

            try
            {
                var servers = new JObject();
                foreach (var pair in mcpServers)
                {
                    if (pair.Value != null)
                    {
                        servers[pair.Key] = (JObject)pair.Value.DeepClone();
                    }
                }

                var config = new JObject { ["mcpServers"] = servers };
                var path = Path.Combine(Path.GetTempPath(), $"sidekick-claude-mcp-{Guid.NewGuid():N}.json");
                File.WriteAllText(path, config.ToString(Newtonsoft.Json.Formatting.Indented),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mcpConfigPath = path;
                return path;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClaudePersistentSessionClient] Failed to write MCP config: {ex.Message}");
                _mcpConfigPath = null;
                return null;
            }
        }

        private void DeleteMcpConfigFile()
        {
            if (string.IsNullOrEmpty(_mcpConfigPath))
            {
                return;
            }

            try
            {
                if (File.Exists(_mcpConfigPath))
                {
                    File.Delete(_mcpConfigPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClaudePersistentSessionClient] Failed to delete MCP config: {ex.Message}");
            }
            finally
            {
                _mcpConfigPath = null;
            }
        }

        private void HandleOutputLine(string line)
        {
            if (SidekickSettings.instance.VerboseLogging)
            {
                var preview = line != null && line.Length > 200 ? line[..200] + "..." : line;
                Debug.Log($"[ClaudePersistentSessionClient] Raw: {preview}");
            }

            _router.ProcessLine(line);
        }

        private void HandleErrorLine(string line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                OnError?.Invoke(line);
            }
        }

        private void HandleControlRequest(string json)
        {
            // initialize control_response from the handshake is a *response*, not a request — the
            // router only forwards control_request lines here. can_use_tool surfaces as a permission;
            // unknown subtypes get an error response written back to stdin.
            var errorResponse = _controlHandler.HandleControlRequest(json);
            if (!string.IsNullOrEmpty(errorResponse))
            {
                WriteToStdin(errorResponse);
            }
        }

        private void HandleResult(ResultEvent resultEvent)
        {
            // Re-broadcast so ProcessManager.HandleResult is the single source of usage/context-window
            // accounting (token totals + ModelContextWindowRegistry + OnContextUsageUpdated). It will
            // NOT double-complete the turn: the persistent path is "observed" via CompletionTask, so
            // its CompleteTurnLifecycle branch (gated on !_activeTurnCompletionIsObserved) is skipped.
            OnResult?.Invoke(resultEvent);

            // Completing the turn does NOT stop the process — the persistent host stays alive and
            // waits for the next user message. This is the key divergence from the CliProcess path.
            _turnCompletionTcs?.TrySetResult(true);
        }

        private void HandleSessionId(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            _currentSessionId = sessionId;
            _activeProcessSessionId = sessionId;
            _controlHandler.CurrentSessionId = sessionId;
            OnSessionIdReceived?.Invoke(sessionId);
        }

        private void HandleProcessExited(int exitCode)
        {
            var interruptedByUser = _interruptRequested && exitCode is 130 or 137 or 143;
            _interruptRequested = false;
            ResetProcessState();

            _turnCompletionTcs?.TrySetResult(false);

            if (!_explicitStop)
            {
                EditorApplication.delayCall += () => OnProcessExited?.Invoke(exitCode);
            }

            _explicitStop = false;
        }

        private void WriteToStdin(string json)
        {
            if (!_host.WriteLineToStdin(json))
            {
                OnError?.Invoke("Cannot send input: Claude session not ready.");
            }
        }

        private void ResetTurnState()
        {
            _router.Reset();
        }

        private void ResetProcessState()
        {
            _activeProcessSessionId = null;
        }

        private void HandleProcessStarted()
        {
            OnProcessStarted?.Invoke();
        }

        // === host wiring (extracted so SetProcessHostFactory can swap the host before any start) ===

        private void WireHost(IProcessHost host)
        {
            host.OnOutputLine += HandleOutputLine;
            host.OnErrorLine += HandleErrorLine;
            host.OnProcessStarted += HandleProcessStarted;
            host.OnProcessExited += HandleProcessExited;
        }

        private void UnwireHost(IProcessHost host)
        {
            host.OnOutputLine -= HandleOutputLine;
            host.OnErrorLine -= HandleErrorLine;
            host.OnProcessStarted -= HandleProcessStarted;
            host.OnProcessExited -= HandleProcessExited;
        }

        // === IProcessHostFactoryAware ===

        public void SetProcessHostFactory(IProcessHostFactory factory)
        {
            // Honor a test-injected host and never swap once a process has started: swapping a live
            // host would orphan the running child and lose its event stream.
            if (factory == null || _hostExplicitlyInjected || _processStarted || _host is { IsRunning: true })
            {
                return;
            }

            var newHost = factory.Create();
            if (newHost == null || ReferenceEquals(newHost, _host))
            {
                return;
            }

            UnwireHost(_host);
            _host.Dispose();
            _host = newHost;
            WireHost(_host);
        }

        // === IReconnectableSessionClient ===

        public string SessionHandle =>
            _host is IReconnectableProcessHost reconnectable ? reconnectable.SessionHandle : string.Empty;

        public long LastObservedSequence =>
            _host is IReconnectableProcessHost reconnectable ? reconnectable.LastObservedSequence : 0L;

        public bool SupportsReattach => _host is IReconnectableProcessHost;

        public PersistentTurnStartAck TryReattach(string sessionHandle, long afterDurableSeq)
        {
            if (_host is not IReconnectableProcessHost reconnectable)
            {
                return null;
            }

            // Prime a fresh parse so the replayed in-flight turn rebuilds state from its boundary
            // idempotently (the host de-dups by seq; re-parsing the same lines yields equivalent state).
            _router.Reset();
            _controlHandler.CurrentSessionId = _currentSessionId;
            _interruptRequested = false;
            _explicitStop = false;

            if (!reconnectable.TryAttach(sessionHandle, afterDurableSeq))
            {
                return null;
            }

            _processStarted = true;
            _activeProcessSessionId = _currentSessionId;

            // Arm a fresh completion task so ProcessManager can observe the (possibly already
            // in-flight) turn exactly as it observes a normal StartTurnAsync. A replayed `result`
            // for an already-completed turn resolves it immediately; a mid-turn reattach leaves it
            // pending until the live `result` arrives.
            _turnCompletionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return PersistentTurnStartAck.Started(_currentSessionId, _turnCompletionTcs.Task);
        }

        public void SendTrim(long safeSeq)
        {
            if (_host is IReconnectableProcessHost reconnectable)
            {
                reconnectable.SendTrim(safeSeq);
            }
        }
    }
}
