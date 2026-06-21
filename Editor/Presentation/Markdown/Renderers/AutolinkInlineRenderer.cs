// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using Markdig.Syntax.Inlines;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    internal class AutolinkInlineRenderer : IMarkdownInlineRenderer
    {
        public int Priority => 100;

        public bool CanRender(Inline inline) => inline is AutolinkInline;

        public void Render(Inline inline, StringBuilder builder, VisualElement parent,
            MarkdownRenderContext context, RenderInlineChildrenDelegate renderChildren)
        {
            var autolink = (AutolinkInline)inline;
            var url = autolink.Url;

            if (context.UseRichTextForInlines)
            {
                builder?.Append($"<color=#61afef>{url}</color>");
            }
            else
            {
                var linkElement = new Label(url);
                linkElement.AddToClassList(context.Class("link"));
                linkElement.AddToClassList(context.Class("autolink"));

                linkElement.RegisterCallback<ClickEvent>(_ =>
                {
                    context.OnLinkClicked?.Invoke(autolink.IsEmail ? $"mailto:{url}" : url);
                });

                linkElement.tooltip = url;
                parent?.Add(linkElement);
            }
        }
    }
}


