// SPDX-License-Identifier: GPL-3.0-only
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Ryx.Sidekick.Editor.Infrastructure.Markdown;
using Ryx.Sidekick.Editor.Presentation.Contracts;
using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Infrastructure
{
    internal sealed class MarkdownContentRenderer : IMarkdownContentRenderer
    {
        public VisualElement Render(string markdown, MarkdownRenderContext context = null)
        {
            return MarkdownRenderer.Render(markdown, context);
        }

        public VisualElement Render(MarkdownDocument document, MarkdownRenderContext context = null)
        {
            return MarkdownRenderer.Render(document, context);
        }

        public string RenderInlinesToRichText(ContainerInline inlines, MarkdownRenderContext context = null)
        {
            return MarkdownRenderer.RenderInlinesToRichText(inlines, context);
        }

        public void RegisterBlockRenderer(IMarkdownBlockRenderer renderer)
        {
            MarkdownRenderer.RegisterBlockRenderer(renderer);
        }

        public void RegisterInlineRenderer(IMarkdownInlineRenderer renderer)
        {
            MarkdownRenderer.RegisterInlineRenderer(renderer);
        }

        public bool UnregisterBlockRenderer(IMarkdownBlockRenderer renderer)
        {
            return MarkdownRenderer.UnregisterBlockRenderer(renderer);
        }

        public bool UnregisterInlineRenderer(IMarkdownInlineRenderer renderer)
        {
            return MarkdownRenderer.UnregisterInlineRenderer(renderer);
        }

        public void Reset()
        {
            MarkdownRenderer.Reset();
        }

        public void RegisterPipelineConfigurator(IMarkdownPipelineConfigurator configurator)
        {
            MarkdownPipelineProvider.RegisterConfigurator(configurator);
        }

        public bool UnregisterPipelineConfigurator(IMarkdownPipelineConfigurator configurator)
        {
            return MarkdownPipelineProvider.UnregisterConfigurator(configurator);
        }

        public void InvalidatePipeline()
        {
            MarkdownPipelineProvider.InvalidatePipeline();
        }
    }
}
