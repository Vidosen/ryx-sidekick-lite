// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Ryx.Sidekick.Editor;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Infrastructure.Platform;
using Ryx.Sidekick.Editor.Infrastructure.Providers.Claude;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Providers.Claude
{
    /// <summary>
    /// Provider implementation for Anthropic's Claude Code CLI.
    /// Uses bidirectional stream-json (stdin/stdout) with interactive permissions.
    /// </summary>
    internal class ClaudeCliProvider : ICliProvider, IProviderCapabilitySourcesFactory
    {
        private static readonly IProviderToolMapper ToolMapper = new ClaudeToolMapper();

        public string Id => "claude";
        public string DisplayName => "Claude Code";
        public string DefaultBinaryName => "claude";
        public string InstallUrl => "https://docs.anthropic.com/en/docs/claude-code/getting-started";

        public string[] ModelPresets => new[] { "sonnet", "opus", "haiku" };
        public string DefaultModel => "sonnet";

        public ProviderModelCatalog BuildFallbackModelCatalog()
        {
            // Per-model reasoning-effort gating. All values are valid `--effort` levels accepted by the
            // CLI; this is a UX restriction (e.g. haiku exposes none, only opus offers xhigh/max).
            var commonEfforts = new[] { "low", "medium", "high" };
            var opusEfforts = new[] { "low", "medium", "high", "xhigh", "max" };
            return new ProviderModelCatalog(
                Id,
                ModelPresets.Select(model =>
                {
                    var efforts = string.Equals(model, "opus", StringComparison.Ordinal)
                        ? opusEfforts
                        : string.Equals(model, "sonnet", StringComparison.Ordinal)
                            ? commonEfforts
                            : Array.Empty<string>();
                    var defaultEffort = string.Equals(model, "opus", StringComparison.Ordinal)
                        ? "xhigh"
                        : efforts.Length > 0
                            ? "high"
                            : string.Empty;
                    return new ModelDescriptor(
                        model,
                        isDefault: string.Equals(model, DefaultModel, StringComparison.Ordinal),
                        supportedReasoningEfforts: efforts.Select(value => new ReasoningEffortDescriptor(value)),
                        defaultReasoningEffort: defaultEffort);
                }));
        }

        public CollaborationModeDescriptor[] CollaborationModes => new[]
        {
            new CollaborationModeDescriptor("default", "Default mode", "mode-default"),
            new CollaborationModeDescriptor("plan", "Plan mode", "mode-plan"),
        };

        public ProviderRuntimeTransport RuntimeTransport => ProviderRuntimeTransport.CliProcess;
        public bool SupportsInteractivePermissions => true;
        public PromptTransportMode PromptTransportMode => PromptTransportMode.StreamJsonStdin;
        public bool UsesImageAttachmentFilePaths => false;
        public bool SupportsThinking => true;
        public bool SupportsMcpConfig => true;
        public bool SupportsVerbose => true;

        public ISessionRuntimeClient CreateSessionRuntimeClient()
        {
            return null;
        }

        public IProviderToolMapper CreateToolMapper()
        {
            return ToolMapper;
        }

        public PermissionModeDescriptor[] GetPermissionModes(string collaborationMode)
        {
            return collaborationMode == "plan"
                ? new[]
                {
                    new PermissionModeDescriptor("default", "Ask before edits", "permission-default"),
                }
                : new[]
                {
                    new PermissionModeDescriptor("default", "Ask before edits", "permission-default"),
                    new PermissionModeDescriptor("bypassPermissions", "Edit automatically", "permission-auto"),
                };
        }

        public ProviderModeSelection NormalizeModeSelection(string collaborationMode, string permissionMode)
        {
            var normalizedCollaboration = collaborationMode == "plan" ? "plan" : "default";
            var permissionModes = GetPermissionModes(normalizedCollaboration);
            var normalizedPermission = permissionModes.Length > 0 ? permissionModes[0].Value : "default";

            foreach (var mode in permissionModes)
            {
                if (mode.Value == permissionMode)
                {
                    normalizedPermission = permissionMode;
                    break;
                }
            }

            return new ProviderModeSelection(normalizedCollaboration, normalizedPermission);
        }

        public bool IsAutoApprovePermissionMode(string permissionMode)
        {
            return permissionMode == "bypassPermissions";
        }

        public string BuildArguments(CliArgumentContext ctx)
        {
            var args = new StringBuilder();
            var normalizedModes = NormalizeModeSelection(ctx.CollaborationMode, ctx.PermissionMode);

            if (ctx.PrintMode)
            {
                if (ctx.PromptTransportMode != PromptTransportMode.StreamJsonStdin)
                    args.Append("-p ");
                args.Append("--output-format stream-json ");
                args.Append("--include-partial-messages ");
                if (ctx.PromptTransportMode == PromptTransportMode.StreamJsonStdin)
                    args.Append("--input-format stream-json ");
            }

            if (!string.IsNullOrEmpty(ctx.Model))
                args.Append($"--model {ctx.Model} ");

            if (!string.IsNullOrEmpty(ctx.ReasoningEffort))
                args.Append($"--effort {ctx.ReasoningEffort} ");

            args.Append("--verbose ");

            if (ctx.PromptTransportMode == PromptTransportMode.StreamJsonStdin)
                args.Append("--permission-prompt-tool stdio ");

            if (ctx.MaxTurns > 0)
                args.Append($"--max-turns {ctx.MaxTurns} ");

            var cliPermissionMode = normalizedModes.CollaborationMode == "plan"
                ? "plan"
                : normalizedModes.PermissionMode;

            if (!string.IsNullOrEmpty(cliPermissionMode))
                args.Append($"--permission-mode {cliPermissionMode} ");

            if (ctx.ContinueSession)
                args.Append("-c ");
            else if (!string.IsNullOrEmpty(ctx.SessionId))
                args.Append($"-r \"{ctx.SessionId}\" ");

            if (ctx.EnableThinking && ctx.MaxThinkingTokens > 0)
                args.Append($"--max-thinking-tokens {ctx.MaxThinkingTokens} ");

            if (!string.IsNullOrEmpty(ctx.McpConfigArgs))
                args.Append($"{ctx.McpConfigArgs} ");

            if (ctx.IncludePrompt && !string.IsNullOrEmpty(ctx.Prompt))
                args.Append($"\"{ctx.Prompt.Replace("\"", "\\\"")}\"");

            return args.ToString().Trim();
        }

        public IStreamEventParser CreateEventParser()
        {
            return new StreamJsonEventRouter(CreateToolMapper());
        }

        public ICliHistoryProvider CreateHistoryProvider()
        {
            return new ClaudeHistoryProvider(CreateToolMapper());
        }

        public IReadOnlyList<string> GetDefaultCliPaths()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new List<string>
            {
                Path.Combine(home, ".claude", "local", "claude"),
                "/usr/local/bin/claude",
                "/usr/bin/claude",
                Path.Combine(home, ".local", "bin", "claude"),
                "claude",
            };
        }

        public (bool success, string message) ValidateCli(string cliPath, string workingDirectory)
        {
            try
            {
                var platform = ClaudePlatformFactory.GetPlatform();
                var resolvedPath = platform.ResolveCliPath(cliPath, GetDefaultCliPaths());

                if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
                    return (false, $"CLI not found at path: {resolvedPath}");

                var startInfo = platform.CreateProcessStartInfo(resolvedPath, "--version", workingDirectory);
                using var process = Process.Start(startInfo);
                if (process == null) return (false, "Failed to start process");

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);

                return process.ExitCode == 0
                    ? (true, $"Claude CLI found: {output.Trim()}")
                    : (false, $"CLI returned error: {error}");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to validate CLI: {ex.Message}");
            }
        }

        public string BuildPromptInput(
            string prompt,
            IReadOnlyList<ImageAttachment> attachments = null,
            IReadOnlyList<IContextAttachment> contextAttachments = null)
        {
            return ControlRequestHandler.BuildUserMessage(prompt, attachments, contextAttachments);
        }

        public ProcessStartInfo CreateProcessStartInfo(
            string cliPath, string arguments, string workingDirectory,
            bool debugMode, bool useBedrock)
        {
            var platform = ClaudePlatformFactory.GetPlatform();
            var resolvedPath = platform.ResolveCliPath(cliPath, GetDefaultCliPaths());

            ProcessStartInfo startInfo;
            if (debugMode)
            {
                startInfo = platform.CreateDebugProcessStartInfo(resolvedPath, arguments, workingDirectory);
            }
            else
            {
                startInfo = platform.CreateProcessStartInfo(resolvedPath, arguments, workingDirectory);

                if (useBedrock)
                    startInfo.EnvironmentVariables["CLAUDE_CODE_USE_BEDROCK"] = "1";
                else if (startInfo.EnvironmentVariables.ContainsKey("CLAUDE_CODE_USE_BEDROCK"))
                    startInfo.EnvironmentVariables.Remove("CLAUDE_CODE_USE_BEDROCK");
            }

            return startInfo;
        }

        public OnboardingInfo GetOnboardingInfo()
        {
            return new OnboardingInfo(
                "Anthropic's AI coding assistant",
                AuthOnboardingKind.OAuthBuiltIn,
                "Log in with your Anthropic account");
        }

        public IProviderCapabilitySources CreateCapabilitySources(ISettingsStore settingsStore, UseCases.Contracts.ILogger logger)
        {
            return new ClaudeCliCapabilitiesClient(settingsStore, logger);
        }

        private sealed class ClaudeToolMapper : ProviderToolMapperBase
        {
            public override string ProviderId => "claude";

            protected override ToolKind ResolveKind(string rawName, string rawTitle, string requestMethod, Newtonsoft.Json.Linq.JToken input)
            {
                return ToolPresentationCatalog.InferKind(rawTitle, rawName, rawName, input);
            }
        }
    }
}
