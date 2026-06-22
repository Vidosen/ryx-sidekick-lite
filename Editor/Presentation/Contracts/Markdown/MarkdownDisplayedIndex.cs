// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor.Presentation.Contracts.Markdown
{
    /// <summary>
    /// Maps RAW string offsets (into a built rich-text string) to DISPLAYED character
    /// offsets (the visible text with rich-text tags stripped).
    /// <para>
    /// UITK's <c>ITextSelection.GetCursorPositionFromStringIndex(i)</c> indexes the
    /// VISIBLE text, not the raw string (verified on 6000.3.12f1). Span descriptors are
    /// recorded in raw offsets at emit time, so they must be converted before querying
    /// glyph geometry.
    /// </para>
    /// <para>
    /// Recognition is exact: the only rich-text tags ever present are the closed set this
    /// codebase emits (<c>&lt;b&gt; &lt;i&gt; &lt;s&gt; &lt;color=…&gt; &lt;font=…&gt;</c> and their
    /// closers). User-authored angle brackets are escaped as <c>&lt;​</c> / <c>​&gt;</c>
    /// upstream (see <c>LiteralInlineRenderer</c>), so a bare <c>&lt;</c> that starts one of these
    /// patterns is unambiguously a tag.
    /// </para>
    /// </summary>
    internal static class MarkdownDisplayedIndex
    {
        private static readonly string[] FixedTags =
        {
            "</color>", "</font>", "</b>", "</i>", "</s>", "<b>", "<i>", "<s>",
        };

        /// <summary>
        /// Builds a lookup of length <c>richText.Length + 1</c> where <c>map[raw]</c> is the
        /// displayed index at that raw offset. Raw offsets inside a tag collapse to the
        /// displayed index at the tag's start.
        /// </summary>
        public static int[] BuildMap(string richText)
        {
            if (string.IsNullOrEmpty(richText))
                return new[] { 0 };

            var map = new int[richText.Length + 1];
            int displayed = 0;
            int i = 0;
            while (i < richText.Length)
            {
                int tagLen = TagLengthAt(richText, i);
                if (tagLen > 0)
                {
                    for (int k = 0; k < tagLen; k++)
                        map[i + k] = displayed;
                    i += tagLen;
                }
                else
                {
                    map[i] = displayed;
                    displayed++;
                    i++;
                }
            }

            map[richText.Length] = displayed;
            return map;
        }

        /// <summary>Total number of displayed characters in <paramref name="richText"/>.</summary>
        public static int DisplayedLength(string richText)
        {
            var map = BuildMap(richText);
            return map[map.Length - 1];
        }

        /// <summary>
        /// Returns the length of the rich-text tag starting at <paramref name="i"/>, or 0 if
        /// no known tag starts there.
        /// </summary>
        private static int TagLengthAt(string s, int i)
        {
            if (s[i] != '<')
                return 0;

            foreach (var tag in FixedTags)
            {
                if (MatchAt(s, i, tag))
                    return tag.Length;
            }

            if (MatchAt(s, i, "<color=") || MatchAt(s, i, "<font="))
            {
                int gt = s.IndexOf('>', i);
                if (gt >= 0)
                    return gt - i + 1;
            }

            return 0;
        }

        private static bool MatchAt(string s, int i, string token)
        {
            if (i + token.Length > s.Length)
                return false;
            for (int k = 0; k < token.Length; k++)
            {
                if (s[i + k] != token[k])
                    return false;
            }
            return true;
        }
    }
}
