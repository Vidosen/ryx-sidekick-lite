// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using Markdig.Syntax.Inlines;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    internal class LinkInlineRenderer : IMarkdownInlineRenderer
    {
        public int Priority => 100;

        public bool CanRender(Inline inline) => inline is LinkInline;

        public void Render(Inline inline, StringBuilder builder, VisualElement parent,
            MarkdownRenderContext context, RenderInlineChildrenDelegate renderChildren)
        {
            var link = (LinkInline)inline;
            var url = link.Url ?? "";

            if (link.IsImage)
            {
                builder?.Append($"[Image: {link.Title ?? url}]");
                return;
            }

            if (context.UseRichTextForInlines)
            {
                builder?.Append("<color=#61afef>");
                renderChildren(link, builder, parent, context);
                builder?.Append("</color>");
            }
            else
            {
                var linkElement = new Label();
                linkElement.AddToClassList(context.Class("link"));

                var textBuilder = new StringBuilder();
                renderChildren(link, textBuilder, null, context);
                linkElement.text = textBuilder.ToString();
                linkElement.enableRichText = true;

                linkElement.RegisterCallback<ClickEvent>(_ => context.OnLinkClicked?.Invoke(url));

                linkElement.tooltip = url;
                parent?.Add(linkElement);
            }
        }
    }
}


