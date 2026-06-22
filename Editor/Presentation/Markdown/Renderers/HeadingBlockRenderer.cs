// SPDX-License-Identifier: GPL-3.0-only
using Markdig.Syntax;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    internal class HeadingBlockRenderer : IMarkdownBlockRenderer
    {
        public int Priority => 100;

        public bool CanRender(Block block) => block is HeadingBlock;

        public void Render(Block block, VisualElement parent, MarkdownRenderContext context, RenderChildrenDelegate renderChildren)
        {
            var heading = (HeadingBlock)block;

            var text = MarkdownRenderer.RenderInlinesToRichText(heading.Inline, context);

            // Wrap in container for proper border support
            var container = new VisualElement();
            container.AddToClassList(context.Class("heading"));
            container.AddToClassList(context.Class($"h{heading.Level}"));

            var headingText = new MarkdownTextElement();
            headingText.AddToClassList(context.Class("heading-text"));
            headingText.SetContent(text, context.Spans);

            container.Add(headingText);
            parent.Add(container);
        }
    }
}


