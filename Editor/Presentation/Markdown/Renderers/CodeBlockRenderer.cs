// SPDX-License-Identifier: GPL-3.0-only
using Markdig.Syntax;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    internal class CodeBlockRenderer : IMarkdownBlockRenderer
    {
        public int Priority => 100;

        public bool CanRender(Block block) => block is CodeBlock && block is not FencedCodeBlock;

        public void Render(Block block, VisualElement parent, MarkdownRenderContext context, RenderChildrenDelegate renderChildren)
        {
            var codeBlock = (CodeBlock)block;
            var code = codeBlock.Lines.ToString();

            var container = new VisualElement();
            container.AddToClassList(context.Class("code-block"));
            container.AddToClassList(context.Class("code-indented"));

            var codeContent = new Label(code);
            codeContent.AddToClassList(context.Class("code-content"));
            codeContent.selection.isSelectable = true;
            container.Add(codeContent);

            parent.Add(container);
        }
    }
}


