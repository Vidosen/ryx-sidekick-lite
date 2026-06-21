// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Presenters
{
    /// <summary>
    /// Owns the conversation popup orchestration (toggle/open/close + click-outside detection)
    /// and the "stop active turn before creating a new conversation" UX policy. Wraps
    /// <see cref="ConversationController"/> calls so the policy lives in the presentation
    /// layer rather than leaking into the controller.
    /// </summary>
    /// <remarks>
    /// Extracted from <c>SidekickWindow.Popup.cs</c> + <c>SidekickWindow.Conversations.cs</c>
    /// during APPUI-T11-02g. The two dead wrappers (<c>SelectConversation</c>, <c>DeleteConversation</c>,
    /// <c>LoadConversations</c>) were removed during extraction — selection now flows directly
    /// through <see cref="IConversationMenuView.ConversationSelected"/> → <see cref="ConversationController"/>,
    /// and the legacy stop-turn-on-select guard was never reachable from the live UI.
    /// </remarks>
    internal sealed class ConversationPopupPresenter : IDisposable
    {
        private readonly Label _dropdownTitle;

        private ConversationController _conversationController;
        private ChatController _chatController;
        private bool _disposed;

        public ConversationPopupPresenter(Label dropdownTitle)
        {
            _dropdownTitle = dropdownTitle;
        }

        /// <summary>
        /// Rebinds the presenter to a new provider scope's controllers. Called by
        /// <see cref="SidekickEditorAppHost"/>.<c>RebuildProviderScope</c> after the new
        /// <see cref="ConversationController"/> and <see cref="ChatController"/> are constructed.
        /// </summary>
        public void RebindProviderScope(
            ConversationController conversationController,
            ChatController chatController)
        {
            if (_conversationController != null)
            {
                _conversationController.CurrentConversationChanged -= UpdateDropdownTitle;
            }

            _conversationController = conversationController;
            _chatController = chatController;

            if (_conversationController != null)
            {
                _conversationController.CurrentConversationChanged += UpdateDropdownTitle;
            }

            UpdateDropdownTitle();
        }

        public void ToggleConversationPopup()
        {
            _conversationController?.TogglePopup();
        }

        public async void OpenConversationPopup()
        {
            if (_conversationController != null)
            {
                await _conversationController.OpenPopupAsync();
            }
        }

        public void CloseConversationPopup()
        {
            _conversationController?.ClosePopup();
        }

        public bool IsClickInsidePopup(ClickEvent evt)
        {
            return _conversationController?.IsClickInsidePopup(evt) ?? false;
        }

        /// <summary>
        /// Refreshes the conversation list from CLI storage without changing current conversation.
        /// Kept as a thin pass-through for command palette / focus handlers.
        /// </summary>
        public async void RefreshSessionList()
        {
            if (_conversationController != null)
            {
                await _conversationController.RefreshOnFocusAsync();
            }
        }

        /// <summary>
        /// Creates a new conversation, stopping any active turn first so the in-flight stream
        /// does not bleed into the new conversation context.
        /// </summary>
        public void CreateNewConversation()
        {
            if (_chatController?.IsTurnInProgress == true)
            {
                _chatController.StopRequest();
            }

            _conversationController?.CreateNewConversation();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_conversationController != null)
            {
                _conversationController.CurrentConversationChanged -= UpdateDropdownTitle;
                _conversationController = null;
            }

            _chatController = null;
        }

        private void UpdateDropdownTitle()
        {
            if (_dropdownTitle == null)
            {
                return;
            }

            var current = _conversationController?.CurrentConversation;
            if (current != null && !string.IsNullOrEmpty(current.Title) && current.Title != "New Chat")
            {
                _dropdownTitle.text = current.Title;
            }
            else
            {
                _dropdownTitle.text = "Past Conversations";
            }
        }
    }
}
