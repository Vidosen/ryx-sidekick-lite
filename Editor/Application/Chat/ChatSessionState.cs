// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Chat
{
    internal sealed class ChatSessionState
    {
        private readonly IChatConversationSession _conversationSession;
        private readonly ISettingsStore _settingsStore;
        private readonly Func<bool> _isTurnInProgress;

        public event Action Changed;

        public ChatSessionState(
            IChatConversationSession conversationSession,
            ISettingsStore settingsStore,
            Func<bool> isTurnInProgress)
        {
            _conversationSession = conversationSession;
            _settingsStore = settingsStore;
            _isTurnInProgress = isTurnInProgress;
            if (_conversationSession != null)
            {
                _conversationSession.Changed += OnSessionChanged;
            }
        }

        private void OnSessionChanged()
        {
            Changed?.Invoke();
        }

        public Conversation CurrentConversation => _conversationSession?.CurrentConversation;

        public bool IsCurrentConversationLoading => _conversationSession?.IsCurrentConversationLoading == true;

        public bool IsTurnInProgress => _isTurnInProgress?.Invoke() == true;

        public (Conversation conversation, bool created) EnsureConversation()
        {
            return _conversationSession?.EnsureConversation() ?? (null, false);
        }

        public static Message AppendUserMessage(
            Conversation conversation,
            string text,
            IReadOnlyList<ImageAttachment> attachments,
            IReadOnlyList<IContextAttachment> contextAttachments)
        {
            if (conversation == null)
            {
                return null;
            }

            var userMessage = new Message
            {
                Role = MessageRole.User,
                Content = text,
                Attachments = attachments?.Where(attachment => attachment != null).ToList() ?? new List<ImageAttachment>(),
                ContextAttachments = contextAttachments?.Where(attachment => attachment != null).ToList() ?? new List<IContextAttachment>()
            };

            conversation.Messages.Add(userMessage);
            conversation.UpdatedAt = DateTime.Now;

            if (conversation.Messages.Count == 1)
            {
                conversation.Title = BuildInitialConversationTitle(text, attachments);
            }

            return userMessage;
        }

        public bool TryCaptureRuntimeSessionId(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId) ||
                CurrentConversation == null ||
                !string.IsNullOrEmpty(CurrentConversation.SessionId))
            {
                return false;
            }

            CurrentConversation.SessionId = sessionId;
            CurrentConversation.Id = sessionId;
            _settingsStore.LastOpenedSessionId = sessionId;
            return true;
        }

        public bool ApplyRuntimeSessionId(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId) || CurrentConversation == null)
            {
                return false;
            }

            CurrentConversation.SessionId = sessionId;
            CurrentConversation.Id = sessionId;
            _settingsStore.LastOpenedSessionId = sessionId;
            return true;
        }

        private static string BuildInitialConversationTitle(string text, IReadOnlyList<ImageAttachment> attachments)
        {
            if (!string.IsNullOrEmpty(text))
            {
                return text.Length > 40 ? text.Substring(0, 40) + "..." : text;
            }

            var attachmentCount = attachments?.Count(attachment => attachment != null) ?? 0;
            if (attachmentCount > 0)
            {
                return attachmentCount == 1 ? "Image" : $"Images ({attachmentCount})";
            }

            return "New Chat";
        }
    }
}
