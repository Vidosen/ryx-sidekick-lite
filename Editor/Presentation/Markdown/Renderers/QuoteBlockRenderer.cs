// SPDX-License-Identifier: GPL-3.0-only
using Markdig.Syntax;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    internal class QuoteBlockRenderer : IMarkdownBlockRenderer
    {
        public int Priority => 100;

        public bool CanRender(Block block) => block is QuoteBlock;

        public void Render(Block block, VisualElement parent, MarkdownRenderContext context, RenderChildrenDelegate renderChildren)
        {
            var quote = (QuoteBlock)block;

            if (context.MaxNestingDepth > 0 && context.CurrentDepth >= context.MaxNestingDepth)
            {
                var truncated = new Label("[Quote truncated - max depth reached]");
                truncated.AddToClassList(context.Class("truncated"));
                parent.Add(truncated);
                return;
            }

            var container = new VisualElement();
            container.AddToClassList(context.Class("blockquote"));

            context.CurrentDepth++;
            renderChildren(quote, container, context);
            context.CurrentDepth--;

            parent.Add(container);
        }
    }
}


