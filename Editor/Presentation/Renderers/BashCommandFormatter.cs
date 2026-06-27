// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    /// <summary>
    /// Formats terminal/Bash command lines for display: splits a command into a dimmed
    /// boilerplate <see cref="CommandRole.Prefix"/> (process wrappers / working-directory
    /// flags) and the meaningful <see cref="CommandRole.Action"/>, computes a middle-ellipsis
    /// for collapsed headers, and counts output lines.
    /// </summary>
    /// <remarks>
    /// Segmentation is intentionally role-based so it can later be extended from the current
    /// dim-prefix + single-color-action scheme to full per-token syntax coloring (flags,
    /// arguments, strings, …) by emitting finer roles — callers render one element per segment.
    /// </remarks>
    internal static class BashCommandFormatter
    {
        internal enum CommandRole
        {
            Prefix,
            Action,
        }

        internal readonly struct CommandSegment
        {
            public readonly string Text;
            public readonly CommandRole Role;

            public CommandSegment(string text, CommandRole role)
            {
                Text = text;
                Role = role;
            }
        }

        // Boilerplate prefixes that should be dimmed, tried in order. Group 1 = prefix, group 2 = action.
        private static readonly Regex[] PrefixPatterns =
        {
            // $(command -v foo) [-C /path]…  — RTK / shell-resolution wrapper used across this repo
            new Regex(@"^\s*(\$\(\s*command\s+-v\s+[^)]*\)(?:\s+-C\s+\S+)*)\s+(.+)$", RegexOptions.Compiled | RegexOptions.Singleline),
            // <program> -C /path …  — e.g. git -C /repo push…
            new Regex(@"^\s*(\S+\s+-C\s+\S+)\s+(.+)$", RegexOptions.Compiled | RegexOptions.Singleline),
            // cd /path && …
            new Regex(@"^\s*(cd\s+\S+\s+&&)\s+(.+)$", RegexOptions.Compiled | RegexOptions.Singleline),
            // leading env assignments: FOO=bar BAZ=qux …
            new Regex(@"^\s*((?:[A-Za-z_][A-Za-z0-9_]*=\S+\s+)+)(.+)$", RegexOptions.Compiled | RegexOptions.Singleline),
        };

        /// <summary>
        /// Splits a command into ordered display segments. Returns a single Action segment when
        /// no boilerplate prefix is recognized; the Action segment of a split keeps a leading
        /// space so segments render with their original spacing when laid out side by side.
        /// </summary>
        internal static IReadOnlyList<CommandSegment> Segment(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                return System.Array.Empty<CommandSegment>();
            }

            foreach (var pattern in PrefixPatterns)
            {
                var match = pattern.Match(command);
                if (match.Success)
                {
                    var prefix = match.Groups[1].Value.TrimEnd();
                    var action = match.Groups[2].Value;
                    return new[]
                    {
                        new CommandSegment(prefix, CommandRole.Prefix),
                        new CommandSegment(" " + action, CommandRole.Action),
                    };
                }
            }

            return new[] { new CommandSegment(command, CommandRole.Action) };
        }

        /// <summary>
        /// Shortens a string with a middle ellipsis (head…tail), keeping both ends visible.
        /// </summary>
        internal static string MiddleEllipsis(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || maxLength <= 1 || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            var budget = maxLength - 1; // room for the ellipsis
            var head = (budget + 1) / 2;
            var tail = budget - head;
            return value.Substring(0, head) + "…" + value.Substring(value.Length - tail);
        }

        /// <summary>
        /// Counts the lines of command output, ignoring a single trailing newline.
        /// </summary>
        internal static int CountLines(string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                return 0;
            }

            var normalized = output.Replace("\r\n", "\n").Replace('\r', '\n');
            if (normalized.EndsWith("\n"))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            if (normalized.Length == 0)
            {
                return 0;
            }

            var count = 1;
            foreach (var ch in normalized)
            {
                if (ch == '\n')
                {
                    count++;
                }
            }

            return count;
        }
    }
}
