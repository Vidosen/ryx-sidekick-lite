// SPDX-License-Identifier: GPL-3.0-only
using Markdig.Syntax;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Samples
{
    /// <summary>
    /// Sample extension: Renders fenced code blocks with "diff" language as styled diff views.
    /// </summary>
    internal class DiffBlockRenderer : IMarkdownBlockRenderer
    {
        public int Priority => 50;

        public bool CanRender(Block block)
        {
            return block is FencedCodeBlock fcb &&
                   (fcb.Info?.ToLowerInvariant() == "diff" || fcb.Info?.ToLowerInvariant() == "patch");
        }

        public void Render(Block block, VisualElement parent, MarkdownRenderContext context, RenderChildrenDelegate renderChildren)
        {
            var codeBlock = (FencedCodeBlock)block;
            var lines = codeBlock.Lines.ToString().Split('\n');

            var container = new VisualElement();
            container.AddToClassList(context.Class("diff-block"));

            var header = new VisualElement();
            header.AddToClassList(context.Class("diff-header"));
            header.Add(new Label("Diff"));
            container.Add(header);

            var content = new VisualElement();
            content.AddToClassList(context.Class("diff-body"));

            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;

                var lineElement = new Label(line);
                lineElement.AddToClassList(context.Class("diff-line"));

                if (line.StartsWith("+"))
                {
                    lineElement.AddToClassList(context.Class("diff-added"));
                }
                else if (line.StartsWith("-"))
                {
                    lineElement.AddToClassList(context.Class("diff-removed"));
                }
                else if (line.StartsWith("@@"))
                {
                    lineElement.AddToClassList(context.Class("diff-hunk"));
                }
                else
                {
                    lineElement.AddToClassList(context.Class("diff-context"));
                }

                content.Add(lineElement);
            }

            container.Add(content);
            parent.Add(container);
        }
    }
}

