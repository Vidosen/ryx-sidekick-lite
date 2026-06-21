// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Markdig.Syntax;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    internal class ParagraphBlockRenderer : IMarkdownBlockRenderer
    {
        public int Priority => 100;

        public bool CanRender(Block block) => block is ParagraphBlock;

        public void Render(Block block, VisualElement parent, MarkdownRenderContext context, RenderChildrenDelegate renderChildren)
        {
            var paragraph = (ParagraphBlock)block;

            // Clear any previous data
            context.UserData.Remove("AssetPaths");
            context.UserData.Remove("HasAssetPaths");
            context.UserData.Remove("CodeInlines");
            context.UserData.Remove("HasCodeInlines");

            var text = MarkdownRenderer.RenderInlinesToRichText(paragraph.Inline, context);

            // Check if we have special elements that need handling
            var hasAssetPaths = context.UserData.ContainsKey("HasAssetPaths") && 
                                (bool)context.UserData["HasAssetPaths"];
            var hasCodeInlines = context.UserData.ContainsKey("HasCodeInlines") && 
                                 (bool)context.UserData["HasCodeInlines"];

            if (hasAssetPaths || hasCodeInlines)
            {
                var assetPaths = hasAssetPaths ? (List<string>)context.UserData["AssetPaths"] : null;
                var codeInlines = hasCodeInlines ? (List<string>)context.UserData["CodeInlines"] : null;
                
                // Create a flow container for mixed content
                var container = CreateFlowContainer(text, assetPaths, codeInlines, context);
                parent.Add(container);
            }
            else
            {
                // Standard rendering - no special elements
                var label = new SelectableLabel(text)
                {
                    enableRichText = true
                };
                label.AddToClassList(context.Class("p"));
                parent.Add(label);
            }

            // Clean up
            context.UserData.Remove("AssetPaths");
            context.UserData.Remove("HasAssetPaths");
            context.UserData.Remove("CodeInlines");
            context.UserData.Remove("HasCodeInlines");
        }

        private VisualElement CreateFlowContainer(string richText, List<string> assetPaths, List<string> codeInlines, MarkdownRenderContext context)
        {
            var container = new VisualElement();
            container.AddToClassList(context.Class("p"));
            container.AddToClassList(context.Class("p-flow"));
            container.style.flexDirection = FlexDirection.Row;
            container.style.flexWrap = Wrap.Wrap;
            container.style.alignItems = Align.FlexStart;

            // Find all markers and their positions
            var markers = new List<(int pos, int endPos, string type, int index)>();
            
            // Find asset path markers
            if (assetPaths != null)
            {
                var markerStart = LiteralInlineRenderer.AssetPathMarkerStart;
                var markerEnd = LiteralInlineRenderer.AssetPathMarkerEnd;
                int idx = 0;
                int searchFrom = 0;
                while (searchFrom < richText.Length)
                {
                    var start = richText.IndexOf(markerStart, searchFrom, StringComparison.Ordinal);
                    if (start == -1) break;
                    var end = richText.IndexOf(markerEnd, start + markerStart.Length, StringComparison.Ordinal);
                    if (end == -1) break;
                    markers.Add((start, end + markerEnd.Length, "asset", idx++));
                    searchFrom = end + markerEnd.Length;
                }
            }
            
            // Find code inline markers
            if (codeInlines != null)
            {
                var codeStart = CodeInlineRenderer.CodeMarkerStart;
                var codeEnd = CodeInlineRenderer.CodeMarkerEnd;
                int idx = 0;
                int searchFrom = 0;
                while (searchFrom < richText.Length)
                {
                    var start = richText.IndexOf(codeStart, searchFrom, StringComparison.Ordinal);
                    if (start == -1) break;
                    var end = richText.IndexOf(codeEnd, start + codeStart.Length, StringComparison.Ordinal);
                    if (end == -1) break;
                    markers.Add((start, end + codeEnd.Length, "code", idx++));
                    searchFrom = end + codeEnd.Length;
                }
            }
            
            // Sort markers by position
            markers.Sort((a, b) => a.pos.CompareTo(b.pos));
            
            int lastIndex = 0;
            foreach (var marker in markers)
            {
                // Add text before marker
                if (marker.pos > lastIndex)
                {
                    var textBefore = richText.Substring(lastIndex, marker.pos - lastIndex);
                    textBefore = CleanOrphanedTags(textBefore);
                    if (!string.IsNullOrWhiteSpace(textBefore))
                    {
                        var label = new SelectableLabel(textBefore) { enableRichText = true };
                        label.AddToClassList(context.Class("p-segment"));
                        container.Add(label);
                    }
                }
                
                // Add the special element
                if (marker.type == "asset" && assetPaths != null && marker.index < assetPaths.Count)
                {
                    var assetLink = new AssetLinkElement(assetPaths[marker.index]);
                    container.Add(assetLink);
                }
                else if (marker.type == "code" && codeInlines != null && marker.index < codeInlines.Count)
                {
                    var codeWrapper = new VisualElement();
                    codeWrapper.AddToClassList(context.Class("code-inline"));

                    var codeLabel = new SelectableLabel(codeInlines[marker.index]);
                    codeLabel.AddToClassList(context.Class("code-inline-text"));
                    CodeInlineRenderer.ApplyMonoFont(codeLabel);
                    var te = codeLabel.Q<UnityEngine.UIElements.TextElement>();
                    if (te != null) CodeInlineRenderer.ApplyMonoFont(te);

                    codeWrapper.Add(codeLabel);
                    container.Add(codeWrapper);
                }
                
                lastIndex = marker.endPos;
            }

            // Add remaining text after last marker
            if (lastIndex < richText.Length)
            {
                var remainingText = richText[lastIndex..];
                remainingText = CleanOrphanedTags(remainingText);
                if (!string.IsNullOrWhiteSpace(remainingText))
                {
                    var label = new SelectableLabel(remainingText) { enableRichText = true };
                    label.AddToClassList(context.Class("p-segment"));
                    container.Add(label);
                }
            }

            return container;
        }

        /// <summary>
        /// Removes orphaned opening/closing rich text tags that are left over after
        /// extracting asset paths. For example, removes trailing "</b>" or leading "<b>".
        /// </summary>
        private static string CleanOrphanedTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Pattern to match orphaned closing tags at the start (e.g., "</b>", "</i>", "</color>")
            // Note: < is escaped as <\u200B in our text
            text = Regex.Replace(text, @"^(<\u200B?/\w+\u200B?>)+", "");
            
            // Pattern to match orphaned opening tags at the end (e.g., "<b>", "<i>", "<color=...>")
            text = Regex.Replace(text, @"(<\u200B?\w+[^>]*\u200B?>)+$", "");

            return text;
        }
    }
}


