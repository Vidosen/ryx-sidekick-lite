// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Domain.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.UI.Elements
{
    /// <summary>
    /// Visual element for rendering thinking blocks as inline chat elements.
    /// Similar to ToolCallElement but for thinking content.
    /// </summary>
    internal sealed class ThinkingElement : VisualElement
    {
        private const string ChevronDownPath = SidekickUiConstants.ChevronDownIconPath;
        private const string ChevronUpPath = SidekickUiConstants.ChevronUpIconPath;

        private static Texture2D _chevronDownIcon;
        private static Texture2D _chevronUpIcon;

        // Global thinking expanded state - shared across all ThinkingElement instances
        private static bool _globalThinkingExpanded = false;
        private static event Action<bool> OnGlobalThinkingToggled;

        // UI elements
        private readonly VisualElement _headerRow;
        private readonly VisualElement _bullet;
        private readonly Label _thinkingLabel;
        private readonly Label _durationLabel;
        private readonly VisualElement _chevron;
        private readonly VisualElement _contentContainer;
        private readonly Label _contentText;

        private Message _message;

        /// <summary>
        /// Gets or sets the global expanded state for all thinking elements.
        /// This is shared with MessageBubbleElement for backward compatibility.
        /// </summary>
        internal static bool GlobalExpanded
        {
            get => _globalThinkingExpanded;
            set
            {
                if (_globalThinkingExpanded != value)
                {
                    _globalThinkingExpanded = value;
                    OnGlobalThinkingToggled?.Invoke(value);
                }
            }
        }

        /// <summary>
        /// Event fired when global thinking state changes.
        /// MessageBubbleElement can subscribe to stay in sync.
        /// </summary>
        internal static event Action<bool> OnGlobalStateChanged
        {
            add => OnGlobalThinkingToggled += value;
            remove => OnGlobalThinkingToggled -= value;
        }

        public ThinkingElement()
        {
            // Load icons
            _chevronDownIcon ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ChevronDownPath);
            _chevronUpIcon ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ChevronUpPath);

            // Root styling
            AddToClassList("sk-thinking-element");
            style.flexShrink = 0;
            style.flexGrow = 0;

            // Main container
            var container = new VisualElement();
            container.AddToClassList("sk-thinking-container");
            Add(container);

            // Header row (clickable)
            _headerRow = new VisualElement();
            _headerRow.AddToClassList("sk-thinking-header-row");
            _headerRow.RegisterCallback<ClickEvent>(OnHeaderClicked);
            container.Add(_headerRow);

            // Bullet point
            _bullet = new VisualElement();
            _bullet.AddToClassList("sk-thinking-bullet");
            _headerRow.Add(_bullet);

            // "Thinking" label
            _thinkingLabel = new Label("Thinking");
            _thinkingLabel.AddToClassList("sk-thinking-label");
            _headerRow.Add(_thinkingLabel);

            // Duration label (shown after completion)
            _durationLabel = new Label();
            _durationLabel.AddToClassList("sk-thinking-duration");
            _durationLabel.style.display = DisplayStyle.None;
            _headerRow.Add(_durationLabel);

            // Chevron
            _chevron = new VisualElement();
            _chevron.AddToClassList("sk-thinking-chevron");
            if (_chevronDownIcon != null)
            {
                _chevron.style.backgroundImage = new StyleBackground(_chevronDownIcon);
            }
            _headerRow.Add(_chevron);

            // Content container (collapsible)
            _contentContainer = new VisualElement();
            _contentContainer.AddToClassList("sk-thinking-content");
            _contentContainer.style.display = _globalThinkingExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            container.Add(_contentContainer);

            // Content text
            _contentText = new Label();
            _contentText.AddToClassList("sk-thinking-text");
            _contentText.style.whiteSpace = WhiteSpace.Normal;
            _contentContainer.Add(_contentText);

            // Subscribe to global toggle
            OnGlobalThinkingToggled += HandleGlobalToggle;
            RegisterCallback<DetachFromPanelEvent>(_ => OnGlobalThinkingToggled -= HandleGlobalToggle);

            // Initial state
            UpdateExpandedState();
        }

        /// <summary>
        /// Sets the thinking message to display.
        /// </summary>
        public void SetThinking(Message thinkingMessage)
        {
            if (thinkingMessage == null)
                return;

            _message = thinkingMessage;
            UpdateUI();
        }

        /// <summary>
        /// Updates the UI to reflect current message state.
        /// Call this during streaming updates.
        /// </summary>
        public void Refresh()
        {
            UpdateUI();
        }

        private void UpdateUI()
        {
            if (_message == null) return;

            // Get content from ThinkingContent or Content
            var content = !string.IsNullOrEmpty(_message.ThinkingContent)
                ? _message.ThinkingContent
                : _message.Content ?? "";
            _contentText.text = content;

            // Duration label
            if (_message.ThinkingDurationSeconds.HasValue && !_message.IsStreaming)
            {
                var duration = _message.ThinkingDurationSeconds.Value;
                _durationLabel.text = $"({duration:F1}s)";
                _durationLabel.style.display = DisplayStyle.Flex;
            }
            else if (_message.IsStreaming)
            {
                _durationLabel.text = "...";
                _durationLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                _durationLabel.style.display = DisplayStyle.None;
            }

            // Update streaming indicator on bullet
            EnableInClassList("sk-thinking-streaming", _message.IsStreaming);

            // Update expanded state
            UpdateExpandedState();
        }

        private void UpdateExpandedState()
        {
            // Always use global state - no special streaming behavior
            var isExpanded = _globalThinkingExpanded;

            // Update chevron
            var icon = isExpanded ? _chevronUpIcon : _chevronDownIcon;
            if (icon != null)
            {
                _chevron.style.backgroundImage = new StyleBackground(icon);
            }

            // Update content visibility
            _contentContainer.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;

            // Update container classes
            EnableInClassList("sk-thinking-expanded", isExpanded);
            EnableInClassList("sk-thinking-collapsed", !isExpanded);
        }

        private void OnHeaderClicked(ClickEvent evt)
        {
            // Toggle global state (affects all thinking elements)
            GlobalExpanded = !GlobalExpanded;
            evt.StopPropagation();
        }

        private void HandleGlobalToggle(bool expanded)
        {
            UpdateExpandedState();
        }
    }
}
