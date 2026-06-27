// SPDX-License-Identifier: GPL-3.0-only
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Presentation.Renderers;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.UI.Elements
{
    /// <summary>
    /// Optional presentation for the collapsed-foldable tool header. A <c>default</c> value
    /// (Foldable = false) leaves the tool rendered with the classic always-expanded header.
    /// </summary>
    internal struct ToolHeaderOptions
    {
        public bool Foldable;
        public string MetaText;
        public string IconKey;

        /// <summary>When set, renders a coloured pill badge (e.g. "MCP") in place of the icon.</summary>
        public string BadgeText;

        /// <summary>When true, renders the tool name in the monospace font (e.g. MCP server.tool).</summary>
        public bool MonospaceName;
    }

    internal sealed class ToolCallElement : VisualElement
    {
        private const string TemplatePath = SidekickUiConstants.ToolCallTemplatePath;
        private const string ChevronDownPath = SidekickUiConstants.ChevronDownIconPath;
        private const string ChevronUpPath = SidekickUiConstants.ChevronUpIconPath;

        private static VisualTreeAsset _template;
        private static Texture2D _chevronDownIcon;
        private static Texture2D _chevronUpIcon;

        private readonly VisualElement _root;
        private readonly VisualElement _header;
        private readonly VisualElement _statusDot;
        private readonly VisualElement _toolIcon;
        private readonly VisualElement _headerBadge;
        private readonly VisualElement _headerBadgeGlyph;
        private readonly Label _headerBadgeLabel;
        private readonly Label _name;
        private readonly Label _headerMeta;
        private readonly VisualElement _headerChevron;
        private readonly VisualElement _headerContent;
        private readonly VisualElement _toolBody;
        private readonly VisualElement _ioContainer;

        private VisualElement _customContent;
        private readonly Label _autoAcceptedChip;

        // Foldable-header state (per instance, opt-in via ToolHeaderOptions)
        private bool _foldable;
        private ToolUse _foldToolUse;

        // Painter2D terminal glyph (drawn instead of a texture for the Bash/Terminal tool)
        private static readonly Color TerminalGlyphFallbackColor = new Color(0.431f, 0.431f, 0.431f, 1f); // #6e6e6e
        private bool _toolIconPainterRegistered;
        private bool _drawTerminalGlyph;

        // Painter2D MCP badge hexagon (drawn inside the header badge pill)
        private static readonly Color McpBadgeFallbackColor = new Color(0.706f, 0.557f, 0.678f, 1f); // #b48ead
        private bool _badgePainterRegistered;
        private bool _drawMcpBadge;

        // Running spinner (code-driven; USS has no @keyframes)
        private IVisualElementScheduledItem _spinnerItem;
        private float _spinnerAngle;

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

            _root = this.Q<VisualElement>("tool-call");
            _header = this.Q<VisualElement>("tool-header");
            _statusDot = this.Q<VisualElement>("status-dot");
            _toolIcon = this.Q<VisualElement>("tool-icon");
            _headerBadge = this.Q<VisualElement>("header-badge");
            _headerBadgeGlyph = this.Q<VisualElement>("header-badge-glyph");
            _headerBadgeLabel = this.Q<Label>("header-badge-label");
            _name = this.Q<Label>("name");
            _headerMeta = this.Q<Label>("header-meta");
            _headerChevron = this.Q<VisualElement>("header-chevron");
            _autoAcceptedChip = this.Q<Label>("auto-accepted-chip");
            _headerContent = this.Q<VisualElement>("header-content");
            _toolBody = this.Q<VisualElement>("tool-body");
            _ioContainer = this.Q<VisualElement>("io-container");

            // Load icons
            _chevronDownIcon ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ChevronDownPath);
            _chevronUpIcon ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ChevronUpPath);

            if (_header != null)
            {
                _header.RegisterCallback<ClickEvent>(OnHeaderClicked);
            }
            RegisterCallback<DetachFromPanelEvent>(_ => StopSpinner());
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
            Func<ToolUse, VisualElement> createHeaderContent = null,
            Func<ToolUse, ToolHeaderOptions> getHeaderOptions = null)
        {
            if (toolUse == null) return;

            var toolName = string.IsNullOrEmpty(toolUse.Name) ? "tool" : toolUse.Name;

            var headerOptions = getHeaderOptions?.Invoke(toolUse) ?? default;

            // Update status dot / spinner
            UpdateStatusIndicator(toolUse, headerOptions.Foldable);

            if (_name != null)
            {
                _name.text = getToolDisplayName?.Invoke(toolUse) ?? toolName;
            }

            // Apply (or clear) the collapsed-foldable header chrome.
            if (headerOptions.Foldable)
            {
                ApplyFoldableHeader(toolUse, headerOptions);
            }
            else
            {
                ClearFoldableHeader();
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

            // For foldable tools the header controls body visibility (collapsed by default).
            if (_foldable)
            {
                UpdateFoldExpandedState();
            }
        }

        private void UpdateStatusIndicator(ToolUse toolUse, bool foldable)
        {
            if (_statusDot == null) return;

            _statusDot.RemoveFromClassList("running");
            _statusDot.RemoveFromClassList("success");
            _statusDot.RemoveFromClassList("error");
            _statusDot.RemoveFromClassList("pending");
            _statusDot.AddToClassList(toolUse.Status.ToString().ToLowerInvariant());

            if (foldable && toolUse.Status == ToolStatus.Running)
            {
                StartSpinner();
            }
            else
            {
                StopSpinner();
            }
        }

        private void StartSpinner()
        {
            if (_statusDot == null) return;

            _statusDot.AddToClassList("sk-tool-spinner");
            if (_spinnerItem != null) return; // already spinning

            _spinnerAngle = 0f;
            _spinnerItem = _statusDot.schedule.Execute(() =>
            {
                _spinnerAngle = (_spinnerAngle + 30f) % 360f;
                _statusDot.style.rotate = new Rotate(new Angle(_spinnerAngle, AngleUnit.Degree));
            }).Every(32);
        }

        private void StopSpinner()
        {
            _spinnerItem?.Pause();
            _spinnerItem = null;
            if (_statusDot != null)
            {
                _statusDot.RemoveFromClassList("sk-tool-spinner");
                _statusDot.style.rotate = StyleKeyword.Null;
            }
        }

        private void ApplyFoldableHeader(ToolUse toolUse, ToolHeaderOptions options)
        {
            _foldable = true;
            _foldToolUse = toolUse;

            _header?.AddToClassList("sk-tool-header--foldable");
            _root?.AddToClassList("sk-tool-call--card");
            if (_root != null)
            {
                if (toolUse.Status == ToolStatus.Error)
                    _root.AddToClassList("sk-tool-call--error");
                else
                    _root.RemoveFromClassList("sk-tool-call--error");
            }

            // Header badge (e.g. MCP) — replaces the icon when present.
            ApplyHeaderBadge(options.BadgeText);
            var hasBadge = !string.IsNullOrEmpty(options.BadgeText);

            // Monospace tool name (e.g. MCP "server.tool").
            if (_name != null)
            {
                if (options.MonospaceName)
                    _name.AddToClassList("sk-tool-name--mono");
                else
                    _name.RemoveFromClassList("sk-tool-name--mono");
            }

            // Tool icon (suppressed when a badge takes its place)
            if (_toolIcon != null && hasBadge)
            {
                _drawTerminalGlyph = false;
                _toolIcon.style.display = DisplayStyle.None;
                _toolIcon.MarkDirtyRepaint();
            }
            else if (_toolIcon != null)
            {
                if (options.IconKey == ToolPresentationCatalog.GetIconKey(ToolKind.Bash))
                {
                    // Vector-drawn ">_" terminal glyph (Painter2D), matching the design.
                    _drawTerminalGlyph = true;
                    _toolIcon.style.backgroundImage = StyleKeyword.None;
                    EnsureToolIconPainter();
                    _toolIcon.style.display = DisplayStyle.Flex;
                    _toolIcon.MarkDirtyRepaint();
                }
                else
                {
                    _drawTerminalGlyph = false;
                    var icon = SidekickIconCatalog.GetIcon(options.IconKey);
                    if (icon != null)
                    {
                        _toolIcon.style.backgroundImage = new StyleBackground(icon);
                        _toolIcon.style.display = DisplayStyle.Flex;
                    }
                    else
                    {
                        _toolIcon.style.display = DisplayStyle.None;
                    }
                    _toolIcon.MarkDirtyRepaint();
                }
            }

            // Right-aligned meta (e.g. line count)
            if (_headerMeta != null)
            {
                if (!string.IsNullOrEmpty(options.MetaText))
                {
                    _headerMeta.text = options.MetaText;
                    _headerMeta.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _headerMeta.style.display = DisplayStyle.None;
                }
            }

            if (_headerChevron != null)
            {
                _headerChevron.style.display = DisplayStyle.Flex;
            }
        }

        private void ClearFoldableHeader()
        {
            _foldable = false;
            _foldToolUse = null;
            _drawTerminalGlyph = false;
            _drawMcpBadge = false;

            _header?.RemoveFromClassList("sk-tool-header--foldable");
            _root?.RemoveFromClassList("sk-tool-call--card");
            _root?.RemoveFromClassList("sk-tool-call--error");

            _name?.RemoveFromClassList("sk-tool-name--mono");
            if (_headerBadge != null) _headerBadge.style.display = DisplayStyle.None;

            if (_toolIcon != null)
            {
                _toolIcon.style.display = DisplayStyle.None;
                _toolIcon.MarkDirtyRepaint();
            }
            if (_headerMeta != null) _headerMeta.style.display = DisplayStyle.None;
            if (_headerChevron != null) _headerChevron.style.display = DisplayStyle.None;
        }

        private void EnsureToolIconPainter()
        {
            if (_toolIconPainterRegistered || _toolIcon == null) return;
            _toolIconPainterRegistered = true;
            _toolIcon.generateVisualContent += OnGenerateToolIcon;
        }

        private void OnGenerateToolIcon(MeshGenerationContext mgc)
        {
            if (!_drawTerminalGlyph) return;

            var color = _toolIcon.resolvedStyle.color;
            if (color.a <= 0f) color = TerminalGlyphFallbackColor;
            ToolGlyphPainter.DrawTerminal(mgc, color);
        }

        private void ApplyHeaderBadge(string badgeText)
        {
            if (_headerBadge == null) return;

            if (string.IsNullOrEmpty(badgeText))
            {
                _drawMcpBadge = false;
                _headerBadge.style.display = DisplayStyle.None;
                return;
            }

            if (_headerBadgeLabel != null) _headerBadgeLabel.text = badgeText;
            _drawMcpBadge = true;
            EnsureBadgePainter();
            _headerBadge.style.display = DisplayStyle.Flex;
            _headerBadgeGlyph?.MarkDirtyRepaint();
        }

        private void EnsureBadgePainter()
        {
            if (_badgePainterRegistered || _headerBadgeGlyph == null) return;
            _badgePainterRegistered = true;
            _headerBadgeGlyph.generateVisualContent += OnGenerateBadgeGlyph;
        }

        private void OnGenerateBadgeGlyph(MeshGenerationContext mgc)
        {
            if (!_drawMcpBadge) return;

            var color = _headerBadgeGlyph.resolvedStyle.color;
            if (color.a <= 0f) color = McpBadgeFallbackColor;
            ToolGlyphPainter.DrawMcpHexagon(mgc, color);
        }

        private void OnHeaderClicked(ClickEvent evt)
        {
            if (!_foldable || _foldToolUse == null) return;

            _foldToolUse.IsIoExpanded = !_foldToolUse.IsIoExpanded;
            UpdateFoldExpandedState();
            evt.StopPropagation();
        }

        private void UpdateFoldExpandedState()
        {
            if (_foldToolUse == null) return;

            var expanded = _foldToolUse.IsIoExpanded;

            if (_toolBody != null)
            {
                _toolBody.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_headerChevron != null)
            {
                var icon = expanded ? _chevronUpIcon : _chevronDownIcon;
                if (icon != null)
                {
                    _headerChevron.style.backgroundImage = new StyleBackground(icon);
                }
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
