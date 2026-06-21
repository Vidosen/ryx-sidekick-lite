// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.Providers
{
    internal enum AuthOnboardingKind
    {
        OAuthBuiltIn,
        CliCommand,
        ExternalUrl
    }

    internal class OnboardingInfo
    {
        public string ProviderDescription { get; }
        public AuthOnboardingKind AuthKind { get; }
        public string AuthDescription { get; }
        public string AuthLoginArg { get; }
        public string AuthUrl { get; }
        public string AuthStatusArgs { get; }
        public Func<string, bool> IsAuthenticatedFromOutput { get; }

        public OnboardingInfo(
            string providerDescription,
            AuthOnboardingKind authKind,
            string authDescription,
            string authLoginArg = null,
            string authUrl = null,
            string authStatusArgs = null,
            Func<string, bool> isAuthenticatedFromOutput = null)
        {
            ProviderDescription = providerDescription;
            AuthKind = authKind;
            AuthDescription = authDescription;
            AuthLoginArg = authLoginArg;
            AuthUrl = authUrl;
            AuthStatusArgs = authStatusArgs;
            IsAuthenticatedFromOutput = isAuthenticatedFromOutput;
        }
    }

    internal enum PromptTransportMode
    {
        Argument,
        PlainTextStdin,
        StreamJsonStdin
    }

    internal enum ProviderRuntimeTransport
    {
        CliProcess,
        AppServer,
        PersistentJsonRpcSession
    }

    internal interface ISessionRuntimeClient : System.IDisposable
    {
        event System.Action<string> OnRawOutput;
        event System.Action<StreamEvent> OnStreamEvent;
        event System.Action OnAssistantMessageStarted;
        event System.Action<string> OnTextDelta;
        event System.Action<ToolUse> OnToolUse;
        event System.Action<string, string> OnToolResult;
        event System.Action<PendingPermission> OnPermissionRequest;
        event System.Action<ResultEvent> OnResult;
        event System.Action OnThinkingStarted;
        event System.Action<string> OnThinkingDelta;
        event System.Action<string> OnThinkingCompleted;
        event System.Action<int, int> OnContextUsageUpdated;
        event System.Action<string> OnSessionIdReceived;
        event System.Action<string> OnError;
        event System.Action OnProcessStarted;
        event System.Action<int> OnProcessExited;

        bool IsRunning { get; }
        string CurrentSessionId { get; }

        Task<bool> RunTurnAsync(
            string prompt,
            string sessionId,
            IReadOnlyList<ImageAttachment> attachments,
            IReadOnlyList<IContextAttachment> contextAttachments,
            CliInvocationSettings settings,
            IReadOnlyDictionary<string, JObject> mcpServers = null);

        void SendApprovalResponse(PendingPermission permission, bool allow, string message = null, bool remember = false);
        void SendUserInputResponse(PendingPermission permission, JObject response);
        Task InterruptAsync();
        void Stop();
    }

    internal sealed class PersistentTurnStartAck
    {
        public PersistentTurnStartAck(bool isStarted, string errorMessage, string resolvedSessionId, Task<bool> completionTask)
        {
            IsStarted = isStarted;
            ErrorMessage = errorMessage;
            ResolvedSessionId = resolvedSessionId;
            CompletionTask = completionTask ?? Task.FromResult(false);
        }

        public bool IsStarted { get; }
        public string ErrorMessage { get; }
        public string ResolvedSessionId { get; }
        public Task<bool> CompletionTask { get; }

        public static PersistentTurnStartAck Started(string resolvedSessionId, Task<bool> completionTask)
        {
            return new PersistentTurnStartAck(true, null, resolvedSessionId, completionTask);
        }

        public static PersistentTurnStartAck Rejected(string errorMessage, string resolvedSessionId = null)
        {
            return new PersistentTurnStartAck(false, errorMessage, resolvedSessionId, Task.FromResult(false));
        }
    }

    internal interface IPersistentTurnStarter
    {
        Task<PersistentTurnStartAck> StartTurnAsync(
            string prompt,
            string sessionId,
            IReadOnlyList<ImageAttachment> attachments,
            IReadOnlyList<IContextAttachment> contextAttachments,
            CliInvocationSettings settings,
            IReadOnlyDictionary<string, JObject> mcpServers = null);
    }

    internal interface IProviderToolMapper
    {
        string ProviderId { get; }

        void Normalize(ToolUse toolUse);
        void Normalize(PendingPermission permission);
        void Normalize(Conversation conversation);
    }

    internal abstract class ProviderToolMapperBase : IProviderToolMapper
    {
        public abstract string ProviderId { get; }

        public void Normalize(ToolUse toolUse)
        {
            if (toolUse == null)
            {
                return;
            }

            var rawName = FirstNonEmpty(toolUse.RawName, toolUse.Name);
            var rawTitle = FirstNonEmpty(toolUse.RawTitle);
            var kind = toolUse.Kind != ToolKind.Unknown
                ? toolUse.Kind
                : ResolveKind(rawName, rawTitle, requestMethod: null, toolUse.Input);

            toolUse.ProviderId = ProviderId;
            toolUse.Kind = kind;
            toolUse.RawName = rawName;
            toolUse.RawTitle = rawTitle;
            toolUse.DecisionKey = ToolPresentationCatalog.BuildDecisionKey(ProviderId, kind, rawTitle, rawName);
            toolUse.Name = ToolPresentationCatalog.ResolveCanonicalOrFallbackName(kind, rawTitle, rawName);
        }

        public void Normalize(PendingPermission permission)
        {
            if (permission == null)
            {
                return;
            }

            var rawName = FirstNonEmpty(permission.RawToolName, permission.ToolName);
            var rawTitle = FirstNonEmpty(permission.RawToolTitle);
            var kind = permission.ToolKind != ToolKind.Unknown
                ? permission.ToolKind
                : ResolveKind(rawName, rawTitle, permission.RequestMethod, permission.Input);

            permission.ProviderId = ProviderId;
            permission.ToolKind = kind;
            permission.RawToolName = rawName;
            permission.RawToolTitle = rawTitle;
            permission.DecisionKey = ToolPresentationCatalog.BuildDecisionKey(ProviderId, kind, rawTitle, rawName);
            permission.ToolName = ToolPresentationCatalog.ResolveCanonicalOrFallbackName(kind, rawTitle, rawName);
        }

        public void Normalize(Conversation conversation)
        {
            if (conversation?.Messages == null)
            {
                return;
            }

            foreach (var message in conversation.Messages)
            {
                Normalize(message);
            }
        }

        protected abstract ToolKind ResolveKind(string rawName, string rawTitle, string requestMethod, JToken input);

        protected virtual void Normalize(Message message)
        {
            if (message?.ToolUses == null)
            {
                return;
            }

            foreach (var toolUse in message.ToolUses)
            {
                Normalize(toolUse);
            }
        }

        protected static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return null;
            }

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }
    }

    internal static class ToolPresentationCatalog
    {
        public static ToolKind GetEffectiveKind(ToolUse toolUse)
        {
            if (toolUse == null)
            {
                return ToolKind.Unknown;
            }

            return ResolveEffectiveKind(toolUse.Kind, toolUse.RawTitle, toolUse.RawName, toolUse.Name, toolUse.Input);
        }

        public static ToolKind GetEffectiveKind(PendingPermission permission)
        {
            if (permission == null)
            {
                return ToolKind.Unknown;
            }

            return ResolveEffectiveKind(
                permission.ToolKind,
                permission.RawToolTitle,
                permission.RawToolName,
                permission.ToolName,
                permission.Input);
        }

        public static string GetIconKey(ToolKind kind)
        {
            return kind switch
            {
                ToolKind.Read => "tool-read",
                ToolKind.Edit => "tool-edit",
                ToolKind.Write => "tool-write",
                ToolKind.Move => "tool-folder",
                ToolKind.Bash => "tool-bash",
                ToolKind.Search => "tool-search",
                ToolKind.ListDirectory => "tool-folder",
                ToolKind.Todo => "tool-todo",
                ToolKind.WebFetch => "tool-web",
                ToolKind.WebSearch => "tool-search",
                ToolKind.ImplementPlan => "mode-plan",
                ToolKind.ExitPlanMode => "mode-plan",
                _ => "tool-default"
            };
        }

        public static string GetCanonicalName(ToolKind kind)
        {
            return kind switch
            {
                ToolKind.Read => "Read",
                ToolKind.Write => "Write",
                ToolKind.Edit => "Edit",
                ToolKind.Move => "Move",
                ToolKind.Bash => "Bash",
                ToolKind.Search => "Search",
                ToolKind.ListDirectory => "LS",
                ToolKind.Todo => "TodoWrite",
                ToolKind.AskUserQuestion => "AskUserQuestion",
                ToolKind.ImplementPlan => "ImplementPlan",
                ToolKind.ExitPlanMode => "ExitPlanMode",
                ToolKind.WebFetch => "WebFetch",
                ToolKind.WebSearch => "WebSearch",
                ToolKind.Delete => "Delete",
                _ => "tool"
            };
        }

        public static string ResolveCanonicalOrFallbackName(ToolKind kind, string rawTitle, string rawName)
        {
            return kind == ToolKind.Unknown
                ? ResolveRawFallbackName(rawTitle, rawName)
                : GetCanonicalName(kind);
        }

        public static string ResolveRawFallbackName(params string[] candidates)
        {
            if (candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return "tool";
        }

        public static string BuildDecisionKey(string providerId, ToolKind kind, params string[] rawCandidates)
        {
            var normalizedProviderId = string.IsNullOrWhiteSpace(providerId)
                ? "unknown"
                : providerId.Trim().ToLowerInvariant();

            if (kind != ToolKind.Unknown)
            {
                return $"{normalizedProviderId}:{kind.ToString().ToLowerInvariant()}";
            }

            var rawKey = ResolveRawFallbackName(rawCandidates).Trim().ToLowerInvariant();
            return $"{normalizedProviderId}:raw:{rawKey}";
        }

        public static bool LooksLikeTodoInput(JToken input)
        {
            if (input is JObject obj)
            {
                return obj["todos"] is JArray todos && todos.Count > 0;
            }

            return false;
        }

        public static bool LooksLikeQuestionInput(JToken input)
        {
            if (input is JObject obj)
            {
                return obj["questions"] is JArray questions && questions.Count > 0;
            }

            return false;
        }

        public static ToolKind InferKind(string rawTitle, string rawName, string displayName, JToken input)
        {
            if (LooksLikeTodoInput(input))
            {
                return ToolKind.Todo;
            }

            if (LooksLikeQuestionInput(input))
            {
                return ToolKind.AskUserQuestion;
            }

            var candidate = NormalizeKey(rawTitle)
                ?? NormalizeKey(rawName)
                ?? NormalizeKey(displayName);

            if (string.IsNullOrEmpty(candidate))
            {
                return InferKindFromInputShape(input);
            }

            var inferred = candidate switch
            {
                "read" or "readtoolcall" or "readfile" => ToolKind.Read,
                "write" or "writetoolcall" or "writefile" or "createfile" => ToolKind.Write,
                "edit" or "edittoolcall" or "editfile" or "patchfile" => ToolKind.Edit,
                "move" or "movetoolcall" or "movefile" or "rename" or "renamefile" => ToolKind.Move,
                "bash" or "terminal" or "runterminalcommandtoolcall" or "commandexecution" or "execcommand" => ToolKind.Bash,
                "glob" or "grep" or "search" or "searchtoolcall" => ToolKind.Search,
                "ls" or "listdirectory" or "listdirectorytoolcall" => ToolKind.ListDirectory,
                "todowrite" or "todoread" or "tasks" or "cursorupdatetodos" => ToolKind.Todo,
                "askuserquestion" or "requestuserinput" => ToolKind.AskUserQuestion,
                "implementplan" or "requestimplementation" => ToolKind.ImplementPlan,
                "exitplanmode" or "switchmode" or "plan" => ToolKind.ExitPlanMode,
                "webfetch" or "fetch" => ToolKind.WebFetch,
                "websearch" => ToolKind.WebSearch,
                "delete" or "deletetoolcall" or "deletefile" => ToolKind.Delete,
                _ => ToolKind.Unknown
            };

            return inferred != ToolKind.Unknown
                ? inferred
                : InferKindFromInputShape(input);
        }

        private static ToolKind ResolveEffectiveKind(ToolKind kind, string rawTitle, string rawName, string displayName, JToken input)
        {
            return kind != ToolKind.Unknown
                ? kind
                : InferKind(rawTitle, rawName, displayName, input);
        }

        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim()
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .Replace("_", string.Empty)
                .Replace("/", string.Empty)
                .ToLowerInvariant();
        }

        private static ToolKind InferKindFromInputShape(JToken input)
        {
            if (input is not JObject obj)
            {
                return ToolKind.Unknown;
            }

            if (obj["command"] != null)
            {
                return ToolKind.Bash;
            }

            if (obj["pattern"] != null)
            {
                return ToolKind.Search;
            }

            if (obj["old_string"] != null || obj["new_string"] != null)
            {
                return ToolKind.Edit;
            }

            if (obj["content"] != null)
            {
                return ToolKind.Write;
            }

            if (obj["plan"] != null)
            {
                return ToolKind.ExitPlanMode;
            }

            if (obj["path"] != null || obj["file_path"] != null || obj["filePath"] != null)
            {
                return ToolKind.Read;
            }

            return ToolKind.Unknown;
        }
    }

    /// <summary>
    /// Abstraction for a CLI backend (e.g. Claude Code, Cursor Agent).
    /// Encapsulates argument building, event parsing, and history reading.
    /// </summary>
    internal interface ICliProvider
    {
        /// <summary>Unique stable identifier, e.g. "claude" or "cursor".</summary>
        string Id { get; }

        /// <summary>Human-readable name shown in UI.</summary>
        string DisplayName { get; }

        /// <summary>Default binary name used when resolving CLI path.</summary>
        string DefaultBinaryName { get; }

        /// <summary>URL to installation instructions, shown in onboarding.</summary>
        string InstallUrl { get; }

        /// <summary>Short model preset IDs, e.g. ["sonnet", "opus", "haiku"].</summary>
        string[] ModelPresets { get; }

        /// <summary>Default model preset to use when first setting up.</summary>
        string DefaultModel { get; }

        /// <summary>
        /// Catalog used before (or instead of) a live <see cref="IProviderModelCatalogSource"/> fetch.
        /// Providers with model-specific reasoning efforts override this; the default exposes the static
        /// presets without efforts.
        /// </summary>
        ProviderModelCatalog BuildFallbackModelCatalog() =>
            ProviderModelCatalogFactory.FromPresets(Id, ModelPresets, DefaultModel);

        /// <summary>Available collaboration modes for this provider.</summary>
        CollaborationModeDescriptor[] CollaborationModes { get; }

        /// <summary>Returns available permission modes for the given collaboration mode.</summary>
        PermissionModeDescriptor[] GetPermissionModes(string collaborationMode);

        /// <summary>Normalizes a collaboration/permission selection pair for this provider.</summary>
        ProviderModeSelection NormalizeModeSelection(string collaborationMode, string permissionMode);

        /// <summary>True when the given permission mode should auto-approve interactive prompts.</summary>
        bool IsAutoApprovePermissionMode(string permissionMode);

        /// <summary>How the provider runtime is hosted.</summary>
        ProviderRuntimeTransport RuntimeTransport { get; }

        /// <summary>True if provider supports interactive permission prompts via stdin.</summary>
        bool SupportsInteractivePermissions { get; }

        /// <summary>How prompts are delivered to the provider.</summary>
        PromptTransportMode PromptTransportMode { get; }

        /// <summary>True if provider expects image attachments as staged file paths in CLI arguments.</summary>
        bool UsesImageAttachmentFilePaths { get; }

        /// <summary>True if provider supports extended thinking mode.</summary>
        bool SupportsThinking { get; }

        /// <summary>True if provider supports MCP config injection.</summary>
        bool SupportsMcpConfig { get; }

        /// <summary>True if provider supports --verbose flag.</summary>
        bool SupportsVerbose { get; }

        /// <summary>Creates a persistent session runtime client when <see cref="RuntimeTransport"/> requires one.</summary>
        ISessionRuntimeClient CreateSessionRuntimeClient();

        /// <summary>Creates the provider-specific mapper that normalizes raw tool metadata.</summary>
        IProviderToolMapper CreateToolMapper();

        /// <summary>
        /// Builds CLI argument string for the given context.
        /// </summary>
        string BuildArguments(CliArgumentContext ctx);

        /// <summary>
        /// Creates a fresh event parser for this provider's stream-json format.
        /// </summary>
        IStreamEventParser CreateEventParser();

        /// <summary>
        /// Creates a history provider for reading conversation storage.
        /// </summary>
        ICliHistoryProvider CreateHistoryProvider();

        /// <summary>
        /// Returns candidate absolute paths to the CLI binary on the current platform.
        /// </summary>
        IReadOnlyList<string> GetDefaultCliPaths();

        /// <summary>
        /// Validates the CLI binary at the given path and returns (success, message).
        /// </summary>
        (bool success, string message) ValidateCli(string cliPath, string workingDirectory);

        /// <summary>
        /// Builds the stdin payload for prompt delivery when the provider does not use CLI arguments.
        /// Returns null for providers that pass the prompt as a CLI argument.
        /// </summary>
        string BuildPromptInput(
            string prompt,
            IReadOnlyList<ImageAttachment> attachments = null,
            IReadOnlyList<IContextAttachment> contextAttachments = null);

        /// <summary>
        /// Creates a ProcessStartInfo for launching the CLI.
        /// </summary>
        ProcessStartInfo CreateProcessStartInfo(string cliPath, string arguments, string workingDirectory, bool debugMode, bool useBedrock);

        /// <summary>
        /// Returns onboarding metadata (card description, auth variant, status check info).
        /// </summary>
        OnboardingInfo GetOnboardingInfo();
    }
}
