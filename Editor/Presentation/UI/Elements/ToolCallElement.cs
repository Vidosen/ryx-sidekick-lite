// SPDX-License-Identifier: GPL-3.0-only
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Domain.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.UI.Elements
{
    internal sealed class ToolCallElement : VisualElement
    {
        private const string TemplatePath = SidekickUiConstants.ToolCallTemplatePath;
        private const string ChevronDownPath = SidekickUiConstants.ChevronDownIconPath;
        private const string ChevronUpPath = SidekickUiConstants.ChevronUpIconPath;

        private static VisualTreeAsset _template;
        private static Texture2D _chevronDownIcon;
        private static Texture2D _chevronUpIcon;

        private readonly VisualElement _statusDot;
        private readonly Label _name;
        private readonly VisualElement _headerContent;
        private readonly VisualElement _toolBody;
        private readonly VisualElement _ioContainer;

        private VisualElement _customContent;
        private readonly Label _autoAcceptedChip;

        // Collapsible IO elements (per instance)
        private bool _ioExpanded = false;
        private ToolUse _ioToolUse; // Back-reference so IO-expand state is persisted on the model (survives ListView recycling)
        private VisualElement _ioHeader;
        private VisualElement _ioChevron;
        private VisualElement _ioContent;
        private Label _inputLabel;
        private Label _outputLabel;

        public ToolCallElement()
        {
            // Wrapper element must not interfere with child layout
            style.flexShrink = 0;
            style.flexGrow = 0;
            style.minWidth = 0; // Allow shrinking below content size
            style.overflow = Overflow.Hidden;

            _template ??= AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TemplatePath);

            if (_template)
            {
                _template.CloneTree(this);
            }
            else
            {
                Add(new Label($"Missing template: {TemplatePath}"));
            }

            _statusDot = this.Q<VisualElement>("status-dot");
            _name = this.Q<Label>("name");
            _autoAcceptedChip = this.Q<Label>("auto-accepted-chip");
            _headerContent = this.Q<VisualElement>("header-content");
            _toolBody = this.Q<VisualElement>("tool-body");
            _ioContainer = this.Q<VisualElement>("io-container");

            // Load icons
            _chevronDownIcon ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ChevronDownPath);
            _chevronUpIcon ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ChevronUpPath);
        }

        public void SetAutoAccepted(bool autoAccepted)
        {
            if (_autoAcceptedChip != null)
                _autoAcceptedChip.style.display = autoAccepted ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetToolUse(
            ToolUse toolUse,
            Func<ToolKind, string> getToolIcon,
            Func<ToolUse, string> getToolDisplayName,
            Func<ToolUse, VisualElement> createCustomContent = null,
            Func<ToolUse, VisualElement> createHeaderContent = null)
        {
            if (toolUse == null) return;

            var toolName = string.IsNullOrEmpty(toolUse.Name) ? "tool" : toolUse.Name;

            // Update status dot
            if (_statusDot != null)
            {
                _statusDot.RemoveFromClassList("running");
                _statusDot.RemoveFromClassList("success");
                _statusDot.RemoveFromClassList("error");
                _statusDot.RemoveFromClassList("pending");
                _statusDot.AddToClassList(toolUse.Status.ToString().ToLowerInvariant());
            }

            if (_name != null)
            {
                _name.text = getToolDisplayName?.Invoke(toolUse) ?? toolName;
            }

            // Header content (inline, like AssetLink for Read)
            var headerContent = createHeaderContent?.Invoke(toolUse);
            if (headerContent != null && _headerContent != null)
            {
                _headerContent.Clear();
                _headerContent.Add(headerContent);
                _headerContent.style.display = DisplayStyle.Flex;

                // Hide body for header-only tools
                if (_toolBody != null)
                {
                    _toolBody.style.display = DisplayStyle.None;
                }
                return;
            }

            // Show body for other tools
            if (_toolBody != null)
            {
                _toolBody.style.display = DisplayStyle.Flex;
            }
            if (_headerContent != null)
            {
                _headerContent.style.display = DisplayStyle.None;
            }

            // Try to create custom content for specialized tools
            var customContent = createCustomContent?.Invoke(toolUse);
            if (customContent != null)
            {
                SetCustomContent(customContent);
            }
            else
            {
                // Fall back to collapsible input/output
                ShowStandardContent(toolUse);
            }
        }

        private void SetCustomContent(VisualElement content)
        {
            if (_toolBody == null) return;

            // Remove previous custom content
            if (_customContent != null)
            {
                _toolBody.Remove(_customContent);
                _customContent = null;
            }

            // Hide IO container
            if (_ioContainer != null) _ioContainer.style.display = DisplayStyle.None;

            // Add custom content
            _customContent = content;
            _customContent.name = "custom-content";
            _toolBody.Insert(0, _customContent);
        }

        private void ShowStandardContent(ToolUse toolUse)
        {
            // Remove custom content if present
            if (_customContent != null && _toolBody != null)
            {
                _toolBody.Remove(_customContent);
                _customContent = null;
            }

            if (_ioContainer == null) return;

            _ioContainer.style.display = DisplayStyle.Flex;
            _ioContainer.Clear();
            CreateCollapsibleIO(toolUse);
        }

        private void CreateCollapsibleIO(ToolUse toolUse)
        {
            // Restore expand state from the model so it survives element recycling/rebind.
            _ioToolUse = toolUse;
            _ioExpanded = toolUse.IsIoExpanded;

            // Header row (clickable)
            _ioHeader = new VisualElement();
            _ioHeader.AddToClassList("sk-tool-io-header");
            _ioHeader.RegisterCallback<ClickEvent>(OnIOHeaderClicked);

            var label = new Label("Input/Output");
            label.AddToClassList("sk-tool-io-label");
            _ioHeader.Add(label);

            _ioChevron = new VisualElement();
            _ioChevron.AddToClassList("sk-tool-io-chevron");
            _ioHeader.Add(_ioChevron);

            _ioContainer.Add(_ioHeader);

            // Content (hidden by default)
            _ioContent = new VisualElement();
            _ioContent.AddToClassList("sk-tool-io-content");

            // Input section
            var inputSection = new VisualElement();
            inputSection.AddToClassList("sk-tool-io-section");

            var inputHeader = new Label("Input");
            inputHeader.AddToClassList("sk-tool-io-section-header");
            inputSection.Add(inputHeader);

            _inputLabel = new Label(FormatInput(toolUse.Input));
            _inputLabel.AddToClassList("sk-tool-input");
            inputSection.Add(_inputLabel);

            _ioContent.Add(inputSection);

            // Output section (if has output)
            if (!string.IsNullOrEmpty(toolUse.Output))
            {
                var outputSection = new VisualElement();
                outputSection.AddToClassList("sk-tool-io-section");

                var outputHeader = new Label("Output");
                outputHeader.AddToClassList("sk-tool-io-section-header");
                outputSection.Add(outputHeader);

                _outputLabel = new Label(toolUse.Output);
                _outputLabel.AddToClassList("sk-tool-output");
                outputSection.Add(_outputLabel);

                _ioContent.Add(outputSection);
            }

            _ioContainer.Add(_ioContent);

            UpdateIOExpandedState();
        }

        private void OnIOHeaderClicked(ClickEvent evt)
        {
            _ioExpanded = !_ioExpanded;
            if (_ioToolUse != null) _ioToolUse.IsIoExpanded = _ioExpanded;
            UpdateIOExpandedState();
            evt.StopPropagation();
        }

        private void UpdateIOExpandedState()
        {
            var isExpanded = _ioExpanded;

            // Chevron icon
            if (_ioChevron != null)
            {
                var icon = isExpanded ? _chevronUpIcon : _chevronDownIcon;
                if (icon != null)
                {
                    _ioChevron.style.backgroundImage = new StyleBackground(icon);
                }
            }

            // Content visibility
            if (_ioContent != null)
            {
                _ioContent.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private static string FormatInput(JToken input)
        {
            if (input == null) return "";

            try
            {
                return input.Type == JTokenType.String ? input.ToString() : input.ToString(Formatting.Indented);
            }
            catch
            {
                return input.ToString();
            }
        }
    }
}
