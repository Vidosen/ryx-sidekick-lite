// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Providers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    /// <summary>
    /// Custom body for MCP tool calls (<c>mcp__server__tool</c>): two nested foldable sections —
    /// INPUT (the JSON arguments, expanded by default) and OUTPUT (the result, collapsed) — each
    /// showing syntax-coloured JSON via <see cref="JsonHighlighter"/>. The foldable card chrome,
    /// purple "MCP" badge and monospace name come from <c>ToolCallElement</c>/<c>ToolHeaderOptions</c>.
    /// </summary>
    internal sealed class McpToolRenderer : IToolElementRenderer
    {
        private static Texture2D _chevron;

        public bool CanRender(ToolUse toolUse) =>
            toolUse != null && ToolPresentationCatalog.GetEffectiveKind(toolUse) == ToolKind.Mcp;

        public VisualElement BuildHeaderContent(ToolUse toolUse) => null;

        public VisualElement BuildBodyContent(ToolUse toolUse)
        {
            if (toolUse == null) return null;

            var container = new VisualElement();
            container.AddToClassList("sk-mcp-content");

            // When the header leads with a human-readable title, surface the technical
            // server.tool address here so it is never lost (mirrors Bash hiding its command).
            if (!string.IsNullOrEmpty(ToolDisplayHelpers.ExtractMcpTitle(toolUse.Input)))
            {
                var address = new Label(ToolPresentationCatalog.ResolveMcpDisplayName(toolUse));
                address.AddToClassList("sk-mcp-address");
                address.selection.isSelectable = true;
                container.Add(address);
            }

            // INPUT — coloured JSON of the tool arguments, expanded by default.
            var inputBody = JsonHighlighter.BuildColoredJson(toolUse.Input);
            container.Add(BuildSection("INPUT", JsonHighlighter.DescribeInput(toolUse.Input), null, inputBody, true));

            // OUTPUT — result; collapsed by default. Plain text when not JSON.
            var output = toolUse.Output ?? "";
            if (!string.IsNullOrEmpty(output))
            {
                var outputBody = JsonHighlighter.BuildFromMaybeJson(output);
                if (toolUse.Status == ToolStatus.Error)
                {
                    outputBody.AddToClassList("sk-mcp-output-text--error");
                }

                var meta = toolUse.Status == ToolStatus.Error ? "error" : null;
                var metaModifier = toolUse.Status == ToolStatus.Error ? "error" : null;
                container.Add(BuildSection("OUTPUT", meta, metaModifier, outputBody, false));
            }
            else if (toolUse.Status == ToolStatus.Running)
            {
                var runningLabel = new Label("Running…");
                runningLabel.AddToClassList("sk-mcp-out-running");
                container.Add(runningLabel);
            }

            return container;
        }

        private static VisualElement BuildSection(
            string title,
            string meta,
            string metaModifier,
            VisualElement body,
            bool expandedByDefault)
        {
            var section = new VisualElement();
            section.AddToClassList("sk-mcp-io-section");

            var header = new VisualElement();
            header.AddToClassList("sk-mcp-io-header");

            var chevron = new VisualElement();
            chevron.AddToClassList("sk-mcp-io-chevron");
            EnsureChevron();
            if (_chevron != null)
            {
                chevron.style.backgroundImage = new StyleBackground(_chevron);
            }
            header.Add(chevron);

            var label = new Label(title);
            label.AddToClassList("sk-mcp-io-label");
            header.Add(label);

            if (!string.IsNullOrEmpty(meta))
            {
                var metaLabel = new Label(meta);
                metaLabel.AddToClassList("sk-mcp-io-meta");
                if (!string.IsNullOrEmpty(metaModifier))
                {
                    metaLabel.AddToClassList($"sk-mcp-io-meta--{metaModifier}");
                }
                header.Add(metaLabel);
            }

            var bodyWrap = new ScrollView(ScrollViewMode.Vertical);
            bodyWrap.AddToClassList("sk-mcp-io-body");
            bodyWrap.Add(body);

            var expanded = expandedByDefault;

            void Apply()
            {
                bodyWrap.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                chevron.style.rotate = new Rotate(new Angle(expanded ? 0f : -90f, AngleUnit.Degree));
            }

            Apply();

            header.RegisterCallback<ClickEvent>(evt =>
            {
                expanded = !expanded;
                Apply();
                evt.StopPropagation();
            });

            section.Add(header);
            section.Add(bodyWrap);
            return section;
        }

        private static void EnsureChevron()
        {
            if (_chevron == null)
            {
                _chevron = AssetDatabase.LoadAssetAtPath<Texture2D>(SidekickUiConstants.ChevronDownIconPath);
            }
        }
    }
}
