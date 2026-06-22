// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor.Presentation.Contracts.Markdown
{
    /// <summary>
    /// Kind of an inline span that needs special treatment on top of the plain
    /// rich-text string of a Markdown block.
    /// </summary>
    internal enum MarkdownSpanKind
    {
        /// <summary>Inline code — a rounded chip background is drawn behind the text.</summary>
        Code,

        /// <summary>A detected asset path — an <c>AssetLinkElement</c> widget is overlaid on top.</summary>
        AssetLink,

        /// <summary>A markdown link / autolink (reserved for future click hit-testing).</summary>
        Link,
    }

    /// <summary>
    /// Describes one inline span inside a Markdown block's rich-text string.
    /// <para>
    /// <see cref="StartIndex"/>/<see cref="EndIndex"/> are RAW offsets into the built
    /// rich-text string (i.e. <c>StringBuilder.Length</c> at emit time, tag characters
    /// included). Consumers that need glyph geometry must first convert them to
    /// DISPLAYED indices via <see cref="MarkdownDisplayedIndex"/>, because UITK's
    /// <c>ITextSelection.GetCursorPositionFromStringIndex</c> indexes the visible text
    /// (rich-text tags stripped) — verified on Unity 6000.3.12f1.
    /// </para>
    /// </summary>
    internal readonly struct SpanDescriptor
    {
        public readonly MarkdownSpanKind Kind;

        /// <summary>Raw start offset into the rich-text string (inclusive).</summary>
        public readonly int StartIndex;

        /// <summary>Raw end offset into the rich-text string (exclusive).</summary>
        public readonly int EndIndex;

        /// <summary>Code text, asset path, or url depending on <see cref="Kind"/>.</summary>
        public readonly string Payload;

        public SpanDescriptor(MarkdownSpanKind kind, int startIndex, int endIndex, string payload)
        {
            Kind = kind;
            StartIndex = startIndex;
            EndIndex = endIndex;
            Payload = payload;
        }

        public override string ToString()
            => $"{Kind}[{StartIndex}..{EndIndex}] '{Payload}'";
    }
}
