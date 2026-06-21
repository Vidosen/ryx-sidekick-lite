// SPDX-License-Identifier: GPL-3.0-only
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Contracts
{
    internal interface IMarkdownContentRenderer
    {
        VisualElement Render(string markdown, MarkdownRenderContext context = null);

        VisualElement Render(MarkdownDocument document, MarkdownRenderContext context = null);

        string RenderInlinesToRichText(ContainerInline inlines, MarkdownRenderContext context = null);

        void RegisterBlockRenderer(IMarkdownBlockRenderer renderer);

        void RegisterInlineRenderer(IMarkdownInlineRenderer renderer);

        bool UnregisterBlockRenderer(IMarkdownBlockRenderer renderer);

        bool UnregisterInlineRenderer(IMarkdownInlineRenderer renderer);

        void Reset();

        void RegisterPipelineConfigurator(IMarkdownPipelineConfigurator configurator);

        bool UnregisterPipelineConfigurator(IMarkdownPipelineConfigurator configurator);

        void InvalidatePipeline();
    }
}
