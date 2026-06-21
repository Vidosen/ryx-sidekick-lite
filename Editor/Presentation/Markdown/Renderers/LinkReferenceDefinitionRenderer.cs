// SPDX-License-Identifier: GPL-3.0-only
using Markdig.Syntax;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    /// <summary>
    /// Handles LinkReferenceDefinitionGroup blocks by doing nothing.
    /// These are internal Markdig nodes that define link references for the parser
    /// but don't produce visible output.
    /// </summary>
    internal class LinkReferenceDefinitionRenderer : IMarkdownBlockRenderer
    {
        public int Priority => 10;

        public bool CanRender(Block block) => block is LinkReferenceDefinitionGroup;

        public void Render(Block block, VisualElement parent, MarkdownRenderContext context, RenderChildrenDelegate renderChildren)
        {
            // No-op: link references are registered in the pipeline, nothing to display
        }
    }
}


