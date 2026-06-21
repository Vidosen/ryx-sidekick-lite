// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown
{
    /// <summary>
    /// Main entry point for rendering Markdown to UI Toolkit elements.
    /// Dispatches to registered block/inline handlers with priority ordering.
    /// </summary>
    internal static class MarkdownRenderer
    {
        private static readonly List<IMarkdownBlockRenderer> BlockRenderers = new();
        private static readonly List<IMarkdownInlineRenderer> InlineRenderers = new();
        private static readonly object Lock = new();
        private static bool _defaultsRegistered;

        #region Registration API

        public static void RegisterBlockRenderer(IMarkdownBlockRenderer renderer)
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            lock (Lock)
            {
                if (!BlockRenderers.Contains(renderer))
                {
                    BlockRenderers.Add(renderer);
                    BlockRenderers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                }
            }
        }

        public static void RegisterInlineRenderer(IMarkdownInlineRenderer renderer)
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            lock (Lock)
            {
                if (!InlineRenderers.Contains(renderer))
                {
                    InlineRenderers.Add(renderer);
                    InlineRenderers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                }
            }
        }

        public static bool UnregisterBlockRenderer(IMarkdownBlockRenderer renderer)
        {
            lock (Lock) { return BlockRenderers.Remove(renderer); }
        }

        public static bool UnregisterInlineRenderer(IMarkdownInlineRenderer renderer)
        {
            lock (Lock) { return InlineRenderers.Remove(renderer); }
        }

        public static void Reset()
        {
            lock (Lock)
            {
                BlockRenderers.Clear();
                InlineRenderers.Clear();
                _defaultsRegistered = false;
            }
        }

        #endregion

        #region Rendering

        public static VisualElement Render(string markdown, MarkdownRenderContext context = null)
        {
            EnsureDefaultsRegistered();

            context ??= new MarkdownRenderContext();
            var document = Markdig.Markdown.Parse(markdown ?? string.Empty, MarkdownPipelineProvider.Pipeline);

            var root = context.Parent ?? new VisualElement();
            root.AddToClassList(context.Class("root"));

            RenderBlocks(document, root, context);

            return root;
        }

        public static VisualElement Render(MarkdownDocument document, MarkdownRenderContext context = null)
        {
            EnsureDefaultsRegistered();

            context ??= new MarkdownRenderContext();

            var root = context.Parent ?? new VisualElement();
            root.AddToClassList(context.Class("root"));

            RenderBlocks(document, root, context);

            return root;
        }

        public static string RenderInlinesToRichText(ContainerInline inlines, MarkdownRenderContext context = null)
        {
            EnsureDefaultsRegistered();

            context ??= new MarkdownRenderContext { UseRichTextForInlines = true };
            var sb = new StringBuilder();

            RenderInlines(inlines, sb, null, context);

            return sb.ToString();
        }

        private static void RenderBlocks(ContainerBlock container, VisualElement parent, MarkdownRenderContext context)
        {
            foreach (var block in container)
            {
                RenderBlock(block, parent, context);
            }
        }

        private static void RenderBlock(Block block, VisualElement parent, MarkdownRenderContext context)
        {
            IMarkdownBlockRenderer handler = null;
            lock (Lock)
            {
                handler = BlockRenderers.FirstOrDefault(r => r.CanRender(block));
            }

            if (handler != null)
            {
                handler.Render(block, parent, context, RenderBlocks);
            }
            else
            {
                var fallback = new Label($"[Unsupported: {block.GetType().Name}]");
                fallback.AddToClassList(context.Class("unsupported"));
                parent.Add(fallback);
            }
        }

        internal static void RenderInlines(ContainerInline container, StringBuilder builder, VisualElement parent, MarkdownRenderContext context)
        {
            if (container == null) return;

            foreach (var inline in container)
            {
                RenderInline(inline, builder, parent, context);
            }
        }

        private static void RenderInline(Inline inline, StringBuilder builder, VisualElement parent, MarkdownRenderContext context)
        {
            IMarkdownInlineRenderer handler = null;
            lock (Lock)
            {
                handler = InlineRenderers.FirstOrDefault(r => r.CanRender(inline));
            }

            if (handler != null)
            {
                handler.Render(inline, builder, parent, context, RenderInlines);
            }
            else if (inline is LiteralInline literal)
            {
                builder?.Append(literal.Content.ToString());
            }
        }

        private static void EnsureDefaultsRegistered()
        {
            lock (Lock)
            {
                if (_defaultsRegistered) return;

                RegisterBlockRenderer(new Renderers.ParagraphBlockRenderer());
                RegisterBlockRenderer(new Renderers.HeadingBlockRenderer());
                RegisterBlockRenderer(new Renderers.FencedCodeBlockRenderer());
                RegisterBlockRenderer(new Renderers.CodeBlockRenderer());
                RegisterBlockRenderer(new Renderers.ListBlockRenderer());
                RegisterBlockRenderer(new Renderers.QuoteBlockRenderer());
                RegisterBlockRenderer(new Renderers.ThematicBreakRenderer());
                RegisterBlockRenderer(new Renderers.TableBlockRenderer());
                RegisterBlockRenderer(new Renderers.LinkReferenceDefinitionRenderer());

                RegisterInlineRenderer(new Renderers.LiteralInlineRenderer());
                RegisterInlineRenderer(new Renderers.EmphasisInlineRenderer());
                RegisterInlineRenderer(new Renderers.AssetPathCodeInlineRenderer()); // Priority 50, before CodeInlineRenderer
                RegisterInlineRenderer(new Renderers.CodeInlineRenderer());
                RegisterInlineRenderer(new Renderers.LinkInlineRenderer());
                RegisterInlineRenderer(new Renderers.LineBreakInlineRenderer());
                RegisterInlineRenderer(new Renderers.AutolinkInlineRenderer());

                _defaultsRegistered = true;
            }
        }

        #endregion
    }
}