// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    internal interface IAskUserQuestionView
    {
        event Action ClosedRequested;

        event Action SubmitRequested;

        event Action<int> QuestionTabChanged;

        event Action<int, int, bool> OptionToggled;

        event Action<string> OtherTextChanged;

        void Render(AskUserQuestionViewState state);
    }

    internal readonly struct AskUserQuestionViewState
    {
        public AskUserQuestionViewState(
            string headerText,
            string questionText,
            string countBadgeText,
            string otherText,
            string otherPlaceholder,
            bool isVisible,
            bool isSubmitEnabled,
            bool showOtherInput,
            bool isOtherInputSecret,
            bool isMultiSelect,
            int activeQuestionIndex,
            IReadOnlyList<AskUserQuestionTabViewState> tabs,
            IReadOnlyList<AskUserQuestionOptionViewState> options)
        {
            HeaderText = headerText;
            QuestionText = questionText;
            CountBadgeText = countBadgeText;
            OtherText = otherText;
            OtherPlaceholder = otherPlaceholder;
            IsVisible = isVisible;
            IsSubmitEnabled = isSubmitEnabled;
            ShowOtherInput = showOtherInput;
            IsOtherInputSecret = isOtherInputSecret;
            IsMultiSelect = isMultiSelect;
            ActiveQuestionIndex = activeQuestionIndex;
            Tabs = tabs;
            Options = options;
        }

        public string HeaderText { get; }

        public string QuestionText { get; }

        public string CountBadgeText { get; }

        public string OtherText { get; }

        public string OtherPlaceholder { get; }

        public bool IsVisible { get; }

        public bool IsSubmitEnabled { get; }

        public bool ShowOtherInput { get; }

        public bool IsOtherInputSecret { get; }

        public bool IsMultiSelect { get; }

        public int ActiveQuestionIndex { get; }

        public IReadOnlyList<AskUserQuestionTabViewState> Tabs { get; }

        public IReadOnlyList<AskUserQuestionOptionViewState> Options { get; }

        /// <summary>
        /// Canonical "nothing to render" state — used by the VM to ask the view to
        /// hide without showing any content.
        /// </summary>
        public static AskUserQuestionViewState Hidden { get; } = new AskUserQuestionViewState(
            headerText: string.Empty,
            questionText: string.Empty,
            countBadgeText: string.Empty,
            otherText: string.Empty,
            otherPlaceholder: string.Empty,
            isVisible: false,
            isSubmitEnabled: false,
            showOtherInput: false,
            isOtherInputSecret: false,
            isMultiSelect: false,
            activeQuestionIndex: 0,
            tabs: Array.Empty<AskUserQuestionTabViewState>(),
            options: Array.Empty<AskUserQuestionOptionViewState>());
    }

    internal readonly struct AskUserQuestionTabViewState
    {
        public AskUserQuestionTabViewState(string label, bool isActive)
        {
            Label = label;
            IsActive = isActive;
        }

        public string Label { get; }

        public bool IsActive { get; }
    }

    internal readonly struct AskUserQuestionOptionViewState
    {
        public AskUserQuestionOptionViewState(string label, string description, bool isSelected, bool isOtherOption)
        {
            Label = label;
            Description = description;
            IsSelected = isSelected;
            IsOtherOption = isOtherOption;
        }

        public string Label { get; }

        public string Description { get; }

        public bool IsSelected { get; }

        public bool IsOtherOption { get; }
    }

    internal sealed class AskUserQuestionView : IAskUserQuestionView
    {
        private readonly VisualElement _container;
        private readonly VisualElement _overlay;
        private readonly Label _headerText;
        private readonly VisualElement _tabs;
        private readonly Label _questionText;
        private readonly VisualElement _options;
        private readonly VisualElement _otherContainer;
        private readonly TextField _otherInput;
        private readonly VisualElement _footer;
        private readonly Label _countBadge;
        private readonly Button _submitButton;

        public AskUserQuestionView(
            VisualElement container,
            VisualElement overlay,
            VisualElement backdrop,
            Label headerText,
            VisualElement tabs,
            Button closeButton,
            Label questionText,
            VisualElement options,
            VisualElement otherContainer,
            TextField otherInput,
            VisualElement footer,
            Label countBadge,
            Button submitButton,
            VisualElement messageList)
        {
            _container = container;
            _overlay = overlay;
            _headerText = headerText;
            _tabs = tabs;
            _questionText = questionText;
            _options = options;
            _otherContainer = otherContainer;
            _otherInput = otherInput;
            _footer = footer;
            _countBadge = countBadge;
            _submitButton = submitButton;

            closeButton?.RegisterCallback<ClickEvent>(_ => ClosedRequested?.Invoke());
            // Backdrop (ask) has display:none via CSS — no click handler needed.
            _submitButton?.RegisterCallback<ClickEvent>(_ => SubmitRequested?.Invoke());
            _otherInput?.RegisterValueChangedCallback(evt => OtherTextChanged?.Invoke(evt.newValue));
            _overlay?.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape)
                {
                    return;
                }

                ClosedRequested?.Invoke();
                evt.StopPropagation();
            });
        }

        public event Action ClosedRequested;

        public event Action SubmitRequested;

        public event Action<int> QuestionTabChanged;

        public event Action<int, int, bool> OptionToggled;

        public event Action<string> OtherTextChanged;

        /// <summary>
        /// Exposes the overlay element for tests that need to introspect the rendered DOM.
        /// </summary>
        internal VisualElement ContentForTests => _overlay;

        public void Render(AskUserQuestionViewState state)
        {
            SetVisible(state.IsVisible);

            if (_headerText != null)
            {
                _headerText.text = state.HeaderText ?? string.Empty;
            }

            if (_questionText != null)
            {
                _questionText.text = state.QuestionText ?? string.Empty;
            }

            if (_countBadge != null)
            {
                _countBadge.text = state.CountBadgeText ?? string.Empty;
            }

            if (_footer != null)
            {
                _footer.style.display = string.IsNullOrEmpty(state.CountBadgeText) && !state.IsSubmitEnabled
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }

            _submitButton?.SetEnabled(state.IsSubmitEnabled);

            if (_otherContainer != null)
            {
                _otherContainer.style.display = state.ShowOtherInput ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_otherInput != null)
            {
                _otherInput.isPasswordField = state.IsOtherInputSecret;
                _otherInput.textEdition.placeholder = string.IsNullOrWhiteSpace(state.OtherPlaceholder)
                    ? "Describe what to do instead"
                    : state.OtherPlaceholder;

                if (_otherInput.value != state.OtherText)
                {
                    _otherInput.SetValueWithoutNotify(state.OtherText ?? string.Empty);
                }
            }

            RenderTabs(state);
            RenderOptions(state);
        }

        public void SetVisible(bool show)
        {
            if (_container != null)
            {
                _container.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_overlay != null)
            {
                _overlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void RenderTabs(AskUserQuestionViewState state)
        {
            if (_tabs == null)
            {
                return;
            }

            _tabs.Clear();

            if (state.Tabs is { Count: > 1 })
            {
                _tabs.AddToClassList("multi-question");
            }
            else
            {
                _tabs.RemoveFromClassList("multi-question");
            }

            if (state.Tabs == null)
            {
                return;
            }

            for (int i = 0; i < state.Tabs.Count; i++)
            {
                var tabIndex = i;
                var tabState = state.Tabs[i];
                var tabButton = new Button(() => QuestionTabChanged?.Invoke(tabIndex))
                {
                    text = tabState.Label ?? $"Q{tabIndex + 1}",
                    name = $"ask-tab-{tabIndex}"
                };
                tabButton.AddToClassList("sk-ask-tab");
                if (tabState.IsActive)
                {
                    tabButton.AddToClassList("active");
                }

                _tabs.Add(tabButton);
            }
        }

        private void RenderOptions(AskUserQuestionViewState state)
        {
            if (_options == null)
            {
                return;
            }

            _options.Clear();

            if (state.Options == null)
            {
                return;
            }

            for (int i = 0; i < state.Options.Count; i++)
            {
                var optionIndex = i;
                var optionState = state.Options[i];
                var optionRow = new VisualElement
                {
                    name = $"ask-option-{state.ActiveQuestionIndex}-{optionIndex}"
                };
                optionRow.AddToClassList("sk-ask-option");
                if (optionState.IsSelected)
                {
                    optionRow.AddToClassList("selected");
                }

                if (!state.IsMultiSelect)
                {
                    var numberIndicator = new Label((i + 1).ToString());
                    numberIndicator.AddToClassList("sk-ask-option__number");
                    optionRow.Add(numberIndicator);
                }
                else
                {
                    var indicator = new VisualElement();
                    indicator.AddToClassList("sk-ask-option__indicator");
                    indicator.AddToClassList("checkbox");

                    var checkmark = new VisualElement();
                    checkmark.AddToClassList("sk-ask-option__check");
                    indicator.Add(checkmark);
                    optionRow.Add(indicator);
                }

                var textContainer = new VisualElement();
                textContainer.AddToClassList("sk-ask-option__text");

                var labelElement = new Label(optionState.Label ?? string.Empty);
                labelElement.AddToClassList("sk-ask-option__label");
                textContainer.Add(labelElement);

                if (!string.IsNullOrEmpty(optionState.Description))
                {
                    var descriptionElement = new Label(optionState.Description);
                    descriptionElement.AddToClassList("sk-ask-option__description");
                    textContainer.Add(descriptionElement);
                }

                optionRow.Add(textContainer);
                optionRow.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.target is TextField)
                    {
                        return;
                    }

                    OptionToggled?.Invoke(state.ActiveQuestionIndex, optionIndex, state.IsMultiSelect);
                });

                _options.Add(optionRow);
            }
        }
    }
}
