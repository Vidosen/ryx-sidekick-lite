// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    internal enum ComposerSendButtonMode
    {
        Send,
        Stop
    }

    internal readonly struct ComposerAttachmentPreviewItem
    {
        public ComposerAttachmentPreviewItem(string id, Texture2D texture)
        {
            Id = id;
            Texture = texture;
        }

        public string Id { get; }

        public Texture2D Texture { get; }
    }

    internal readonly struct ComposerContextAttachmentItem
    {
        public ComposerContextAttachmentItem(IContextAttachment attachment)
        {
            Attachment = attachment;
        }

        public IContextAttachment Attachment { get; }
    }

    internal interface IComposerView
    {
        event Action<string> PromptChanged;

        event Action SendRequested;

        event Action NewChatRequested;

        event Action AddContextRequested;

        event Action<string> AttachmentPreviewOpened;

        event Action<string> AttachmentPreviewRemoved;

        event Action<string> ContextAttachmentOpened;

        event Action<string> ContextAttachmentRemoved;

        string PromptText { get; set; }

        bool IsPromptEnabled { get; set; }

        bool IsSendButtonEnabled { get; set; }

        void AdjustForContent();

        void BlurPrompt();

        void FocusPrompt();

        void SetSendButtonMode(ComposerSendButtonMode mode);

        void RenderAttachmentPreviews(IReadOnlyList<ComposerAttachmentPreviewItem> attachments);

        void RenderContextAttachments(IReadOnlyList<ComposerContextAttachmentItem> attachments);
    }

    internal sealed class ComposerView : IComposerView
    {
        private readonly TextField _promptField;
        private readonly Unity.AppUI.UI.Button _sendButton;
        private readonly Button _newChatButton;
        private readonly Unity.AppUI.UI.Button _addContextButton;
        private readonly VisualElement _attachmentsPreview;
        private readonly VisualElement _contextChipsArea;

        public ComposerView(
            TextField promptField,
            Unity.AppUI.UI.Button sendButton,
            Button newChatButton,
            Unity.AppUI.UI.Button addContextButton,
            VisualElement attachmentsPreview,
            VisualElement contextChipsArea)
        {
            _promptField = promptField;
            _sendButton = sendButton;
            _newChatButton = newChatButton;
            _addContextButton = addContextButton;
            _attachmentsPreview = attachmentsPreview;
            _contextChipsArea = contextChipsArea;

            _promptField?.RegisterValueChangedCallback(evt => PromptChanged?.Invoke(evt.newValue));
            if (_sendButton != null)
            {
                _sendButton.tooltip = "Send";
                _sendButton.RegisterCallback<ClickEvent>(_ => SendRequested?.Invoke());
            }
            _newChatButton?.RegisterCallback<ClickEvent>(_ => NewChatRequested?.Invoke());
            if (_addContextButton != null)
            {
                _addContextButton.tooltip = "Add context";
                _addContextButton.RegisterCallback<ClickEvent>(_ => AddContextRequested?.Invoke());
            }
        }

        public event Action<string> PromptChanged;

        public event Action SendRequested;

        public event Action NewChatRequested;

        public event Action AddContextRequested;

        public event Action<string> AttachmentPreviewOpened;

        public event Action<string> AttachmentPreviewRemoved;

        public event Action<string> ContextAttachmentOpened;

        public event Action<string> ContextAttachmentRemoved;

        public string PromptText
        {
            get => _promptField?.value ?? string.Empty;
            set
            {
                if (_promptField == null)
                {
                    return;
                }

                _promptField.value = value ?? string.Empty;
            }
        }

        public bool IsPromptEnabled
        {
            get => _promptField?.enabledSelf ?? false;
            set => _promptField?.SetEnabled(value);
        }

        public bool IsSendButtonEnabled
        {
            get => _sendButton?.enabledSelf ?? false;
            set => _sendButton?.SetEnabled(value);
        }

        public void AdjustForContent()
        {
            if (_promptField == null) return;

            // Reset internal TextField position to prevent text "flying up"
            // TextInput is NOT a ScrollView - it's a custom Unity class that manages scroll internally
            // Reset transform on the multiline-container and TextElement to keep text at top
            var multilineContainer = _promptField.Q(className: "unity-base-text-field__multiline-container");
            if (multilineContainer != null)
            {
                multilineContainer.style.translate = new Translate(0, 0);
                multilineContainer.style.top = 0;
            }

            var textElement = _promptField.Q<TextElement>();
            if (textElement != null)
            {
                textElement.style.translate = new Translate(0, 0);
                textElement.style.top = 0;
            }

            var text = _promptField.value ?? "";
            var lineHeight = 20f;
            var padding = 20f;
            var minHeight = 40f;
            // No maxHeight - outer ScrollView handles overflow

            // Empty text - use minimum height
            if (string.IsNullOrEmpty(text))
            {
                _promptField.style.height = minHeight;
                return;
            }

            // Calculate height based on text content
            var fieldWidth = _promptField.resolvedStyle.width;
            if (float.IsNaN(fieldWidth) || fieldWidth <= 0)
                fieldWidth = 400f;

            var textWidth = fieldWidth - 28f;
            var avgCharWidth = 8f;
            var charsPerLine = Mathf.Max(1, Mathf.FloorToInt(textWidth / avgCharWidth));

            var lines = text.Split('\n');
            var totalLines = 0;
            foreach (var line in lines)
            {
                totalLines += string.IsNullOrEmpty(line)
                    ? 1
                    : Mathf.Max(1, Mathf.CeilToInt((float)line.Length / charsPerLine));
            }

            // No clamping - let TextField grow, ScrollView handles overflow
            var requiredHeight = Mathf.Max(totalLines * lineHeight + padding, minHeight);
            _promptField.style.height = requiredHeight;
        }

        public void BlurPrompt()
        {
            _promptField?.Blur();
        }

        public void FocusPrompt()
        {
            _promptField?.Focus();
        }

        public void SetSendButtonMode(ComposerSendButtonMode mode)
        {
            if (_sendButton == null)
            {
                return;
            }

            switch (mode)
            {
                case ComposerSendButtonMode.Stop:
                    _sendButton.AddToClassList("stop-mode");
                    _sendButton.title = string.Empty;
                    _sendButton.tooltip = "Stop";
                    if (_sendButton.Q<VisualElement>("stop-icon") == null)
                    {
                        var icon = new VisualElement
                        {
                            name = "stop-icon",
                            pickingMode = PickingMode.Ignore
                        };
                        icon.AddToClassList("sk-send-btn__stop-icon");
                        _sendButton.Add(icon);
                    }
                    break;

                default:
                    _sendButton.RemoveFromClassList("stop-mode");
                    _sendButton.title = "↑";
                    _sendButton.tooltip = "Send";
                    _sendButton.Q<VisualElement>("stop-icon")?.RemoveFromHierarchy();
                    break;
            }
        }

        public void RenderAttachmentPreviews(IReadOnlyList<ComposerAttachmentPreviewItem> attachments)
        {
            if (_attachmentsPreview == null)
            {
                return;
            }

            _attachmentsPreview.Clear();

            if (attachments == null || attachments.Count == 0)
            {
                _attachmentsPreview.style.display = DisplayStyle.None;
                return;
            }

            _attachmentsPreview.style.display = DisplayStyle.Flex;

            foreach (var attachment in attachments)
            {
                _attachmentsPreview.Add(CreateAttachmentPreviewElement(attachment));
            }
        }

        public void RenderContextAttachments(IReadOnlyList<ComposerContextAttachmentItem> attachments)
        {
            if (_contextChipsArea == null)
            {
                return;
            }

            _contextChipsArea.Clear();

            if (attachments == null || attachments.Count == 0)
            {
                _contextChipsArea.style.display = DisplayStyle.None;
                _contextChipsArea.RemoveFromClassList("has-items");
                return;
            }

            _contextChipsArea.style.display = DisplayStyle.Flex;
            _contextChipsArea.AddToClassList("has-items");

            foreach (var attachment in attachments)
            {
                if (attachment.Attachment == null)
                {
                    continue;
                }

                var chip = new ContextChipElement();
                chip.SetAttachment(attachment.Attachment, showRemoveButton: true);
                chip.OnRemove += _ => ContextAttachmentRemoved?.Invoke(attachment.Attachment.Id);
                chip.OnClick += _ => ContextAttachmentOpened?.Invoke(attachment.Attachment.Id);
                _contextChipsArea.Add(chip);
            }
        }

        private static void ApplyContainSize(VisualElement wrapper, Texture2D texture, float maxWidth, float maxHeight)
        {
            if (wrapper == null)
            {
                return;
            }

            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                wrapper.style.width = maxWidth;
                wrapper.style.height = maxHeight;
                return;
            }

            float scale = Mathf.Min(maxWidth / texture.width, maxHeight / texture.height);
            wrapper.style.width = texture.width * scale;
            wrapper.style.height = texture.height * scale;
        }

        private VisualElement CreateAttachmentPreviewElement(ComposerAttachmentPreviewItem attachment)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("sk-attachment-preview");
            wrapper.userData = attachment.Id;

            ApplyContainSize(wrapper, attachment.Texture, 96f, 96f);

            var image = new Image();
            image.AddToClassList("sk-attachment-preview__image");
            image.scaleMode = ScaleMode.ScaleToFit;
            image.image = attachment.Texture;
            wrapper.Add(image);

            var removeButton = new Button
            {
                text = "×"
            };
            removeButton.AddToClassList("sk-attachment-preview__remove");
            removeButton.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                AttachmentPreviewRemoved?.Invoke(attachment.Id);
            });
            wrapper.Add(removeButton);

            wrapper.RegisterCallback<ClickEvent>(_ => AttachmentPreviewOpened?.Invoke(attachment.Id));

            return wrapper;
        }
    }
}
