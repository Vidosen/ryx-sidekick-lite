// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>
    /// Outcome of a <see cref="IRuntimeOrchestrator.SendPromptAsync"/> call.
    /// </summary>
    internal enum PromptDispatchStatus
    {
        Started,
        RejectedAlreadyInProgress,
        RejectedValidation,
        RejectedStartupFailure
    }

    /// <summary>
    /// Result of a prompt dispatch attempt — distinguishes "started" from
    /// validation/startup rejections so callers can surface user-facing errors
    /// without inspecting log output.
    /// </summary>
    internal readonly struct PromptDispatchResult
    {
        public PromptDispatchResult(PromptDispatchStatus status, string errorMessage = null)
        {
            Status = status;
            ErrorMessage = errorMessage;
        }

        public PromptDispatchStatus Status { get; }

        public string ErrorMessage { get; }

        public bool IsStarted => Status == PromptDispatchStatus.Started;

        public static PromptDispatchResult Started() => new(PromptDispatchStatus.Started);
    }

    /// <summary>
    /// Provider-agnostic runtime contract that orchestrates a single CLI / ACP
    /// session — spawning the process, marshalling stream events, sending
    /// user/permission/tool responses. Implemented by <c>ProcessManager</c>
    /// (Claude/Codex/Cursor) and tests' fakes.
    /// </summary>
    internal interface IRuntimeOrchestrator : IDisposable
    {
        event Action<string> OnRawOutput;
        event Action OnAssistantMessageStarted;
        event Action<string> OnTextDelta;
        event Action<ToolUse> OnToolUse;
        event Action<string, string> OnToolResult;
        event Action<PendingPermission> OnPermissionRequest;
        event Action<string> OnError;
        event Action OnStreamComplete;
        event Action<ImageAttachment> OnImageAttachment;
        event Action OnTurnStarted;
        event Action OnTurnFinished;
        event Action OnThinkingStarted;
        event Action<string> OnThinkingDelta;
        event Action<string> OnThinkingCompleted;
        event Action<int, int> OnContextUsageUpdated;

        bool IsRunning { get; }
        bool IsTurnInProgress { get; }
        string CurrentSessionId { get; }

        Task<PromptDispatchResult> SendPromptAsync(
            string prompt,
            string sessionId = null,
            IReadOnlyList<ImageAttachment> attachments = null,
            IReadOnlyList<IContextAttachment> contextAttachments = null);
        
        void SendPermissionResponse(PendingPermission permission, bool allow, string message = null, bool remember = false);
        void SendUserInputResponse(PendingPermission permission, JObject response);
        void SendControlResponse(string requestId, string toolUseId, bool allow, JToken updatedInput = null, string message = null);
        Task InterruptAsync();
        void Stop();
    }
}
