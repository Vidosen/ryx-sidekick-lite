// SPDX-License-Identifier: GPL-3.0-only
using System.Text.RegularExpressions;

namespace Ryx.Sidekick.Editor.Domain.Parsing
{
    /// <summary>
    /// Result of slash token detection in input text.
    /// </summary>
    internal readonly struct SlashDetectionResult
    {
        /// <summary>
        /// Whether a slash token is detected at the caret position.
        /// </summary>
        public readonly bool IsActive;

        /// <summary>
        /// The query string after the slash (e.g., "comp" for "/comp").
        /// Empty string if just "/" was typed.
        /// </summary>
        public readonly string Query;

        /// <summary>
        /// Start index of the slash token in the text.
        /// </summary>
        public readonly int TokenStart;

        /// <summary>
        /// End index of the slash token (exclusive).
        /// </summary>
        public readonly int TokenEnd;

        public SlashDetectionResult(bool isActive, string query, int tokenStart, int tokenEnd)
        {
            IsActive = isActive;
            Query = query ?? "";
            TokenStart = tokenStart;
            TokenEnd = tokenEnd;
        }

        public static SlashDetectionResult None => new(false, "", -1, -1);
    }

    /// <summary>
    /// Detects slash command tokens in input text following VS Code semantics.
    /// A slash token is "/" followed by non-whitespace characters, triggered at start of line or after whitespace.
    /// </summary>
    internal static class SlashDetector
    {
        // VS Code pattern: (?:^|\s)\/[^\s/]*
        // Matches: start of string or whitespace, followed by "/" and non-whitespace/non-slash chars
        private static readonly Regex SlashTokenRegex = new(
            @"(?:^|(?<=\s))\/([^\s/]*)",
            RegexOptions.Compiled
        );

        /// <summary>
        /// Detects if the caret is inside a slash token.
        /// </summary>
        /// <param name="text">The full input text.</param>
        /// <param name="caretIndex">Current caret position (0-based).</param>
        /// <returns>Detection result with query and token bounds if active.</returns>
        public static SlashDetectionResult Detect(string text, int caretIndex)
        {
            if (string.IsNullOrEmpty(text) || caretIndex < 0)
                return SlashDetectionResult.None;

            // Clamp caret to valid range
            if (caretIndex > text.Length) caretIndex = text.Length;

            // Find all slash tokens
            var matches = SlashTokenRegex.Matches(text);

            foreach (Match match in matches)
            {
                // The full match includes the space/start, but we want the "/" position
                var slashIndex = match.Index;
                if (match.Value.Length > 0 && match.Value[0] != '/')
                {
                    // Match started with whitespace, so "/" is at index+1
                    slashIndex = match.Index + 1;
                }

                var tokenStart = slashIndex;
                var tokenEnd = match.Index + match.Length;

                // Check if caret is within this token (after slash, inclusive of end for typing)
                if (caretIndex > tokenStart && caretIndex <= tokenEnd)
                {
                    var query = match.Groups[1].Value;
                    return new SlashDetectionResult(true, query, tokenStart, tokenEnd);
                }
            }

            return SlashDetectionResult.None;
        }

        /// <summary>
        /// Checks if user just typed "/" at a valid position (start or after space).
        /// </summary>
        public static bool ShouldOpenPalette(string text, int caretIndex)
        {
            if (string.IsNullOrEmpty(text) || caretIndex <= 0) return false;
            if (caretIndex > text.Length) return false;

            // Character just typed is at caretIndex - 1
            var justTyped = text[caretIndex - 1];
            if (justTyped != '/') return false;

            // Check if at start or after whitespace
            if (caretIndex == 1) return true; // "/" at start
            var charBefore = text[caretIndex - 2];
            return char.IsWhiteSpace(charBefore);
        }
    }

    /// <summary>
    /// Utility for transforming input text based on slash command selection.
    /// </summary>
    internal static class SlashTextTransform
    {
        /// <summary>
        /// Removes the slash token from text and returns the new text with updated caret position.
        /// </summary>
        /// <param name="text">Original text.</param>
        /// <param name="detection">The active detection result.</param>
        /// <returns>Tuple of (new text, new caret position).</returns>
        public static (string Text, int CaretIndex) RemoveToken(string text, SlashDetectionResult detection)
        {
            if (!detection.IsActive || string.IsNullOrEmpty(text))
                return (text, 0);

            var before = text.Substring(0, detection.TokenStart);
            var after = detection.TokenEnd < text.Length ? text.Substring(detection.TokenEnd) : "";

            return (before + after, detection.TokenStart);
        }

        /// <summary>
        /// Replaces the slash token with the given text (for Tab-insert).
        /// </summary>
        /// <param name="text">Original text.</param>
        /// <param name="detection">The active detection result.</param>
        /// <param name="replacement">Text to insert (e.g., "/compact ").</param>
        /// <returns>Tuple of (new text, new caret position).</returns>
        public static (string Text, int CaretIndex) ReplaceToken(string text, SlashDetectionResult detection, string replacement)
        {
            if (!detection.IsActive || string.IsNullOrEmpty(text))
                return ((text ?? "") + replacement, (text?.Length ?? 0) + (replacement?.Length ?? 0));

            var before = text.Substring(0, detection.TokenStart);
            var after = detection.TokenEnd < text.Length ? text.Substring(detection.TokenEnd) : "";

            var newText = before + (replacement ?? "") + after;
            var newCaret = detection.TokenStart + (replacement?.Length ?? 0);

            return (newText, newCaret);
        }

        /// <summary>
        /// Inserts a slash command at the current position (when palette is opened manually).
        /// </summary>
        /// <param name="text">Original text.</param>
        /// <param name="caretIndex">Current caret position.</param>
        /// <param name="command">Command text to insert (e.g., "/compact ").</param>
        /// <returns>Tuple of (new text, new caret position).</returns>
        public static (string Text, int CaretIndex) InsertCommand(string text, int caretIndex, string command)
        {
            text ??= "";
            command ??= "";

            if (caretIndex < 0) caretIndex = 0;
            if (caretIndex > text.Length) caretIndex = text.Length;

            var before = text.Substring(0, caretIndex);
            var after = text.Substring(caretIndex);

            // Add space before if needed
            var needsSpaceBefore = before.Length > 0 && !char.IsWhiteSpace(before[before.Length - 1]);
            var prefix = needsSpaceBefore ? " " : "";

            var newText = before + prefix + command + after;
            var newCaret = caretIndex + prefix.Length + command.Length;

            return (newText, newCaret);
        }
    }
}

