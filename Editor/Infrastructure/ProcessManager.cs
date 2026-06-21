// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.Infrastructure.Mcp;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Manages the Claude CLI process lifecycle and communication.
    /// Facade that coordinates CliProcessHost, StreamJsonEventRouter, ControlRequestHandler, and MCP managers.
    /// </summary>
    internal class ProcessManager : IRuntimeOrchestrator
    {
        #region Events (Public API)

        public event Action<string> OnRawOutput;
        public event Action OnAssistantMessageStarted;
        public event Action<string> OnTextDelta;
        public event Action<ToolUse> OnToolUse;
        public event Action<string, string> OnToolResult;
        public event Action<PendingPermission> OnPermissionRequest;
        public event Action<string> OnError;
        public event Action OnStreamComplete;
        public event Action<ImageAttachment> OnImageAttachment;
        public event Action OnTurnStarted;
        public event Action OnTurnFinished;

        // Thinking events
        public event Action OnThinkingStarted;
        public event Action<string> OnThinkingDelta;
        public event Action<string> OnThinkingCompleted;

        // Context usage event (usedTokens, contextWindow)
        public event Action<int, int> OnContextUsageUpdated;

        #endregion

        #region Components

        private readonly IProcessHost _processHost;
        private IStreamEventParser _eventParser;
        private readonly ControlRequestHandler _controlHandler = new();
        private readonly McpConfigManager _mcpConfigManager = new();
        private readonly ISessionRuntimeClient _sharedSessionRuntimeClient;
        private readonly string _sharedSessionRuntimeProviderId;
        private ISessionRuntimeClient _sessionRuntimeClient;
        private string _sessionRuntimeProviderId;
        private bool _ownsSessionRuntimeClient;

        #endregion

        #region State

        private string _currentSessionId;
        private bool _turnInProgress;
        private bool _explicitStop; // True when Stop() was called explicitly (vs unexpected exit)
        private int _totalInputTokens;
        private int _totalOutputTokens;
        private CliInvocationPlan _activeInvocationPlan;
        private int _turnGeneration;
        private bool _activeTurnCompletionIsObserved;
        private bool _providerAuthFailureReported;

        #endregion

        #region Public Properties

        public bool IsRunning => SidekickSettings.instance.ActiveProvider.RuntimeTransport == ProviderRuntimeTransport.PersistentJsonRpcSession
            ? _sessionRuntimeClient?.IsRunning == true
            : _processHost.IsRunning;
        public bool IsTurnInProgress => _turnInProgress;
        public string CurrentSessionId => _currentSessionId;

        #endregion

        public ProcessManager(ISessionRuntimeClient sharedSessionRuntimeClient = null, string sharedSessionRuntimeProviderId = null)
            : this(new CliProcessHost(), sharedSessionRuntimeClient, sharedSessionRuntimeProviderId)
        {
        }

        internal ProcessManager(IProcessHost processHost, ISessionRuntimeClient sharedSessionRuntimeClient = null, string sharedSessionRuntimeProviderId = null)
        {
            _processHost = processHost ?? new CliProcessHost();
            _sharedSessionRuntimeClient = sharedSessionRuntimeClient;
            _sharedSessionRuntimeProviderId = sharedSessionRuntimeProviderId;

            // Wire process host and control handler
            _processHost.OnOutputLine += HandleOutputLine;
            _processHost.OnErrorLine += line => OnError?.Invoke(line);
            _processHost.OnProcessExited += HandleProcessExited;
            _controlHandler.OnPermissionRequired += perm =>
            {
                NormalizePermission(perm);
                OnPermissionRequest?.Invoke(perm);
            };
            if (SidekickSettings.instance.ActiveProvider.RuntimeTransport == ProviderRuntimeTransport.CliProcess)
            {
                SetEventParser(SidekickSettings.instance.ActiveProvider.CreateEventParser());
            }
        }

        /// <summary>
        /// Replaces the active event parser, re-wiring all events.
        /// </summary>
        private void SetEventParser(IStreamEventParser parser)
        {
            // Unwire old parser
            if (_eventParser != null)
            {
                _eventParser.OnRawLine -= OnRawParserLine;
                _eventParser.OnStreamEvent -= HandleStreamEvent;
                _eventParser.OnTextDelta -= OnTextDeltaRelay;
                _eventParser.OnToolUse -= OnToolUseRelay;
                _eventParser.OnToolResult -= OnToolResultRelay;
                _eventParser.OnPermissionRequest -= OnPermissionRequestRelay;
                _eventParser.OnImageAttachment -= OnImageAttachmentRelay;
                _eventParser.OnSessionIdReceived -= OnSessionIdReceivedRelay;
                _eventParser.OnControlRequest -= HandleControlRequest;
                _eventParser.OnResult -= HandleResult;
                _eventParser.OnThinkingStarted -= OnThinkingStartedRelay;
                _eventParser.OnThinkingDelta -= OnThinkingDeltaRelay;
                _eventParser.OnThinkingCompleted -= OnThinkingCompletedRelay;
            }

            _eventParser = parser;

            // Wire new parser
            _eventParser.OnRawLine += OnRawParserLine;
            _eventParser.OnStreamEvent += HandleStreamEvent;
            _eventParser.OnTextDelta += OnTextDeltaRelay;
            _eventParser.OnToolUse += OnToolUseRelay;
            _eventParser.OnToolResult += OnToolResultRelay;
            _eventParser.OnPermissionRequest += OnPermissionRequestRelay;
            _eventParser.OnImageAttachment += OnImageAttachmentRelay;
            _eventParser.OnSessionIdReceived += OnSessionIdReceivedRelay;
            _eventParser.OnControlRequest += HandleControlRequest;
            _eventParser.OnResult += HandleResult;
            _eventParser.OnThinkingStarted += OnThinkingStartedRelay;
            _eventParser.OnThinkingDelta += OnThinkingDeltaRelay;
            _eventParser.OnThinkingCompleted += OnThinkingCompletedRelay;
        }

        private void OnRawParserLine(string line) => OnRawOutput?.Invoke(line);
        private void OnAssistantMessageStartedRelay() => OnAssistantMessageStarted?.Invoke();
        private void OnTextDeltaRelay(string text) => OnTextDelta?.Invoke(text);
        private void OnToolUseRelay(ToolUse tool)
        {
            NormalizeToolUse(tool);
            OnToolUse?.Invoke(tool);
        }
        private void OnToolResultRelay(string id, string content) => OnToolResult?.Invoke(id, content);
        private void OnPermissionRequestRelay(PendingPermission perm)
        {
            NormalizePermission(perm);
            OnPermissionRequest?.Invoke(perm);
        }
        private void OnImageAttachmentRelay(ImageAttachment img) => OnImageAttachment?.Invoke(img);
        private void OnSessionIdReceivedRelay(string id) => _currentSessionId = id;
        private void OnThinkingStartedRelay() => OnThinkingStarted?.Invoke();
        private void OnThinkingDeltaRelay(string chunk) => OnThinkingDelta?.Invoke(chunk);
        private void OnThinkingCompletedRelay(string text) => OnThinkingCompleted?.Invoke(text);

        private void NormalizeToolUse(ToolUse toolUse)
        {
            if (toolUse == null)
            {
                return;
            }

            SidekickSettings.instance.ActiveProvider?.CreateToolMapper()?.Normalize(toolUse);
        }

        private void NormalizePermission(PendingPermission permission)
        {
            if (permission == null)
            {
                return;
            }

            SidekickSettings.instance.ActiveProvider?.CreateToolMapper()?.Normalize(permission);
        }

        private void EnsureSessionRuntimeClient(ICliProvider provider)
        {
            if (provider is not { RuntimeTransport: ProviderRuntimeTransport.PersistentJsonRpcSession })
            {
                ReleaseSessionRuntimeClient();
                return;
            }

            if (_sessionRuntimeClient != null && string.Equals(_sessionRuntimeProviderId, provider.Id, StringComparison.Ordinal))
            {
                return;
            }

            ReleaseSessionRuntimeClient();

            if (_sharedSessionRuntimeClient != null
                && string.Equals(_sharedSessionRuntimeProviderId, provider.Id, StringComparison.Ordinal))
            {
                _sessionRuntimeClient = _sharedSessionRuntimeClient;
                _ownsSessionRuntimeClient = false;
            }
            else
            {
                _sessionRuntimeClient = provider.CreateSessionRuntimeClient();
                _ownsSessionRuntimeClient = true;
            }

            _sessionRuntimeProviderId = provider.Id;
            if (_sessionRuntimeClient == null)
            {
                return;
            }

            _sessionRuntimeClient.OnRawOutput += line => OnRawOutput?.Invoke(line);
            _sessionRuntimeClient.OnStreamEvent += HandleStreamEvent;
            _sessionRuntimeClient.OnAssistantMessageStarted += OnAssistantMessageStartedRelay;
            _sessionRuntimeClient.OnTextDelta += OnTextDeltaRelay;
            _sessionRuntimeClient.OnToolUse += OnToolUseRelay;
            _sessionRuntimeClient.OnToolResult += OnToolResultRelay;
            _sessionRuntimeClient.OnPermissionRequest += OnPermissionRequestRelay;
            _sessionRuntimeClient.OnResult += HandleResult;
            _sessionRuntimeClient.OnThinkingStarted += OnThinkingStartedRelay;
            _sessionRuntimeClient.OnThinkingDelta += OnThinkingDeltaRelay;
            _sessionRuntimeClient.OnThinkingCompleted += OnThinkingCompletedRelay;
            _sessionRuntimeClient.OnContextUsageUpdated += (usedTokens, contextWindow) => OnContextUsageUpdated?.Invoke(usedTokens, contextWindow);
            _sessionRuntimeClient.OnSessionIdReceived += OnSessionIdReceivedRelay;
            _sessionRuntimeClient.OnError += message => OnError?.Invoke(message);
            _sessionRuntimeClient.OnProcessExited += HandleProcessExited;
        }

        private void ReleaseSessionRuntimeClient()
        {
            if (_sessionRuntimeClient == null)
            {
                _sessionRuntimeProviderId = null;
                _ownsSessionRuntimeClient = false;
                return;
            }

            if (_ownsSessionRuntimeClient)
            {
                _sessionRuntimeClient.Dispose();
            }

            _sessionRuntimeClient = null;
            _sessionRuntimeProviderId = null;
            _ownsSessionRuntimeClient = false;
        }

        #region Public Methods

        /// <summary>
        /// Sends a prompt to Claude CLI and streams the response.
        /// Always uses bidirectional stream-json.
        /// </summary>
        public Task<PromptDispatchResult> SendPromptAsync(
            string prompt,
            string sessionId = null,
            IReadOnlyList<ImageAttachment> attachments = null,
            IReadOnlyList<IContextAttachment> contextAttachments = null)
        {
            return SendPromptStreamingAsync(prompt, sessionId, attachments, contextAttachments);
        }
        
        /// <summary>
        /// Sends a permission response (allow/deny) to the CLI for a pending tool use.
        /// </summary>
        public void SendPermissionResponse(bool allow, string toolUseId = null)
        {
            var json = ControlRequestHandler.BuildPermissionResponseJson(allow, toolUseId);
            WriteJsonLineToStdin(json);
        }

        /// <summary>
        /// Sends a permission response for a PendingPermission, routing to the appropriate method.
        /// </summary>
        public void SendPermissionResponse(PendingPermission permission, bool allow, string message = null, bool remember = false)
        {
            if (permission == null)
            {
                Debug.LogWarning("[Ryx Sidekick] SendPermissionResponse: permission is null");
                return;
            }

            if (SidekickSettings.instance.VerboseLogging)
            {
                Debug.Log($"[Ryx Sidekick] SendPermissionResponse: IsControlRequest={permission.IsControlRequest}");
            }

            try
            {
                switch (permission.Kind)
                {
                    case PendingPermissionKind.SessionCommandApproval:
                    case PendingPermissionKind.SessionFileApproval:
                        _sessionRuntimeClient?.SendApprovalResponse(permission, allow, message, remember);
                        break;

                    case PendingPermissionKind.SessionUserInput:
                        SendUserInputResponse(permission, new JObject
                        {
                            ["answers"] = new JObject()
                        });
                        break;

                    case PendingPermissionKind.ClaudeControlRequest:
                        if (!string.IsNullOrEmpty(permission.RequestId))
                        {
                            SendControlResponse(permission.RequestId, permission.ToolUseId, allow, permission.Input, message);
                        }
                        break;

                    default:
                        SendPermissionResponse(allow, permission.ToolUseId);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Ryx Sidekick] Error in SendPermissionResponse: {ex}");
            }
        }

        public void SendUserInputResponse(PendingPermission permission, JObject response)
        {
            if (permission == null || permission.IsLocalOnly)
            {
                return;
            }

            if (permission.Kind == PendingPermissionKind.SessionUserInput)
            {
                _sessionRuntimeClient?.SendUserInputResponse(permission, response);
            }
        }

        /// <summary>
        /// Sends a control_response back to CLI stdin for a control_request.
        /// </summary>
        public void SendControlResponse(string requestId, string toolUseId, bool allow, JToken updatedInput = null, string message = null)
        {
            var json = ControlRequestHandler.BuildControlResponse(requestId, toolUseId, allow, updatedInput, message);

            if (SidekickSettings.instance.VerboseLogging)
            {
                Debug.Log($"[Ryx Sidekick] Sending control_response: allow={allow}, requestId={requestId}");
            }

            WriteJsonLineToStdin(json);
        }

        /// <summary>
        /// Stops the current request/process.
        /// </summary>
        public void Stop()
        {
            _explicitStop = true;

            if (_turnInProgress)
            {
                CompleteTurnLifecycle(emitTurnFinished: true, emitStreamComplete: false);
            }

            if (SidekickSettings.instance.ActiveProvider.RuntimeTransport == ProviderRuntimeTransport.PersistentJsonRpcSession)
            {
                _sessionRuntimeClient?.Stop();
            }
            else
            {
                _processHost.Stop();
                ReleaseInvocationPlan();
            }
        }

        /// <summary>
        /// Gracefully interrupts by closing stdin and waiting for exit.
        /// </summary>
        public async Task InterruptAsync()
        {
            if (SidekickSettings.instance.ActiveProvider.RuntimeTransport == ProviderRuntimeTransport.PersistentJsonRpcSession)
            {
                if (_sessionRuntimeClient != null)
                {
                    await _sessionRuntimeClient.InterruptAsync();
                }
            }
            else
            {
                await _processHost.InterruptAsync();
                _mcpConfigManager.Cleanup();
                ReleaseInvocationPlan();
            }
        }

        public void Dispose()
        {
            Stop();
            _processHost.Dispose();
            _mcpConfigManager.Dispose();
            ReleaseInvocationPlan();
            ReleaseSessionRuntimeClient();
        }

        #endregion

        #region Private Methods

        private Task<PromptDispatchResult> SendPromptStreamingAsync(
            string prompt,
            string sessionId = null,
            IReadOnlyList<ImageAttachment> attachments = null,
            IReadOnlyList<IContextAttachment> contextAttachments = null)
        {
            if (_turnInProgress)
            {
                const string errorMessage = "A request is already in progress. Please wait for it to complete.";
                OnError?.Invoke(errorMessage);
                return Task.FromResult(new PromptDispatchResult(PromptDispatchStatus.RejectedAlreadyInProgress, errorMessage));
            }

            var settings = SidekickSettings.instance;
            var (valid, message) = settings.ValidateCli();
            if (!valid)
            {
                var errorMessage = $"CLI validation failed: {message}";
                OnError?.Invoke(errorMessage);
                return Task.FromResult(new PromptDispatchResult(PromptDispatchStatus.RejectedValidation, errorMessage));
            }

            var provider = settings.ActiveProvider;
            if (provider.RuntimeTransport == ProviderRuntimeTransport.PersistentJsonRpcSession)
            {
                try
                {
                    EnsureSessionRuntimeClient(provider);
                }
                catch (Exception ex)
                {
                    var errorMessage = $"Failed to start {provider.DisplayName} session runtime: {ex.Message}";
                    OnError?.Invoke(errorMessage);
                    return Task.FromResult(new PromptDispatchResult(PromptDispatchStatus.RejectedStartupFailure, errorMessage));
                }

                if (_sessionRuntimeClient == null)
                {
                    var errorMessage = $"Failed to start {provider.DisplayName} session runtime.";
                    OnError?.Invoke(errorMessage);
                    return Task.FromResult(new PromptDispatchResult(PromptDispatchStatus.RejectedStartupFailure, errorMessage));
                }

                IReadOnlyDictionary<string, JObject> mcpServers;
                try
                {
                    mcpServers = _mcpConfigManager.LoadMcpServers();
                }
                catch (Exception ex)
                {
                    var errorMessage = $"Failed to load MCP configuration: {ex.Message}";
                    OnError?.Invoke(errorMessage);
                    return Task.FromResult(new PromptDispatchResult(PromptDispatchStatus.RejectedStartupFailure, errorMessage));
                }

                if (!string.IsNullOrEmpty(sessionId))
                {
                    _currentSessionId = sessionId;
                }

                return StartPersistentTurnAsync(prompt, sessionId, attachments, contextAttachments, settings.ToInvocationSettings(), mcpServers);
            }

            // Stop any existing process
            if (_processHost.IsRunning)
            {
                Stop();
                _processHost.Cleanup();
            }

            if (!_mcpConfigManager.Prepare(out var mcpArgs))
            {
                const string errorMessage = "Failed to prepare MCP configuration.";
                return Task.FromResult(new PromptDispatchResult(PromptDispatchStatus.RejectedStartupFailure, errorMessage));
            }

            // Create a fresh parser for this provider
            SetEventParser(provider.CreateEventParser());

            _activeInvocationPlan = CliInvocationPlanner.Build(
                settings,
                prompt,
                sessionId,
                mcpArgs,
                attachments,
                contextAttachments);
            _providerAuthFailureReported = false;

            // Start process
            if (!_processHost.StartStreaming(_activeInvocationPlan.Arguments))
            {
                var errorMessage = $"Failed to start {provider.DisplayName} CLI";
                OnError?.Invoke(errorMessage);
                ReleaseInvocationPlan();
                return Task.FromResult(new PromptDispatchResult(PromptDispatchStatus.RejectedStartupFailure, errorMessage));
            }

            // Pre-seed session ID when resuming
            if (!string.IsNullOrEmpty(sessionId))
                _currentSessionId = sessionId;

            _eventParser.Reset();
            _controlHandler.CurrentSessionId = _currentSessionId;

            try
            {
                if (!string.IsNullOrEmpty(_activeInvocationPlan?.StdinPayload))
                {
                    if (!SendPromptPayload(_activeInvocationPlan.StdinPayload, _activeInvocationPlan.CloseStdinAfterPrompt))
                    {
                        return Task.FromResult(FailCliStartup("Cannot send input: process not ready"));
                    }
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(FailCliStartup($"Failed to send prompt: {ex.Message}"));
            }

            StartTurnLifecycle(useCompletionObserver: false);
            return Task.FromResult(PromptDispatchResult.Started());
        }

        private async Task<PromptDispatchResult> StartPersistentTurnAsync(
            string prompt,
            string sessionId,
            IReadOnlyList<ImageAttachment> attachments,
            IReadOnlyList<IContextAttachment> contextAttachments,
            CliInvocationSettings settings,
            IReadOnlyDictionary<string, JObject> mcpServers)
        {
            if (_sessionRuntimeClient is not IPersistentTurnStarter turnStarter)
            {
                const string errorMessage = "Active session runtime does not support persistent startup acknowledgement.";
                OnError?.Invoke(errorMessage);
                return new PromptDispatchResult(PromptDispatchStatus.RejectedStartupFailure, errorMessage);
            }

            PersistentTurnStartAck ack;
            try
            {
                ack = await turnStarter.StartTurnAsync(prompt, sessionId, attachments, contextAttachments, settings, mcpServers);
            }
            catch (Exception ex)
            {
                var errorMessage = $"Failed to start session turn: {ex.Message}";
                OnError?.Invoke(errorMessage);
                return new PromptDispatchResult(PromptDispatchStatus.RejectedStartupFailure, errorMessage);
            }

            if (ack == null || !ack.IsStarted)
            {
                return new PromptDispatchResult(
                    PromptDispatchStatus.RejectedStartupFailure,
                    ack?.ErrorMessage ?? "Failed to start session turn.");
            }

            if (!string.IsNullOrEmpty(ack.ResolvedSessionId))
            {
                _currentSessionId = ack.ResolvedSessionId;
            }

            StartTurnLifecycle(useCompletionObserver: true);
            ObservePersistentTurnCompletion(ack.CompletionTask, _turnGeneration);
            return PromptDispatchResult.Started();
        }

        private void ObservePersistentTurnCompletion(Task<bool> completionTask, int turnGeneration)
        {
            _ = ObservePersistentTurnCompletionAsync(completionTask, turnGeneration);
        }

        private async Task ObservePersistentTurnCompletionAsync(Task<bool> completionTask, int turnGeneration)
        {
            try
            {
                await completionTask;
                if (!IsObservedPersistentTurnCurrent(turnGeneration))
                {
                    return;
                }

                CompleteTurnLifecycle(emitTurnFinished: true, emitStreamComplete: true);
            }
            catch (Exception ex)
            {
                if (!IsObservedPersistentTurnCurrent(turnGeneration))
                {
                    return;
                }

                OnError?.Invoke($"Failed to run session turn: {ex.Message}");
                CompleteTurnLifecycle(emitTurnFinished: true, emitStreamComplete: true);
            }
        }

        private bool IsObservedPersistentTurnCurrent(int turnGeneration)
        {
            return _activeTurnCompletionIsObserved
                && _turnInProgress
                && turnGeneration == _turnGeneration;
        }

        private void StartTurnLifecycle(bool useCompletionObserver)
        {
            _turnGeneration++;
            _turnInProgress = true;
            _activeTurnCompletionIsObserved = useCompletionObserver;
            _totalInputTokens = 0;
            _totalOutputTokens = 0;
            OnTurnStarted?.Invoke();
        }

        private void CompleteTurnLifecycle(bool emitTurnFinished, bool emitStreamComplete)
        {
            if (!_turnInProgress)
            {
                return;
            }

            _turnInProgress = false;
            _activeTurnCompletionIsObserved = false;
            _turnGeneration++;

            if (emitTurnFinished)
            {
                OnTurnFinished?.Invoke();
            }

            if (emitStreamComplete)
            {
                OnStreamComplete?.Invoke();
            }
        }

        private PromptDispatchResult FailCliStartup(string errorMessage)
        {
            _explicitStop = true;
            _processHost.Stop();
            _processHost.Cleanup();
            ReleaseInvocationPlan();
            OnError?.Invoke(errorMessage);
            return new PromptDispatchResult(PromptDispatchStatus.RejectedStartupFailure, errorMessage);
        }

        private void WriteJsonLineToStdin(string json)
        {
            if (SidekickSettings.instance.VerboseLogging)
            {
                var preview = json.Length > 200 ? json[..200] + "..." : json;
                Debug.Log($"[Ryx Sidekick] WriteJsonLineToStdin: {preview}");
            }

            var success = _processHost.WriteLineToStdin(json);
            if (!success)
            {
                OnError?.Invoke("Cannot send input: process not ready");
            }
        }

        private bool SendPromptPayload(string payload, bool closeAfterWrite)
        {
            bool success;

            if (closeAfterWrite)
            {
                success = _processHost.WriteToStdin(payload, appendNewLine: false);
                if (success)
                {
                    success = _processHost.TryCloseStdin();
                }
            }
            else
            {
                success = _processHost.WriteLineToStdin(payload);
            }

            if (!success)
            {
                OnError?.Invoke("Cannot send input: process not ready");
            }

            return success;
        }

        private void HandleOutputLine(string line)
        {
            if (SidekickSettings.instance.VerboseLogging)
            {
                var preview = line.Length > 200 ? line[..200] + "..." : line;
                Debug.Log($"[Ryx Sidekick] Raw: {preview}");
            }

            _eventParser.ProcessLine(line);
        }

        private void HandleStreamEvent(StreamEvent evt)
        {
            if (TryHandleProviderAuthFailure(evt))
            {
                return;
            }

            // Track tokens from assistant messages. Input must include cache tokens — under prompt
            // caching the bulk of the context lives in cache_read, so input_tokens alone badly
            // under-reports how full the window is. This mirrors the history path (CliHistoryService).
            if (evt is AssistantMessageEvent { message: { usage: not null } } assistantEvent)
            {
                var usage = assistantEvent.message.usage;
                _totalInputTokens = usage.input_tokens
                    + usage.cache_creation_input_tokens
                    + usage.cache_read_input_tokens;
                _totalOutputTokens = usage.output_tokens;
            }
        }

        private void HandleControlRequest(string json)
        {
            var errorResponse = _controlHandler.HandleControlRequest(json);
            if (!string.IsNullOrEmpty(errorResponse))
            {
                WriteJsonLineToStdin(errorResponse);
            }
        }

        private void HandleResult(ResultEvent resultEvent)
        {
            // Extract context usage info and fire event
            if (resultEvent?.modelUsage != null)
            {
                var totalTokens = _totalInputTokens + _totalOutputTokens;
                var contextWindow = 0;

                // modelUsage is a dictionary keyed by model ID. Harvest every real per-model window
                // so history / cold-start lookups can reuse it, and keep the first positive value
                // for the live status display.
                foreach (var kvp in resultEvent.modelUsage)
                {
                    var window = kvp.Value?.contextWindow ?? 0;
                    if (window <= 0)
                        continue;

                    ModelContextWindowRegistry.Record(kvp.Key, window);

                    if (contextWindow == 0)
                        contextWindow = window;
                }

                if (contextWindow > 0)
                {
                    OnContextUsageUpdated?.Invoke(totalTokens, contextWindow);
                }
            }

            if (_turnInProgress && !_activeTurnCompletionIsObserved)
            {
                CompleteTurnLifecycle(emitTurnFinished: true, emitStreamComplete: true);
            }
        }

        private bool TryHandleProviderAuthFailure(StreamEvent evt)
        {
            if (_providerAuthFailureReported)
            {
                return true;
            }

            if (evt is not SystemEvent systemEvent)
            {
                return false;
            }

            if (!IsProviderAuthFailure(systemEvent))
            {
                return false;
            }

            _providerAuthFailureReported = true;

            var providerName = SidekickSettings.instance.ActiveProvider.DisplayName;
            OnError?.Invoke(
                $"{providerName} authentication failed. Claude reported no usable credentials for this Unity-launched process. " +
                "Log in from Sidekick or run `claude login` in a terminal, then retry.");

            Stop();
            return true;
        }

        private static bool IsProviderAuthFailure(SystemEvent systemEvent)
        {
            if (!string.Equals(systemEvent.subtype, "api_retry", StringComparison.Ordinal))
            {
                return false;
            }

            var error = systemEvent.error ?? string.Empty;
            if (error.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return systemEvent.error_status is 401 or 403;
        }

        private void HandleProcessExited(int exitCode)
        {
            // If Stop() was called explicitly, it already handled turn state - don't interfere
            if (_explicitStop)
            {
                _explicitStop = false;
            }
            else if (_turnInProgress)
            {
                // Unexpected exit (crash, error) - reset turn state
                CompleteTurnLifecycle(emitTurnFinished: false, emitStreamComplete: false);
                EditorApplication.delayCall += () => OnTurnFinished?.Invoke();
                EditorApplication.delayCall += () => OnStreamComplete?.Invoke();
            }

            // Exit codes 130 (SIGINT), 137 (SIGKILL), 143 (SIGTERM) are user interruptions, not errors
            var isUserInterruption = exitCode is 130 or 137 or 143;
            if (exitCode != 0 && !isUserInterruption)
            {
                var providerName = SidekickSettings.instance.ActiveProvider.DisplayName;
                EditorApplication.delayCall += () => OnError?.Invoke($"{providerName} CLI exited with code {exitCode}");
            }
            ReleaseInvocationPlan();
            _providerAuthFailureReported = false;
        }

        private void ReleaseInvocationPlan()
        {
            _activeInvocationPlan?.Dispose();
            _activeInvocationPlan = null;
        }

        #endregion
    }
}
