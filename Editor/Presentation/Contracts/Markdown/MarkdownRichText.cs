// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Text;

namespace Ryx.Sidekick.Editor.Presentation.Contracts.Markdown
{
    /// <summary>
    /// Shared helpers for emitting rich-text into a Markdown block string.
    /// </summary>
    internal static class MarkdownRichText
    {
        // Transparent reserve placeholder for an asset link, sized to roughly match the
        // overlaid AssetLinkElement (icon + left/right padding + bold name). The leading pad
        // reserves icon + left chrome; the bold name matches the widget's bold name width;
        // the trailing pad reserves the widget's right chrome. Slightly generous so the
        // widget fits with a small gap rather than overlapping the next word. (Temporary —
        // a custom inline-block layout API will replace this.)
        private const string AssetLeadingPad = "\u2003\u2003"; // ~2 em ≈ icon + left padding
        private const string AssetTrailingPad = "\u2002";       // ~en ≈ right padding

        /// <summary>
        /// Appends a transparent, selectable placeholder for an asset link and records an
        /// <see cref="MarkdownSpanKind.AssetLink"/> span over it (raw offsets).
        /// </summary>
        public static void AppendAssetPlaceholder(StringBuilder builder, string fileName,
            List<SpanDescriptor> spans, string assetPath)
        {
            builder.Append("<color=#00000000>");
            int start = builder.Length;
            builder.Append(AssetLeadingPad);
            builder.Append("<b>");
            builder.Append(EscapeAngles(fileName));
            builder.Append("</b>");
            builder.Append(AssetTrailingPad);
            int end = builder.Length;
            builder.Append("</color>");
            spans.Add(new SpanDescriptor(MarkdownSpanKind.AssetLink, start, end, assetPath));
        }

        /// <summary>
        /// Escapes user-authored angle brackets so they are not parsed as rich-text tags.
        /// Inserts a zero-width space (U+200B) next to each bracket; the bracket still
        /// renders and counts as a displayed character (see <see cref="MarkdownDisplayedIndex"/>),
        /// but no longer opens/closes a tag. Mirrors the escaping in <c>LiteralInlineRenderer</c>.
        /// </summary>
        public static string EscapeAngles(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            return text
                .Replace("<", "<\u200B")
                .Replace(">", "\u200B>");
        }
    }
}
