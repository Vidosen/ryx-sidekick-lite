// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Presentation.Renderers;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    internal interface IChatTimelineView
    {
        // Push-style state
        void Render(ChatTimelineViewState state);

        // History status
        void RenderHistoryStatus(ConversationLoadStatus<ConversationHistoryLoadState> status, Action onRetry);
        void HideHistoryStatus();

        // Typing indicator
        void SetTypingIndicatorVisible(bool visible);

        // Scroll
        void RequestScrollToBottom(int delayMs = 50);

        // Events
        event Action ScrollToBottomClicked;
        event Action<ScrollDeltaSnapshot> ScrollChanged;

        // ListView surface
        void SetItems(System.Collections.Generic.IList<Message> messages, Conversation conversation);
        void RefreshAll();
        bool RefreshMessageById(string messageId);
        bool RefreshToolById(string toolUseId);
        bool RefreshStreamingMessage(string messageId);
        void AddOverlayBanner(VisualElement banner);
        VisualElement FindOverlayBanner(string name);
    }

    internal readonly struct ChatTimelineViewState
    {
        public ChatTimelineViewState(bool autoScrollEnabled, bool showEmptyState, bool showScrollToBottomButton)
        {
            AutoScrollEnabled = autoScrollEnabled;
            ShowEmptyState = showEmptyState;
            ShowScrollToBottomButton = showScrollToBottomButton;
        }

        public bool AutoScrollEnabled { get; }

        public bool ShowEmptyState { get; }

        public bool ShowScrollToBottomButton { get; }
    }

    internal sealed class ChatTimelineView : IChatTimelineView
    {
        private const float AutoScrollBottomThreshold = 4f;

        private readonly VisualElement _welcomeScreen;
        private readonly VisualElement _scrollToBottomContainer;
        private readonly Button _scrollToBottomButton;
        private IMessageElementFactory _messageElementFactory;

        // ListView path fields
        private readonly ListView _messageListView;
        private readonly VisualElement _historyStatusHost;
        private readonly VisualElement _permissionBannerHost;
        // The list ListView binds to: a copy of the live domain messages plus an optional
        // trailing typing-indicator sentinel while a turn is active.
        private readonly List<Message> _viewItems = new();
        private IList<Message> _sourceMessages;
        private bool _typingActive;
        // Identity marker rendered as the trailing "assistant is working" row inside the list.
        private readonly Message _typingSentinel = new Message { Role = MessageRole.Assistant };
        private Conversation _conversation;
        private ScrollView _innerScroll;
        private Action<float> _innerScrollerCallback;

        private bool _autoScrollEnabled = true;
        private bool _suppressScrollEvents;
        private bool _welcomeWasShown;

        public ChatTimelineView(
            VisualElement welcomeScreen,
            VisualElement scrollToBottomContainer,
            Button scrollToBottomButton,
            ListView messageListView,
            VisualElement historyStatusHost,
            VisualElement permissionBannerHost)
        {
            _welcomeScreen = welcomeScreen;
            _scrollToBottomContainer = scrollToBottomContainer;
            _scrollToBottomButton = scrollToBottomButton;
            _messageListView = messageListView;
            _historyStatusHost = historyStatusHost;
            _permissionBannerHost = permissionBannerHost;

            SetupListView();

            _scrollToBottomButton?.RegisterCallback<ClickEvent>(_ =>
            {
                ScrollToBottomClicked?.Invoke();
            });
        }

        public event Action ScrollToBottomClicked;
        public event Action<ScrollDeltaSnapshot> ScrollChanged;

        public void SetMessageElementFactory(IMessageElementFactory factory)
        {
            _messageElementFactory = factory;
        }

        public void Detach()
        {
            if (_innerScroll != null && _innerScrollerCallback != null)
            {
                _innerScroll.verticalScroller.valueChanged -= _innerScrollerCallback;
                _innerScroll = null;
                _innerScrollerCallback = null;
            }
        }

        // === Scroll helpers ===

        private void SetupListView()
        {
            if (_messageListView == null) return;
            _messageListView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            _messageListView.selectionType = SelectionType.None;
            _messageListView.makeItem = () =>
            {
                var slot = new VisualElement();
                slot.style.flexShrink = 0;
                return slot;
            };
            _messageListView.bindItem = (slot, index) =>
            {
                slot.Clear();
                if (index < 0 || index >= _viewItems.Count) return;
                var m = _viewItems[index];
                if (m == null) return;
                if (ReferenceEquals(m, _typingSentinel))
                {
                    slot.Add(BuildTypingRow());
                    return;
                }
                var el = _messageElementFactory?.CreateMessageElement(
                    m, TimelineRenderHelpers.ComputeShowRoleHeader(_conversation, index));
                if (el != null)
                {
                    el.name = TimelineRenderHelpers.GetElementName(m);
                    slot.Add(el);
                }
            };
            _messageListView.unbindItem = (slot, index) => slot.Clear();
            _messageListView.schedule.Execute(SetupListViewScroll).ExecuteLater(0);
        }

        private void SetupListViewScroll()
        {
            if (_innerScroll != null) return;
            _innerScroll = _messageListView?.Q<ScrollView>();
            if (_innerScroll == null) return;
            _innerScrollerCallback = _ => RaiseScrollChangedFromInner();
            _innerScroll.verticalScroller.valueChanged += _innerScrollerCallback;
        }

        private void RaiseScrollChangedFromInner()
        {
            if (_suppressScrollEvents || _innerScroll == null) return;
            var value = _innerScroll.scrollOffset.y;
            var contentHeight = _innerScroll.contentContainer.layout.height;
            var viewportHeight = _innerScroll.contentViewport.layout.height;
            var max = contentHeight - viewportHeight;
            if (max < 0) max = 0;
            var isAtBottom = max <= 0 || value >= max - AutoScrollBottomThreshold;
            var snapshot = new ScrollDeltaSnapshot(value, max, isAtBottom, isAtBottom, false);
            ScrollChanged?.Invoke(snapshot);
        }

        // === Render ===

        public void Render(ChatTimelineViewState state)
        {
            _autoScrollEnabled = state.AutoScrollEnabled;

            if (_welcomeScreen != null)
            {
                _welcomeScreen.style.display = state.ShowEmptyState ? DisplayStyle.Flex : DisplayStyle.None;

                // Re-roll the inspiring subtitle each time the welcome screen transitions into view.
                if (state.ShowEmptyState && !_welcomeWasShown)
                {
                    var subtitle = _welcomeScreen.Q<Label>("welcome-subtitle");
                    if (subtitle != null)
                    {
                        subtitle.text = WelcomeMessages.Random();
                    }
                }
            }

            if (_messageListView != null)
            {
                _messageListView.style.display = state.ShowEmptyState ? DisplayStyle.None : DisplayStyle.Flex;
            }

            _welcomeWasShown = state.ShowEmptyState;
            SetScrollToBottomButtonVisible(state.ShowScrollToBottomButton);
        }

        // === History status ===

        private VisualElement _historyStatusElement;

        public void RenderHistoryStatus(ConversationLoadStatus<ConversationHistoryLoadState> status, Action onRetry)
        {
            HideHistoryStatus();
            if (status == null) return;

            var container = new VisualElement { name = "history-status-banner" };
            container.AddToClassList("sk-loading");

            var isSpinning = status.State is ConversationHistoryLoadState.Initializing
                or ConversationHistoryLoadState.Loading;
            var spinner = new Label(isSpinning ? "..." : string.Empty);
            spinner.AddToClassList("sk-loading-spinner");
            container.Add(spinner);

            var message = status.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = status.State switch
                {
                    ConversationHistoryLoadState.Initializing => "Initializing agent...",
                    ConversationHistoryLoadState.Loading => "Loading conversation...",
                    ConversationHistoryLoadState.Error => "Failed to load conversation history.",
                    ConversationHistoryLoadState.Empty => "Conversation is empty.",
                    _ => string.Empty
                };
            }

            var text = new Label(message);
            text.AddToClassList("sk-loading-text");
            container.Add(text);

            if (status.CanRetry && onRetry != null)
            {
                var retryButton = new Button(onRetry) { text = "Retry" };
                container.Add(retryButton);
            }

            _historyStatusElement = container;
            if (_historyStatusHost != null)
            {
                _historyStatusHost.Add(_historyStatusElement);
            }
        }

        public void HideHistoryStatus()
        {
            _historyStatusElement?.RemoveFromHierarchy();
            _historyStatusElement = null;
        }

        // === Scroll ===

        public void RequestScrollToBottom(int delayMs = 50)
        {
            if (_messageListView == null) return;
            _messageListView.schedule.Execute(() =>
            {
                _messageListView.ScrollToItem(-1);
                if (_innerScroll != null)
                {
                    try
                    {
                        _suppressScrollEvents = true;
                        _innerScroll.scrollOffset = new Vector2(0, _innerScroll.contentContainer.layout.height);
                    }
                    finally
                    {
                        _suppressScrollEvents = false;
                    }
                }
            }).ExecuteLater(delayMs);
        }

        // === Typing indicator ===

        public void SetTypingIndicatorVisible(bool visible)
        {
            if (_typingActive == visible) return;
            _typingActive = visible;
            if (_messageListView == null) return;
            RebuildViewItems();
            _messageListView.RefreshItems();
            if (visible && _autoScrollEnabled) RequestScrollToBottom(0);
        }

        // Builds the trailing "assistant is working" row rendered inside the ListView.
        // The dot animation is scheduled on the element itself, so it ticks only while the
        // row is attached to the panel (visible) and stops automatically when recycled.
        private VisualElement BuildTypingRow()
        {
            var row = new VisualElement { name = "turn-activity-indicator" };
            row.AddToClassList("sk-typing-indicator");
            var dots = new List<VisualElement>(3);
            for (int i = 0; i < 3; i++)
            {
                var dot = new VisualElement();
                dot.AddToClassList("sk-typing-dot");
                row.Add(dot);
                dots.Add(dot);
            }

            int currentDot = 0;
            const int intervalMs = 300;
            row.schedule.Execute(() =>
            {
                for (int i = 0; i < dots.Count; i++) dots[i].RemoveFromClassList("sk-typing-dot--active");
                dots[currentDot].AddToClassList("sk-typing-dot--active");
                currentDot = (currentDot + 1) % dots.Count;
            }).Every(intervalMs);

            return row;
        }

        // === Helpers ===

        internal void SetScrollToBottomButtonVisible(bool show)
        {
            if (_scrollToBottomContainer != null)
            {
                _scrollToBottomContainer.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // === ListView surface ===

        // Rebuilds the bound list from the live domain messages, appending the typing
        // sentinel as the trailing row while a turn is active.
        private void RebuildViewItems()
        {
            _viewItems.Clear();
            if (_sourceMessages != null) _viewItems.AddRange(_sourceMessages);
            if (_typingActive) _viewItems.Add(_typingSentinel);
        }

        public void SetItems(IList<Message> messages, Conversation conversation)
        {
            if (_messageListView == null) return;
            _sourceMessages = messages ?? System.Array.Empty<Message>();
            _conversation = conversation;
            RebuildViewItems();
            if (!ReferenceEquals(_messageListView.itemsSource, _viewItems))
            {
                _messageListView.itemsSource = _viewItems;
            }
            _messageListView.RefreshItems();
        }

        public void RefreshAll()
        {
            if (_messageListView == null) return;
            RebuildViewItems();
            _messageListView.RefreshItems();
            if (_autoScrollEnabled) RequestScrollToBottom(0);
        }

        public bool RefreshMessageById(string messageId)
        {
            if (_messageListView == null || string.IsNullOrEmpty(messageId)) return false;
            for (int i = _viewItems.Count - 1; i >= 0; i--)
            {
                if (_viewItems[i]?.Id == messageId)
                {
                    _messageListView.RefreshItem(i);
                    return true;
                }
            }
            return false;
        }

        public bool RefreshToolById(string toolUseId)
        {
            if (_messageListView == null || string.IsNullOrEmpty(toolUseId)) return false;
            for (int i = _viewItems.Count - 1; i >= 0; i--)
            {
                var msg = _viewItems[i];
                if (msg?.ToolUses == null) continue;
                foreach (var t in msg.ToolUses)
                {
                    if (t?.Id == toolUseId)
                    {
                        _messageListView.RefreshItem(i);
                        return true;
                    }
                }
            }
            return false;
        }

        public bool RefreshStreamingMessage(string messageId)
        {
            if (_messageListView == null || string.IsNullOrEmpty(messageId)) return false;
            for (int i = _viewItems.Count - 1; i >= 0; i--)
            {
                if (_viewItems[i]?.Id == messageId)
                {
                    _messageListView.RefreshItem(i);
                    return true;
                }
            }
            return false;
        }

        public void AddOverlayBanner(VisualElement banner)
        {
            if (banner == null) return;
            _permissionBannerHost?.Add(banner);
        }

        public VisualElement FindOverlayBanner(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return _permissionBannerHost?.Q<VisualElement>(name);
        }
    }
}
