// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Domain.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    internal enum ConversationMenuEntryKind
    {
        Header,
        Status,
        Conversation
    }

    internal readonly struct ConversationMenuEntryViewState
    {
        public ConversationMenuEntryViewState(
            ConversationMenuEntryKind kind,
            string text,
            string detailText = null,
            bool isSelected = false,
            bool canRetry = false,
            bool showSpinner = false,
            Conversation conversation = null)
        {
            Kind = kind;
            Text = text;
            DetailText = detailText;
            IsSelected = isSelected;
            CanRetry = canRetry;
            ShowSpinner = showSpinner;
            Conversation = conversation;
        }

        public ConversationMenuEntryKind Kind { get; }

        public string Text { get; }

        public string DetailText { get; }

        public bool IsSelected { get; }

        public bool CanRetry { get; }

        public bool ShowSpinner { get; }

        public Conversation Conversation { get; }
    }

    internal readonly struct ConversationMenuViewState
    {
        public ConversationMenuViewState(
            bool isVisible,
            string searchText,
            bool isSearchEnabled,
            IReadOnlyList<ConversationMenuEntryViewState> entries)
        {
            IsVisible = isVisible;
            SearchText = searchText;
            IsSearchEnabled = isSearchEnabled;
            Entries = entries;
        }

        public bool IsVisible { get; }

        public string SearchText { get; }

        public bool IsSearchEnabled { get; }

        public IReadOnlyList<ConversationMenuEntryViewState> Entries { get; }
    }

    internal interface IConversationMenuView
    {
        event Action<string> SearchChanged;

        event Action RetryRequested;

        event Action<Conversation> ConversationSelected;

        event Action<Conversation> ConversationDeleteRequested;

        string SearchText { get; }

        void Render(ConversationMenuViewState state);

        void FocusSearch();

        bool IsClickInside(ClickEvent evt);
    }

    internal sealed class ConversationMenuView : IConversationMenuView
    {
        private readonly Button _dropdownButton;
        private readonly VisualElement _popup;
        private readonly TextField _search;
        private readonly ScrollView _list;

        public ConversationMenuView(
            Button dropdownButton,
            VisualElement popup,
            TextField search,
            ScrollView list)
        {
            _dropdownButton = dropdownButton;
            _popup = popup;
            _search = search;
            _list = list;

            _search?.RegisterValueChangedCallback(evt => SearchChanged?.Invoke(evt.newValue));
        }

        public event Action<string> SearchChanged;

        public event Action RetryRequested;

        public event Action<Conversation> ConversationSelected;

        public event Action<Conversation> ConversationDeleteRequested;

        public string SearchText => _search?.value ?? string.Empty;

        public void Render(ConversationMenuViewState state)
        {
            if (_popup != null)
            {
                _popup.style.display = state.IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_search != null)
            {
                if (_search.value != state.SearchText)
                {
                    _search.SetValueWithoutNotify(state.SearchText ?? string.Empty);
                }

                _search.SetEnabled(state.IsSearchEnabled);
            }

            if (_list == null)
            {
                return;
            }

            _list.Clear();

            if (state.Entries == null)
            {
                return;
            }

            foreach (var entry in state.Entries)
            {
                _list.Add(CreateEntry(entry));
            }
        }

        public void FocusSearch()
        {
            _search?.Focus();
        }

        public bool IsClickInside(ClickEvent evt)
        {
            if (_popup == null || _dropdownButton == null)
            {
                return false;
            }

            var target = evt.target as VisualElement;
            while (target != null)
            {
                if (target == _popup || target == _dropdownButton)
                {
                    return true;
                }

                target = target.parent;
            }

            return false;
        }

        private VisualElement CreateEntry(ConversationMenuEntryViewState entry)
        {
            return entry.Kind switch
            {
                ConversationMenuEntryKind.Header => CreateHeader(entry.Text),
                ConversationMenuEntryKind.Status => CreateStatus(entry),
                ConversationMenuEntryKind.Conversation => CreateConversation(entry),
                _ => new Label(entry.Text ?? string.Empty)
            };
        }

        private static VisualElement CreateHeader(string text)
        {
            var header = new Label(text ?? string.Empty);
            header.AddToClassList("sk-popup-date-group");
            return header;
        }

        private VisualElement CreateStatus(ConversationMenuEntryViewState entry)
        {
            var container = new VisualElement();
            container.AddToClassList("sk-loading");

            var spinner = new Label(entry.ShowSpinner ? "..." : string.Empty);
            spinner.AddToClassList("sk-loading-spinner");
            container.Add(spinner);

            var text = new Label(entry.Text ?? string.Empty);
            text.AddToClassList("sk-loading-text");
            container.Add(text);

            if (entry.CanRetry)
            {
                var retryButton = new Button(() => RetryRequested?.Invoke())
                {
                    text = "Retry"
                };
                container.Add(retryButton);
            }

            return container;
        }

        private VisualElement CreateConversation(ConversationMenuEntryViewState entry)
        {
            var item = new Button(() => ConversationSelected?.Invoke(entry.Conversation));
            item.AddToClassList("sk-popup-conversation-item");

            if (entry.IsSelected)
            {
                item.AddToClassList("selected");
            }

            var title = new Label(entry.Text ?? string.Empty);
            title.AddToClassList("sk-popup-conversation-title");
            item.Add(title);

            var time = new Label(entry.DetailText ?? string.Empty);
            time.AddToClassList("sk-popup-conversation-time");
            item.Add(time);

            item.RegisterCallback<ContextClickEvent>(_ =>
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Delete"), false, () => ConversationDeleteRequested?.Invoke(entry.Conversation));
                menu.ShowAsContext();
            });

            return item;
        }
    }
}
