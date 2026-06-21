// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.Contracts;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.UseCases.Chat;
using Unity.AppUI.MVVM;
using Unity.Properties;

namespace Ryx.Sidekick.Editor.Presentation.ViewModels
{
    [ObservableObject]
    internal partial class ChatTimelineViewModel : IChatTimelineSink, IDisposable
    {
        private CancellationTokenSource _disposeCts = new();
        private bool _disposed;

        private bool _autoScrollEnabled_backing = true;
        private bool _showEmptyState_backing;
        private bool _showScrollToBottomButton_backing;
        private bool _isTypingIndicatorVisible_backing;

        // Provider-scope dependencies (attached per provider scope)
        private IChatTimelineView _view;
        private ChatSessionState _chatSessionState;
        private ConversationController _conversationController;
        private ChatController _chatController;

        // === Observable properties ===

        [CreateProperty]
        public bool AutoScrollEnabled
        {
            get => _autoScrollEnabled_backing;
            private set => SetProperty(ref _autoScrollEnabled_backing, value);
        }

        [CreateProperty]
        public bool ShowEmptyState
        {
            get => _showEmptyState_backing;
            private set => SetProperty(ref _showEmptyState_backing, value);
        }

        [CreateProperty]
        public bool ShowScrollToBottomButton
        {
            get => _showScrollToBottomButton_backing;
            private set => SetProperty(ref _showScrollToBottomButton_backing, value);
        }

        [CreateProperty]
        public bool IsTypingIndicatorVisible
        {
            get => _isTypingIndicatorVisible_backing;
            private set => SetProperty(ref _isTypingIndicatorVisible_backing, value);
        }

        // === Commands ===

        [ICommand]
        private void ScrollToLatest()
        {
            AutoScrollEnabled = true;
            SyncItems();
        }

        [ICommand]
        private void Refresh()
        {
            SyncItems();
        }

        // === Wiring ===

        public void BindView(IChatTimelineView view)
        {
            if (_view != null)
            {
                _view.ScrollChanged -= OnScrollChanged;
                _view.ScrollToBottomClicked -= OnScrollToBottomClicked;
            }

            _view = view;

            if (_view == null) return;

            _view.ScrollChanged += OnScrollChanged;
            _view.ScrollToBottomClicked += OnScrollToBottomClicked;

            PushInitialState();
            SyncItems();
        }

        private Action _retryHistoryAction;

        public void AttachProviderScope(
            ChatSessionState chatSessionState,
            ConversationController conversationController,
            Action retryHistoryAction = null)
        {
            DetachProviderScope();
            _chatSessionState = chatSessionState;
            _conversationController = conversationController;
            _retryHistoryAction = retryHistoryAction;

            if (_chatSessionState != null)
            {
                _chatSessionState.Changed += OnChatSessionStateChanged;
            }

            if (_conversationController != null)
            {
                _conversationController.HistoryLoadStatusChanged += OnHistoryLoadStatusChanged;
            }

            SyncItems();
            UpdateHistoryStatus();
        }

        public void DetachProviderScope()
        {
            if (_chatSessionState != null)
            {
                _chatSessionState.Changed -= OnChatSessionStateChanged;
            }

            if (_conversationController != null)
            {
                _conversationController.HistoryLoadStatusChanged -= OnHistoryLoadStatusChanged;
            }

            _chatSessionState = null;
            _conversationController = null;
            _retryHistoryAction = null;
        }

        public void AttachChatController(ChatController chatController)
        {
            DetachChatController();
            _chatController = chatController;
            if (_chatController != null)
            {
                _chatController.OnTurnActiveChanged += OnTurnActiveChanged;
                // Initial sync
                OnTurnActiveChanged(_chatController.TurnActive);
            }
        }

        public void DetachChatController()
        {
            if (_chatController != null)
            {
                _chatController.OnTurnActiveChanged -= OnTurnActiveChanged;
            }

            _chatController = null;
            IsTypingIndicatorVisible = false;
            _view?.SetTypingIndicatorVisible(false);
        }

        // === IChatTimelineSink (explicit) ===

        void IChatTimelineSink.AppendMessage(Message message)
        {
            _view?.RefreshAll();
            UpdateScrollButtonState();
        }

        void IChatTimelineSink.AppendToolMessage(Message toolMessage)
        {
            _view?.RefreshAll();
            UpdateScrollButtonState();
        }

