// SPDX-License-Identifier: GPL-3.0-only
using System.Text.RegularExpressions;

namespace Ryx.Sidekick.Editor.Domain.Parsing
{
    /// <summary>
    /// Result of @ mention token detection in input text.
    /// </summary>
    internal readonly struct AtDetectionResult
    {
        /// <summary>
        /// Whether an @ mention token is detected at the caret position.
        /// </summary>
        public readonly bool IsActive;

        /// <summary>
        /// The query string after the @ (e.g., "Player" for "@Player").
        /// Empty string if just "@" was typed.
        /// </summary>
        public readonly string Query;

        /// <summary>
        /// Start index of the @ token in the text.
        /// </summary>
        public readonly int TokenStart;

        /// <summary>
        /// End index of the @ token (exclusive).
        /// </summary>
        public readonly int TokenEnd;

        public AtDetectionResult(bool isActive, string query, int tokenStart, int tokenEnd)
        {
            IsActive = isActive;
            Query = query ?? "";
            TokenStart = tokenStart;
            TokenEnd = tokenEnd;
        }

        public static AtDetectionResult None => new(false, "", -1, -1);
    }

    /// <summary>
    /// Detects @ mention tokens in input text following VS Code semantics.
    /// An @ token is "@" followed by non-whitespace characters, triggered at start of line or after whitespace.
    /// </summary>
    internal static class AtDetector
    {
        // Pattern: start of string or whitespace, followed by "@" and non-whitespace/non-@ chars
        private static readonly Regex AtTokenRegex = new(
            @"(?:^|(?<=\s))@([^\s@]*)",
            RegexOptions.Compiled
        );

        /// <summary>
        /// Detects if the caret is inside an @ mention token.
        /// </summary>
        /// <param name="text">The full input text.</param>
        /// <param name="caretIndex">Current caret position (0-based).</param>
        /// <returns>Detection result with query and token bounds if active.</returns>
        public static AtDetectionResult Detect(string text, int caretIndex)
        {
            if (string.IsNullOrEmpty(text) || caretIndex < 0)
                return AtDetectionResult.None;

            // Clamp caret to valid range
            if (caretIndex > text.Length) caretIndex = text.Length;

            // Find all @ tokens
            var matches = AtTokenRegex.Matches(text);

            foreach (Match match in matches)
            {
                // The full match includes the space/start, but we want the "@" position
                var atIndex = match.Index;
                if (match.Value.Length > 0 && match.Value[0] != '@')
                {
                    // Match started with whitespace, so "@" is at index+1
                    atIndex = match.Index + 1;
                }

                var tokenStart = atIndex;
                var tokenEnd = match.Index + match.Length;

                // Check if caret is within this token (after @, inclusive of end for typing)
                if (caretIndex > tokenStart && caretIndex <= tokenEnd)
                {
                    var query = match.Groups[1].Value;
                    return new AtDetectionResult(true, query, tokenStart, tokenEnd);
                }
            }

            return AtDetectionResult.None;
        }

        /// <summary>
        /// Checks if user just typed "@" at a valid position (start or after space).
        /// </summary>
        public static bool ShouldOpenMentions(string text, int caretIndex)
        {
            if (string.IsNullOrEmpty(text) || caretIndex <= 0) return false;
            if (caretIndex > text.Length) return false;

            // Character just typed is at caretIndex - 1
            var justTyped = text[caretIndex - 1];
            if (justTyped != '@') return false;

            // Check if at start or after whitespace
            if (caretIndex == 1) return true; // "@" at start
            var charBefore = text[caretIndex - 2];
            return char.IsWhiteSpace(charBefore);
        }
    }

    /// <summary>
    /// Utility for transforming input text based on @ mention selection.
    /// </summary>
    internal static class MentionTextTransform
    {
        /// <summary>
        /// Removes the @ token from text and returns the new text with updated caret position.
        /// </summary>
        /// <param name="text">Original text.</param>
        /// <param name="detection">The active detection result.</param>
        /// <returns>Tuple of (new text, new caret position).</returns>
        public static (string Text, int CaretIndex) RemoveToken(string text, AtDetectionResult detection)
        {
            if (!detection.IsActive || string.IsNullOrEmpty(text))
                return (text, 0);

            var before = text.Substring(0, detection.TokenStart);
            var after = detection.TokenEnd < text.Length ? text.Substring(detection.TokenEnd) : "";

            return (before + after, detection.TokenStart);
        }

        /// <summary>
        /// Replaces the @ token with the given asset path mention (for selection).
        /// </summary>
        /// <param name="text">Original text.</param>
        /// <param name="detection">The active detection result.</param>
        /// <param name="assetPath">Asset path to insert (e.g., "Assets/Scripts/Player.cs").</param>
        /// <returns>Tuple of (new text, new caret position).</returns>
        public static (string Text, int CaretIndex) ReplaceToken(string text, AtDetectionResult detection, string assetPath)
        {
            var mention = $"@{assetPath} ";
            
            if (!detection.IsActive || string.IsNullOrEmpty(text))
                return ((text ?? "") + mention, (text?.Length ?? 0) + mention.Length);

            var before = text.Substring(0, detection.TokenStart);
            var after = detection.TokenEnd < text.Length ? text.Substring(detection.TokenEnd) : "";

            var newText = before + mention + after;
            var newCaret = detection.TokenStart + mention.Length;

            return (newText, newCaret);
        }

        /// <summary>
        /// Inserts an asset mention at the current caret position.
        /// Used for drag-n-drop, browse, and selection attachment flows.
        /// </summary>
        /// <param name="text">Original text.</param>
        /// <param name="caretIndex">Current caret position.</param>
        /// <param name="assetPath">Asset path to insert.</param>
        /// <returns>Tuple of (new text, new caret position).</returns>
        public static (string Text, int CaretIndex) InsertMention(string text, int caretIndex, string assetPath)
        {
            text ??= "";
            var mention = $"@{assetPath}";

            if (caretIndex < 0) caretIndex = 0;
            if (caretIndex > text.Length) caretIndex = text.Length;

            var before = text.Substring(0, caretIndex);
            var after = text.Substring(caretIndex);

            // Add space before if needed (not at start and previous char isn't whitespace)
            var needsSpaceBefore = before.Length > 0 && !char.IsWhiteSpace(before[before.Length - 1]);
            var prefix = needsSpaceBefore ? " " : "";

            // Add space after if needed (not at end and next char isn't whitespace)
            var needsSpaceAfter = after.Length > 0 && !char.IsWhiteSpace(after[0]);
            var suffix = needsSpaceAfter ? " " : "";

            var newText = before + prefix + mention + suffix + after;
            var newCaret = caretIndex + prefix.Length + mention.Length + suffix.Length;

            return (newText, newCaret);
        }

        /// <summary>
        /// Inserts multiple asset mentions at the current caret position.
        /// Used when multiple files are dropped at once.
        /// </summary>
        /// <param name="text">Original text.</param>
        /// <param name="caretIndex">Current caret position.</param>
        /// <param name="assetPaths">Asset paths to insert.</param>
        /// <returns>Tuple of (new text, new caret position).</returns>
        public static (string Text, int CaretIndex) InsertMultipleMentions(string text, int caretIndex, params string[] assetPaths)
        {
            if (assetPaths == null || assetPaths.Length == 0)
                return (text ?? "", caretIndex);

            var currentText = text ?? "";
            var currentCaret = caretIndex;

            foreach (var path in assetPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                (currentText, currentCaret) = InsertMention(currentText, currentCaret, path);
            }

            return (currentText, currentCaret);
        }
    }
}
