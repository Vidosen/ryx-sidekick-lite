// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Markdig;
using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown
{
    /// <summary>
    /// Provides a lazily-built, configurable Markdig pipeline.
    /// Thread-safe singleton; call <see cref="InvalidatePipeline"/> to rebuild after registering new configurators.
    /// </summary>
    internal static class MarkdownPipelineProvider
    {
        private static MarkdownPipeline _pipeline;
        private static readonly object Lock = new();
        private static readonly List<IMarkdownPipelineConfigurator> Configurators = new();
        private static bool _dirty = true;

        /// <summary>
        /// Gets the shared pipeline instance. Lazily built on first access.
        /// </summary>
        public static MarkdownPipeline Pipeline
        {
            get
            {
                lock (Lock)
                {
                    if (_dirty || _pipeline == null)
                    {
                        _pipeline = BuildPipeline();
                        _dirty = false;
                    }
                    return _pipeline;
                }
            }
        }

        /// <summary>
        /// Registers a configurator that will be invoked during pipeline construction.
        /// Call <see cref="InvalidatePipeline"/> afterwards to apply changes.
        /// </summary>
        public static void RegisterConfigurator(IMarkdownPipelineConfigurator configurator)
        {
            if (configurator == null) throw new ArgumentNullException(nameof(configurator));

            lock (Lock)
            {
                if (!Configurators.Contains(configurator))
                {
                    Configurators.Add(configurator);
                    _dirty = true;
                }
            }
        }

        /// <summary>
        /// Removes a previously registered configurator.
        /// </summary>
        public static bool UnregisterConfigurator(IMarkdownPipelineConfigurator configurator)
        {
            lock (Lock)
            {
                var removed = Configurators.Remove(configurator);
                if (removed) _dirty = true;
                return removed;
            }
        }

        /// <summary>
        /// Forces the pipeline to be rebuilt on next access.
        /// </summary>
        public static void InvalidatePipeline()
        {
            lock (Lock)
            {
                _dirty = true;
            }
        }

        /// <summary>
        /// Clears all configurators and resets to defaults. Primarily for testing.
        /// </summary>
        public static void Reset()
        {
            lock (Lock)
            {
                Configurators.Clear();
                _pipeline = null;
                _dirty = true;
            }
        }

        private static MarkdownPipeline BuildPipeline()
        {
            var builder = new MarkdownPipelineBuilder();

            // Apply default extensions useful for chat/code contexts
            builder
                .UseEmphasisExtras()
                .UseAutoLinks()
                .UsePipeTables()
                .UseTaskLists()
                .UseAutoIdentifiers();

            foreach (var cfg in Configurators.OrderBy(c => c.Priority))
            {
                cfg.Configure(builder);
            }

            return builder.Build();
        }
    }
}


