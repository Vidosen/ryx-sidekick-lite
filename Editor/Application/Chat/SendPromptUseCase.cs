// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Chat
{
    internal enum SendPromptStatus
    {
        IgnoredEmpty,
        RequiresAuthentication,
        ConversationLoading,
        TurnAlreadyInProgress,
        Started,
        RuntimeStartFailed
    }

    internal sealed class SendPromptRequest
    {
        public string Text { get; set; }
        public IReadOnlyList<ImageAttachment> Attachments { get; set; }
        public IReadOnlyList<IContextAttachment> ContextAttachments { get; set; }
        public bool RequiresAuthentication { get; set; }
    }

    internal sealed class SendPromptResult
    {
        public SendPromptResult(
            SendPromptStatus status,
            bool conversationCreated = false,
            bool userMessageAdded = false,
            bool shouldClearComposer = false,
            string errorMessage = null)
        {
            Status = status;
            ConversationCreated = conversationCreated;
            UserMessageAdded = userMessageAdded;
            ShouldClearComposer = shouldClearComposer;
            ErrorMessage = errorMessage;
        }

        public SendPromptStatus Status { get; }
        public bool ConversationCreated { get; }
        public bool UserMessageAdded { get; }
        public bool ShouldClearComposer { get; }
        public string ErrorMessage { get; }
    }

    internal sealed class SendPromptUseCase
    {
        private readonly IRuntimeOrchestrator _runtimeOrchestrator;
        private readonly ChatSessionState _chatSessionState;

        public SendPromptUseCase(
            IRuntimeOrchestrator runtimeOrchestrator,
            ChatSessionState chatSessionState)
        {
            _runtimeOrchestrator = runtimeOrchestrator;
            _chatSessionState = chatSessionState;
        }

        public async Task<SendPromptResult> ExecuteAsync(SendPromptRequest request)
        {
            var text = (request?.Text ?? string.Empty).Trim();
            var attachments = request?.Attachments;
            if (attachments != null && attachments.Any(attachment => attachment == null))
            {
                attachments = attachments.Where(attachment => attachment != null).ToList();
            }

            var contextAttachments = request?.ContextAttachments;
            if (contextAttachments != null && contextAttachments.Any(attachment => attachment == null))
            {
                contextAttachments = contextAttachments.Where(attachment => attachment != null).ToList();
            }

            if (string.IsNullOrEmpty(text)
                && (attachments == null || attachments.Count == 0)
                && (contextAttachments == null || contextAttachments.Count == 0))
            {
                return new SendPromptResult(SendPromptStatus.IgnoredEmpty);
            }

            if (request?.RequiresAuthentication == true)
            {
                return new SendPromptResult(SendPromptStatus.RequiresAuthentication);
            }

            if (_chatSessionState.IsTurnInProgress)
            {
                return new SendPromptResult(SendPromptStatus.TurnAlreadyInProgress);
            }

            var (conversation, conversationCreated) = _chatSessionState.EnsureConversation();
            if (conversation == null)
            {
                return new SendPromptResult(SendPromptStatus.RuntimeStartFailed);
            }

            if (_chatSessionState.IsCurrentConversationLoading)
            {
                return new SendPromptResult(
                    SendPromptStatus.ConversationLoading,
                    conversationCreated: conversationCreated);
            }

            var userMessage = ChatSessionState.AppendUserMessage(
                conversation,
                text,
                attachments,
                contextAttachments);

            if (_runtimeOrchestrator == null)
            {
                return new SendPromptResult(
                    SendPromptStatus.RuntimeStartFailed,
                    conversationCreated: conversationCreated,
                    userMessageAdded: userMessage != null);
            }

            var dispatchResult = await _runtimeOrchestrator.SendPromptAsync(
                text,
                sessionId: conversation.SessionId,
                attachments: attachments,
                contextAttachments: contextAttachments);

            var status = dispatchResult.Status switch
            {
                PromptDispatchStatus.Started => SendPromptStatus.Started,
                PromptDispatchStatus.RejectedAlreadyInProgress => SendPromptStatus.TurnAlreadyInProgress,
                _ => SendPromptStatus.RuntimeStartFailed
            };

            return new SendPromptResult(
                status,
                conversationCreated: conversationCreated,
                userMessageAdded: userMessage != null,
                shouldClearComposer: dispatchResult.IsStarted,
                errorMessage: dispatchResult.ErrorMessage);
        }
    }
}
