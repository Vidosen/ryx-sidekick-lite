// SPDX-License-Identifier: GPL-3.0-only
using Markdig;

namespace Ryx.Sidekick.Editor.Presentation.Contracts.Markdown
{
    /// <summary>
    /// Extension point for features that need to customize the Markdig pipeline.
    /// Implementations are discovered and invoked during pipeline construction.
    /// </summary>
    internal interface IMarkdownPipelineConfigurator
    {
        /// <summary>
        /// Priority determines the order configurators run. Lower values execute first.
        /// Use negative values to run before defaults, positive to run after.
        /// </summary>
        int Priority => 0;

        /// <summary>
        /// Called during pipeline construction. Add extensions, swap presets, etc.
        /// </summary>
        void Configure(MarkdownPipelineBuilder builder);
    }
}


