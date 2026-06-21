// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Domain.Parsing;
using Ryx.Sidekick.Editor.Infrastructure.Assets;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.UI.Elements
{
    internal sealed class MessageBubbleElement : VisualElement
    {
        private const string TemplatePath = SidekickUiConstants.MessageBubbleTemplatePath;
        private static VisualTreeAsset _template;

        private const string CopyIconPath = SidekickUiConstants.CopyIconPath;
        private const string ChevronDownIconPath = SidekickUiConstants.ChevronDownIconPath;
        private const string ChevronUpIconPath = SidekickUiConstants.ChevronUpIconPath;

        private readonly VisualElement _messageRoot;
        private readonly Label _roleLabel;
        private readonly Label _timeLabel;
        private readonly Button _copyButton;
        private readonly VisualElement _contentRoot;
        private readonly Label _textLabel;

        private static Texture2D _copyIcon;
        private static Texture2D _chevronDownIcon;
        private static Texture2D _chevronUpIcon;

        // Thinking UI elements (for backward compatibility with loaded history that has embedded thinking)
        private VisualElement _thinkingContainer;
        private Label _thinkingLabel;
        private VisualElement _thinkingChevron;
        private VisualElement _thinkingContent;
        private Label _thinkingText;
        private Message _currentMessage;
        
        // Role header container reference
        private VisualElement _headerRow;

        public MessageBubbleElement()
        {
            // Wrapper element must not interfere with child layout
            style.flexShrink = 0;
            style.flexGrow = 0;

            _template ??= AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TemplatePath);

            if (_template)
            {
                _template.CloneTree(this);
            }
            else
            {
                // Fallback so the UI doesn't hard-fail if the UXML is missing.
                Add(new Label($"Missing template: {TemplatePath}"));
            }

            _messageRoot = this.Q<VisualElement>("message") ?? this;
            _headerRow = this.Q<VisualElement>(className: "sk-message-header");
            _roleLabel = this.Q<Label>("role");
            _timeLabel = this.Q<Label>("time");
            _copyButton = this.Q<Button>("copy-btn");
            _contentRoot = this.Q<VisualElement>("content");
            _textLabel = this.Q<Label>("text");

            // Setup copy button
            SetupCopyButton();

            // Subscribe to global thinking toggle (shared with ThinkingElement)
            ThinkingElement.OnGlobalStateChanged += HandleGlobalThinkingToggled;
            RegisterCallback<DetachFromPanelEvent>(_ => ThinkingElement.OnGlobalStateChanged -= HandleGlobalThinkingToggled);
        }

        private void HandleGlobalThinkingToggled(bool expanded)
        {
            if (_thinkingContainer == null || _currentMessage == null) return;
            if (string.IsNullOrEmpty(_currentMessage.ThinkingContent)) return;

            UpdateThinkingUI(_currentMessage);
        }
        
        private void SetupCopyButton()
        {
            if (_copyButton == null) return;
            
            // Load icon once
            _copyIcon ??= AssetDatabase.LoadAssetAtPath<Texture2D>(CopyIconPath);
            
            if (_copyIcon != null)
            {
                _copyButton.style.backgroundImage = new StyleBackground(_copyIcon);
            }
            
            _copyButton.clicked += OnCopyClicked;
        }
        
        private void OnCopyClicked()
        {
            if (_currentMessage == null || string.IsNullOrEmpty(_currentMessage.Content)) return;
            
            GUIUtility.systemCopyBuffer = _currentMessage.Content;
        }

        public void SetMessage(
            Message message,
            Func<string, VisualElement> renderMarkdownContent,
            AttachmentController attachmentController)
        {
            if (message == null) return;
            _currentMessage = message;

            if (message.Role == MessageRole.User && !string.IsNullOrEmpty(message.Content))
            {
                if (message.Content.Contains("<context_", StringComparison.Ordinal) &&
                    (message.ContextAttachments == null || message.ContextAttachments.Count == 0))
                {
                    if (ContextAttachmentParser.TryExtractContext(message.Content, out var cleaned, out var parsed))
                    {
                        message.Content = cleaned;
                        if (parsed.Count > 0)
                        {
                            message.ContextAttachments = parsed;
                        }
                    }
                }

                if (message.Content.Contains("<command-", StringComparison.Ordinal) &&
                    CommandTagParser.TryFormatCommandText(message.Content, out var formatted))
                {
                    message.Content = formatted;
                }
            }

            // Root styling
            _messageRoot?.RemoveFromClassList("user");
            _messageRoot?.RemoveFromClassList("assistant");
            _messageRoot?.AddToClassList(message.Role == MessageRole.User ? "user" : "assistant");

            if (_roleLabel != null)
            {
                _roleLabel.text = message.Role.ToString().ToUpperInvariant();
            }

            if (_timeLabel != null)
            {
                _timeLabel.text = message.Timestamp.ToString("HH:mm");
            }
            
            // Hide copy button if no content
            if (_copyButton != null)
            {
                var hasContent = !string.IsNullOrEmpty(message.Content);
                _copyButton.style.display = hasContent ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_contentRoot == null) return;

            // Remove any existing markdown content we injected previously
            var existingInjected = _contentRoot.Q<VisualElement>(name: "sk-message-rendered");
            if (existingInjected != null)
            {
                _contentRoot.Remove(existingInjected);
            }

            // Keep template label for non-markdown use; clear it first
            if (_textLabel != null)
            {
                _textLabel.text = "";
                _textLabel.style.display = DisplayStyle.None;
            }

            // Thinking block (before main content)
            UpdateThinkingUI(message);

            // Content
            if (message.Role == MessageRole.Assistant && !string.IsNullOrEmpty(message.Content))
            {
                var md = renderMarkdownContent?.Invoke(message.Content);
                if (md != null)
                {
                    md.name = "sk-message-rendered";
                    md.AddToClassList("sk-message-text");
                    _contentRoot.Insert(_thinkingContainer != null ? 1 : 0, md);
                }
            }
            else if (!string.IsNullOrEmpty(message.Content))
            {
                // Use template label for simplicity (selection is handled by SelectableLabel elsewhere in the window).
                if (_textLabel != null)
                {
                    _textLabel.style.display = DisplayStyle.Flex;
                    _textLabel.text = message.Content;
                    _textLabel.enableRichText = true;
                }
            }

            // Attachments
            attachmentController?.AddAttachmentsToContent(_contentRoot, message);
        }

        /// <summary>
        /// Shows or hides the role header (ASSISTANT/USER + timestamp).
        /// Hide when consecutive messages are from the same role.
        /// </summary>
        public void SetShowRoleHeader(bool show)
        {
            if (_headerRow != null)
            {
                _headerRow.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>
        /// Updates the thinking UI for streaming. Call this during streaming to show thinking progress.
        /// </summary>
        public void RefreshThinking(Message message)
        {
            if (message == null) return;
            _currentMessage = message;
            UpdateThinkingUI(message);
        }

        private void UpdateThinkingUI(Message message)
        {
            // Skip rendering for IsThinkingBlock messages - they are rendered as separate ThinkingElement
            if (message.IsThinkingBlock)
            {
                if (_thinkingContainer != null)
                {
                    _thinkingContainer.RemoveFromHierarchy();
                    _thinkingContainer = null;
                }
                return;
            }

            var hasThinking = !string.IsNullOrEmpty(message.ThinkingContent);

            if (!hasThinking)
            {
                // Remove thinking container if it exists and there's no thinking
                if (_thinkingContainer != null)
                {
                    _thinkingContainer.RemoveFromHierarchy();
                    _thinkingContainer = null;
                }
                return;
            }

            // Create thinking container if needed
            if (_thinkingContainer == null)
            {
                CreateThinkingUI();
            }

            // Always use global expanded state (shared with ThinkingElement)
            var isExpanded = ThinkingElement.GlobalExpanded;

            // Update chevron icon
            if (_thinkingChevron != null)
            {
                // Load icons on first use
                _chevronDownIcon ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ChevronDownIconPath);
                _chevronUpIcon ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ChevronUpIconPath);

                var icon = isExpanded ? _chevronUpIcon : _chevronDownIcon;
                if (icon != null)
                {
                    _thinkingChevron.style.backgroundImage = new StyleBackground(icon);
                }
            }

            // Update content visibility based on expanded state
            if (_thinkingContent != null)
            {
                _thinkingContent.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Update text content
            if (_thinkingText != null)
            {
                _thinkingText.text = message.ThinkingContent;
            }

            // Update container class for styling
            _thinkingContainer?.EnableInClassList("sk-thinking-expanded", isExpanded);
            _thinkingContainer?.EnableInClassList("sk-thinking-collapsed", !isExpanded);
        }

        private void CreateThinkingUI()
        {
            if (_contentRoot == null) return;

            _thinkingContainer = new VisualElement();
            _thinkingContainer.name = "thinking-container";
            _thinkingContainer.AddToClassList("sk-thinking-container");

            // Header row (clickable to toggle) - VS Code style: • Thinking ˅
            var headerRow = new VisualElement();
            headerRow.AddToClassList("sk-thinking-header-row");
            headerRow.RegisterCallback<ClickEvent>(OnThinkingHeaderClicked);

            // Bullet point
            var bullet = new VisualElement();
            bullet.AddToClassList("sk-thinking-bullet");
            headerRow.Add(bullet);

            // "Thinking" label (italic)
            _thinkingLabel = new Label("Thinking");
            _thinkingLabel.AddToClassList("sk-thinking-label");
            headerRow.Add(_thinkingLabel);

            // Chevron icon (image-based)
            _thinkingChevron = new VisualElement();
            _thinkingChevron.AddToClassList("sk-thinking-chevron");
            // Load icons on first use
            _chevronDownIcon ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ChevronDownIconPath);
            if (_chevronDownIcon != null)
            {
                _thinkingChevron.style.backgroundImage = new StyleBackground(_chevronDownIcon);
            }
            headerRow.Add(_thinkingChevron);

            _thinkingContainer.Add(headerRow);

            // Thinking content (shown when expanded)
            _thinkingContent = new VisualElement();
            _thinkingContent.AddToClassList("sk-thinking-content");
            _thinkingContent.style.display = DisplayStyle.None; // Start collapsed

            _thinkingText = new Label();
            _thinkingText.AddToClassList("sk-thinking-text");
            _thinkingText.style.whiteSpace = WhiteSpace.Normal;
            _thinkingContent.Add(_thinkingText);

            _thinkingContainer.Add(_thinkingContent);

            // Insert at the beginning of content
            _contentRoot.Insert(0, _thinkingContainer);
        }

        private void OnThinkingHeaderClicked(ClickEvent evt)
        {
            if (_currentMessage == null) return;

            // Toggle global expanded state (shared with ThinkingElement)
            ThinkingElement.GlobalExpanded = !ThinkingElement.GlobalExpanded;

            evt.StopPropagation();
        }
    }
}
