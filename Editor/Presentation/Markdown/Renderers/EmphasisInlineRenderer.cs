// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using Markdig.Syntax.Inlines;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    internal class EmphasisInlineRenderer : IMarkdownInlineRenderer
    {
        public int Priority => 100;

        public bool CanRender(Inline inline) => inline is EmphasisInline;

        public void Render(Inline inline, StringBuilder builder, VisualElement parent,
            MarkdownRenderContext context, RenderInlineChildrenDelegate renderChildren)
        {
            var emphasis = (EmphasisInline)inline;

            string openTag, closeTag;

            if (emphasis.DelimiterChar == '~')
            {
                openTag = "<s>";
                closeTag = "</s>";
            }
            else if (emphasis.DelimiterCount >= 2)
            {
                openTag = "<b>";
                closeTag = "</b>";
            }
            else
            {
                openTag = "<i>";
                closeTag = "</i>";
            }

            builder?.Append(openTag);
            renderChildren(emphasis, builder, parent, context);
            builder?.Append(closeTag);
        }
    }
}


