// SPDX-License-Identifier: GPL-3.0-only
using Markdig.Syntax;
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
            var text = MarkdownRenderer.RenderInlinesToRichText(paragraph.Inline, context);

            // The whole paragraph is one selectable rich-text element; inline-code chips are
            // drawn behind the text and asset links overlaid (see MarkdownTextElement).
            var element = new MarkdownTextElement();
            element.AddToClassList(context.Class("p"));
            element.SetContent(text, context.Spans);
            parent.Add(element);
        }
    }
}
