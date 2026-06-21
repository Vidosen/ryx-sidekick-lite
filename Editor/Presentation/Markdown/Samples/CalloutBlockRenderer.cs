// SPDX-License-Identifier: GPL-3.0-only
using Markdig.Syntax;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Samples
{
    /// <summary>
    /// Sample extension: Renders blockquotes starting with specific keywords as styled callouts.
    /// </summary>
    internal class CalloutBlockRenderer : IMarkdownBlockRenderer
    {
        public int Priority => 50;

        public bool CanRender(Block block)
        {
            if (block is not QuoteBlock quote) return false;

            if (quote.Count > 0 && quote[0] is ParagraphBlock para)
            {
                var text = MarkdownRenderer.RenderInlinesToRichText(para.Inline, null);
                return text.StartsWith("[!NOTE]") ||
                       text.StartsWith("[!WARNING]") ||
                       text.StartsWith("[!TIP]") ||
                       text.StartsWith("[!IMPORTANT]") ||
                       text.StartsWith("[!CAUTION]");
            }
            return false;
        }

        public void Render(Block block, VisualElement parent, MarkdownRenderContext context, RenderChildrenDelegate renderChildren)
        {
            var quote = (QuoteBlock)block;

            string calloutType = "note";
            string icon = "i";

            if (quote[0] is ParagraphBlock para)
            {
                var text = MarkdownRenderer.RenderInlinesToRichText(para.Inline, null);

                if (text.StartsWith("[!WARNING]"))
                {
                    calloutType = "warning";
                    icon = "!";
                }
                else if (text.StartsWith("[!TIP]"))
                {
                    calloutType = "tip";
                    icon = "*";
                }
                else if (text.StartsWith("[!IMPORTANT]"))
                {
                    calloutType = "important";
                    icon = "!";
                }
                else if (text.StartsWith("[!CAUTION]"))
                {
                    calloutType = "caution";
                    icon = "x";
                }
            }

            var container = new VisualElement();
            container.AddToClassList(context.Class("callout"));
            container.AddToClassList(context.Class($"callout-{calloutType}"));

            var header = new VisualElement();
            header.AddToClassList(context.Class("callout-header"));

            var iconLabel = new Label(icon);
            iconLabel.AddToClassList(context.Class("callout-icon"));
            header.Add(iconLabel);

            var titleLabel = new Label(calloutType.ToUpperInvariant());
            titleLabel.AddToClassList(context.Class("callout-title"));
            header.Add(titleLabel);

            container.Add(header);

            var content = new VisualElement();
            content.AddToClassList(context.Class("callout-content"));

            bool first = true;
            foreach (var child in quote)
            {
                if (first && child is ParagraphBlock firstPara)
                {
                    var text = MarkdownRenderer.RenderInlinesToRichText(firstPara.Inline, context);
                    var prefixEnd = text.IndexOf(']');
                    if (prefixEnd > 0 && prefixEnd + 1 < text.Length)
                    {
                        text = text[(prefixEnd + 1)..].TrimStart();
                    }

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var label = new Label(text) { enableRichText = true };
                        label.AddToClassList(context.Class("p"));
                        content.Add(label);
                    }
                    first = false;
                    continue;
                }

                renderChildren(quote, content, context);
                break;
            }

            container.Add(content);
            parent.Add(container);
        }
    }
}

