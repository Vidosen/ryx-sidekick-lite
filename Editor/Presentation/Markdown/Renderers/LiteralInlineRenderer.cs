// SPDX-License-Identifier: GPL-3.0-only
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
        public int Priority => 100;

        public bool CanRender(Inline inline) => inline is LiteralInline;

        public void Render(Inline inline, StringBuilder builder, VisualElement parent,
            MarkdownRenderContext context, RenderInlineChildrenDelegate renderChildren)
        {
            var literal = (LiteralInline)inline;
            var text = literal.Content.ToString();

            if (context.UseRichTextForInlines && builder != null)
            {
                AppendForSingleElement(text, builder, context);
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

        private static void AppendForSingleElement(string text, StringBuilder builder, MarkdownRenderContext context)
        {
            var assetPaths = AssetLinkService.FindAssetPaths(text);
            if (assetPaths.Count == 0)
            {
                builder.Append(MarkdownRichText.EscapeAngles(text));
                return;
            }

            int lastIndex = 0;
            foreach (var match in assetPaths)
            {
                if (match.StartIndex > lastIndex)
                {
                    builder.Append(MarkdownRichText.EscapeAngles(
                        text.Substring(lastIndex, match.StartIndex - lastIndex)));
                }

                // Reserve a transparent placeholder under the overlaid AssetLinkElement so
                // the span occupies real layout width and stays selectable/copyable.
                var fileName = AssetLinkService.GetAssetNameWithExtension(match.Path);
                MarkdownRichText.AppendAssetPlaceholder(builder, fileName, context.Spans, match.Path);

                lastIndex = match.StartIndex + match.Length;
            }

            if (lastIndex < text.Length)
                builder.Append(MarkdownRichText.EscapeAngles(text[lastIndex..]));
        }
    }
}


