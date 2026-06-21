// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Ryx.Sidekick.Editor.Domain.Parsing
{
    internal static class CommandTagParser
    {
        private static readonly Regex CommandNameRegex = new(
            @"<command-name>(?<name>.*?)</command-name>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex CommandMessageRegex = new(
            @"<command-message>(?<message>.*?)</command-message>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex CommandTagRegex = new(
            @"<command-name>.*?</command-name>|<command-message>.*?</command-message>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // Local command tags used by CLI for slash command output
        private static readonly Regex LocalCommandStdoutRegex = new(
            @"<local-command-stdout>(?<content>.*?)</local-command-stdout>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex LocalCommandCaveatRegex = new(
            @"<local-command-caveat>.*?</local-command-caveat>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex CommandArgsRegex = new(
            @"<command-args>.*?</command-args>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// Extracts content from local command stdout tags.
        /// Used by both streaming and history loading paths.
        /// </summary>
        public static string ExtractLocalCommandOutput(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Extract content from <local-command-stdout> tags
            var stdoutMatch = LocalCommandStdoutRegex.Match(text);
            if (stdoutMatch.Success)
            {
                return stdoutMatch.Groups["content"].Value.Trim();
            }

            // Remove caveat tags entirely (system instructions to AI)
            text = LocalCommandCaveatRegex.Replace(text, "");
            
            // Remove command-args tags (metadata)
            text = CommandArgsRegex.Replace(text, "");

            return text.Trim();
        }

        /// <summary>
        /// Cleans all local command XML tags from content.
        /// Returns true if any tags were found and cleaned.
        /// </summary>
        public static bool TryCleanLocalCommandTags(string content, out string cleaned)
        {
            cleaned = content;
            if (string.IsNullOrEmpty(content)) return false;

            var hasLocalTags = LocalCommandStdoutRegex.IsMatch(content) ||
                               LocalCommandCaveatRegex.IsMatch(content) ||
                               CommandArgsRegex.IsMatch(content);

            if (!hasLocalTags) return false;

            cleaned = ExtractLocalCommandOutput(content);
            return true;
        }

        public static bool TryFormatCommandText(string content, out string formatted)
        {
            formatted = content;
            if (string.IsNullOrEmpty(content)) return false;

            var nameMatch = CommandNameRegex.Match(content);
            var messageMatch = CommandMessageRegex.Match(content);

            if (!nameMatch.Success && !messageMatch.Success) return false;

            var commandName = nameMatch.Success ? nameMatch.Groups["name"].Value.Trim() : "";
            var commandMessage = messageMatch.Success ? messageMatch.Groups["message"].Value.Trim() : "";
            var remainder = CommandTagRegex.Replace(content, "").Trim();

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(commandName))
            {
                parts.Add(commandName);
            }

            if (!string.IsNullOrEmpty(commandMessage) && !IsRedundant(commandName, commandMessage))
            {
                parts.Add(commandMessage);
            }

            if (!string.IsNullOrEmpty(remainder) && !IsRedundant(string.Join(" ", parts), remainder))
            {
                parts.Add(remainder);
            }

            formatted = parts.Count > 0 ? string.Join(" ", parts) : remainder;
            return true;
        }

        private static bool IsRedundant(string existing, string candidate)
        {
            if (string.IsNullOrEmpty(existing) || string.IsNullOrEmpty(candidate)) return false;

            if (string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (existing.StartsWith("/", StringComparison.Ordinal))
            {
                var trimmed = existing.Substring(1);
                if (string.Equals(trimmed, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
