// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Text;
using Markdig.Syntax.Inlines;
using Ryx.Sidekick.Editor.Infrastructure.Assets;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    internal class LiteralInlineRenderer : IMarkdownInlineRenderer
    {
        // Marker constants for asset path detection in post-processing
        public const string AssetPathMarkerStart = "\u200B\u200C\u200D"; // Zero-width space + non-joiner + joiner
        public const string AssetPathMarkerEnd = "\u200D\u200C\u200B";

        public int Priority => 100;

        public bool CanRender(Inline inline) => inline is LiteralInline;

        public void Render(Inline inline, StringBuilder builder, VisualElement parent,
            MarkdownRenderContext context, RenderInlineChildrenDelegate renderChildren)
        {
            var literal = (LiteralInline)inline;
            var text = literal.Content.ToString();

            if (context.UseRichTextForInlines)
            {
                // Find asset paths in the text and mark them for post-processing
                var assetPaths = AssetLinkService.FindAssetPaths(text);
                
                if (assetPaths.Count > 0)
                {
                    // Store paths for post-processing
                    if (!context.UserData.ContainsKey("AssetPaths"))
                    {
                        context.UserData["AssetPaths"] = new List<string>();
                    }
                    var pathList = (List<string>)context.UserData["AssetPaths"];

                    // Set flag that this content has asset paths
                    context.UserData["HasAssetPaths"] = true;

                    // Build text with markers around asset paths
                    var result = new StringBuilder();
                    int lastIndex = 0;

                    foreach (var match in assetPaths)
                    {
                        // Add text before the match
                        if (match.StartIndex > lastIndex)
                        {
                            result.Append(EscapeRichText(text.Substring(lastIndex, match.StartIndex - lastIndex)));
                        }

                        // Add marked asset path - content between markers will be replaced
                        // by AssetLinkElement in ParagraphBlockRenderer
                        var fileName = AssetLinkService.GetAssetNameWithExtension(match.Path);
                        pathList.Add(match.Path);
                        
                        result.Append(AssetPathMarkerStart);
                        result.Append(fileName); // Just filename, no rich text
                        result.Append(AssetPathMarkerEnd);

                        lastIndex = match.StartIndex + match.Length;
                    }

                    // Add remaining text
                    if (lastIndex < text.Length)
                    {
                        result.Append(EscapeRichText(text[lastIndex..]));
                    }

                    builder?.Append(result);
                }
                else
                {
                    builder?.Append(EscapeRichText(text));
                }
            }
            else
            {
                // In non-rich-text mode, we can create elements directly
                var assetPaths = AssetLinkService.FindAssetPaths(text);
                
                if (assetPaths.Count > 0 && parent != null)
                {
                    int lastIndex = 0;
                    foreach (var match in assetPaths)
                    {
                        // Add text before the match
                        if (match.StartIndex > lastIndex)
                        {
                            var textBefore = text.Substring(lastIndex, match.StartIndex - lastIndex);
                            var label = new Label(textBefore);
                            parent.Add(label);
                        }

                        // Add asset link element
                        var assetLink = new AssetLinkElement(match.Path);
                        parent.Add(assetLink);

                        lastIndex = match.StartIndex + match.Length;
                    }

                    // Add remaining text
                    if (lastIndex < text.Length)
                    {
                        var textAfter = text[lastIndex..];
                        var label = new Label(textAfter);
                        parent.Add(label);
                    }
                }
                else
                {
                    builder?.Append(text);
                }
            }
        }

        private static string EscapeRichText(string text)
        {
            return text
                .Replace("<", "<\u200B")
                .Replace(">", "\u200B>");
        }
    }
}


