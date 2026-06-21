// SPDX-License-Identifier: GPL-3.0-only
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Contracts.Markdown
{
    internal interface IMarkdownBlockRenderer
    {
        int Priority => 100;

        bool CanRender(Block block);

        void Render(Block block, VisualElement parent, MarkdownRenderContext context, RenderChildrenDelegate renderChildren);
    }

    internal interface IMarkdownInlineRenderer
    {
        int Priority => 100;

        bool CanRender(Inline inline);

        void Render(Inline inline, System.Text.StringBuilder builder, VisualElement parent,
            MarkdownRenderContext context, RenderInlineChildrenDelegate renderChildren);
    }

    internal delegate void RenderChildrenDelegate(ContainerBlock container, VisualElement parent, MarkdownRenderContext context);

    internal delegate void RenderInlineChildrenDelegate(ContainerInline container, System.Text.StringBuilder builder,
        VisualElement parent, MarkdownRenderContext context);
}


