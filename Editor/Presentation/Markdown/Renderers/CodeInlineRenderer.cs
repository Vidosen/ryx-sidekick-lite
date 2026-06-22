// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using Markdig.Syntax.Inlines;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    internal class CodeInlineRenderer : IMarkdownInlineRenderer
    {
        private const string FontTtfPath = SidekickUiConstants.RobotoMonoFontTtfPath;
        private static Font s_monoFont;

        // Single-element mode: inline code is wrapped so a chip can be drawn behind it.
        // The TextCore FontAsset name resolved by the <font> tag (see SidekickUiConstants).
        private const string MonoFontName = SidekickUiConstants.RobotoMonoFontAssetName;
        // Code text color (matches --sk-code-inline-text).
        private const string CodeColor = "#9cdcfe";
        // Mono <font> tag DISABLED. The font name "RobotoMono-wght" resolves only
        // intermittently in the App UI panel: across the real render paths (streaming,
        // ListView rebind, domain-reload auto-resume) TextCore frequently fails to resolve
        // it and renders the OPENING <font="…"> tag literally (consuming only </font>).
        // Eager-loading the FontAsset (lazy, render-path preload, and InitializeOnLoad) did
        // not make it reliable. Inline code therefore stays chip + accent color (no mono).
        // Per-span mono is deferred to the custom inline-block approach — see
        // Documentation~/MarkdownInlineBlocks.md.
        private static readonly bool EmitMonoFont = false;
        // Loaded once so the TextCore FontAsset registers itself in the global lookup table
        // that the <font="…"> tag consults.
        private static UnityEngine.TextCore.Text.FontAsset s_monoFontAsset;
        // Thin spaces inside the chip reserve real layout width for horizontal padding, so
        // the (behind-the-text) chip background no longer eats the inter-word gap.
        private const string ChipPad = "\u2009"; // U+2009 THIN SPACE

        public int Priority => 100;

        public bool CanRender(Inline inline) => inline is CodeInline;

        public void Render(Inline inline, StringBuilder builder, VisualElement parent,
            MarkdownRenderContext context, RenderInlineChildrenDelegate renderChildren)
        {
            var code = (CodeInline)inline;
            var content = code.Content;

            if (context.UseRichTextForInlines && builder != null)
            {
                // Emit the code inline directly into the rich-text string (code color, and
                // mono font when enabled) and record a span so the owning MarkdownTextElement
                // can draw a rounded chip behind it.
                if (EmitMonoFont)
                {
                    EnsureMonoFontRegistered();
                    builder.Append($"<font=\"{MonoFontName}\">");
                }
                builder.Append($"<color={CodeColor}>");
                int start = builder.Length;
                builder.Append(ChipPad);
                builder.Append(MarkdownRichText.EscapeAngles(content));
                builder.Append(ChipPad);
                int end = builder.Length;
                builder.Append("</color>");
                if (EmitMonoFont)
                    builder.Append("</font>");
                context.Spans.Add(new SpanDescriptor(MarkdownSpanKind.Code, start, end, content));
            }
            else
            {
                var codeWrapper = new VisualElement();
                codeWrapper.AddToClassList(context.Class("code-inline"));

                var label = new SelectableLabel(content);
                label.AddToClassList(context.Class("code-inline-text"));
                ApplyMonoFont(label);
                var te = label.Q<UnityEngine.UIElements.TextElement>();
                if (te != null) ApplyMonoFont(te);

                codeWrapper.Add(label);
                parent?.Add(codeWrapper);
            }
        }
        
        // Loads the RobotoMono TextCore FontAsset once. Retained for a future per-span mono
        // attempt; currently only referenced by the disabled EmitMonoFont path.
        internal static void EnsureMonoFontRegistered()
        {
            if (s_monoFontAsset == null)
            {
                s_monoFontAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>(
                    SidekickUiConstants.RobotoMonoFontAssetPath);
            }
        }

        public static void ApplyMonoFont(VisualElement element)
        {
            if (element == null)
                return;

            if (s_monoFont == null)
                s_monoFont = AssetDatabase.LoadAssetAtPath<Font>(FontTtfPath);
            
            if (s_monoFont != null)
                element.style.unityFont = s_monoFont;
        }
    }
}