        void IChatTimelineSink.UpdateStreamingMessage()
        {
            if (_view == null || _chatController == null) return;
            var id = _chatController.CurrentStreamingMessage?.Id;
            if (id == null || !_view.RefreshStreamingMessage(id)) _view?.RefreshAll();
            if (AutoScrollEnabled) _view?.RequestScrollToBottom(0);
        }

        void IChatTimelineSink.UpdateToolMessage(string toolUseId)
        {
            if (_view == null || string.IsNullOrEmpty(toolUseId)) return;
            if (!_view.RefreshToolById(toolUseId)) _view.RefreshAll();
        }

        void IChatTimelineSink.AppendThinkingMessage(Message thinkingMessage)
        {
            _view?.RefreshAll();
            UpdateScrollButtonState();
        }

        void IChatTimelineSink.UpdateThinkingMessage(string messageId)
        {
            if (_view == null || string.IsNullOrEmpty(messageId)) return;
            if (!_view.RefreshMessageById(messageId)) _view.RefreshAll();
        }

        void IChatTimelineSink.Refresh()
        {
            SyncItems();
        }

        // === IDisposable ===

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            BindView(null);
            DetachProviderScope();
            DetachChatController();

            _disposeCts?.Cancel();
            _disposeCts?.Dispose();
            _disposeCts = null;
        }

        // === Private implementation ===

        private void OnChatSessionStateChanged()
        {
            SyncItems();
        }

        private void OnHistoryLoadStatusChanged() => UpdateHistoryStatus();

        private void UpdateHistoryStatus()
        {
            if (_view == null) return;
            var status = _conversationController?.HistoryLoadStatus;
            var conversation = _chatSessionState?.CurrentConversation;
            var messages = conversation?.Messages;
            var isEmpty = messages == null || messages.Count == 0;

            var hasActiveStatus = status != null && status.State is
                ConversationHistoryLoadState.Initializing
                or ConversationHistoryLoadState.Loading
                or ConversationHistoryLoadState.Error;

            if (hasActiveStatus && isEmpty)
            {
                ShowEmptyState = false;
                _view.RenderHistoryStatus(status, () => _retryHistoryAction?.Invoke());
                PushInitialState();
            }
            else
            {
                _view.HideHistoryStatus();
            }
        }

        private void OnScrollToBottomClicked() => ScrollToLatestCommand.Execute(null);

        private void OnTurnActiveChanged(bool isActive)
        {
            IsTypingIndicatorVisible = isActive;
            _view?.SetTypingIndicatorVisible(isActive);
        }

        private void OnScrollChanged(ScrollDeltaSnapshot snapshot)
        {
            var newAutoScroll = snapshot.IsAtBottom;
            if (newAutoScroll != AutoScrollEnabled)
            {
                AutoScrollEnabled = newAutoScroll;
                ShowScrollToBottomButton = !newAutoScroll;
                PushInitialState();
            }
        }

        private void PushInitialState()
        {
            _view?.Render(new ChatTimelineViewState(AutoScrollEnabled, ShowEmptyState, ShowScrollToBottomButton));
        }

        private void UpdateScrollButtonState()
        {
            ShowScrollToBottomButton = !AutoScrollEnabled;
            PushInitialState();
        }

        private void SyncItems()
        {
            if (_view == null) return;
            var conversation = _chatSessionState?.CurrentConversation;
            var messages = conversation?.Messages;

            if (messages == null || messages.Count == 0)
            {
                _view.SetItems(System.Array.Empty<Message>(), conversation);
                var status = _conversationController?.HistoryLoadStatus;
                var hasActiveStatus = status != null && status.State is
                    ConversationHistoryLoadState.Initializing
                    or ConversationHistoryLoadState.Loading
                    or ConversationHistoryLoadState.Error;

                if (hasActiveStatus)
                {
                    ShowEmptyState = false;
                    _view.RenderHistoryStatus(status, () => _retryHistoryAction?.Invoke());
                }
                else
                {
                    ShowEmptyState = true;
                    _view.HideHistoryStatus();
                }

                ShowScrollToBottomButton = false;
                PushInitialState();
                return;
            }

            ShowEmptyState = false;
            _view.HideHistoryStatus();
            _view.SetItems(messages, conversation);
            ShowScrollToBottomButton = !AutoScrollEnabled;
            PushInitialState();
            if (AutoScrollEnabled)
            {
                _view.RequestScrollToBottom(0);
            }
        }
    }
}
