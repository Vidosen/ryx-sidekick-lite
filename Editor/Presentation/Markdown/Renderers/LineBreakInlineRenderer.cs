// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using Markdig.Syntax.Inlines;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    internal class LineBreakInlineRenderer : IMarkdownInlineRenderer
    {
        public int Priority => 100;

        public bool CanRender(Inline inline) => inline is LineBreakInline;

        public void Render(Inline inline, StringBuilder builder, VisualElement parent,
            MarkdownRenderContext context, RenderInlineChildrenDelegate renderChildren)
        {
            var lineBreak = (LineBreakInline)inline;

            builder?.Append(lineBreak.IsHard ? "\n" : " ");
        }
    }
}


