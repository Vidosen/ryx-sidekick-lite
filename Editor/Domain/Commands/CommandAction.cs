// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor.Domain.Commands
{
    /// <summary>
    /// Predefined section keys for command palette grouping.
    /// Lower order = shown first.
    /// </summary>
    internal static class CommandSections
    {
        public const string Context = "Context";
        public const string Model = "Model";
        public const string SlashCommands = "Slash Commands";
        public const string Skills = "Skills";
        public const string McpPrompts = "MCP Prompts";
        public const string Settings = "Settings";
        public const string Feedback = "Feedback";

        public static int GetOrder(string section) => section switch
        {
            Context => 0,
            Model => 1,
            SlashCommands => 2,
            Skills => 3,
            McpPrompts => 4,
            Settings => 5,
            Feedback => 6,
            _ => 100
        };
    }

    /// <summary>
    /// Represents a single action in the command palette.
    /// </summary>
    internal sealed class CommandAction
    {
        /// <summary>
        /// Unique identifier for this action (e.g., "slash:compact", "model:sonnet", "context:attach-file").
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Display label shown in the palette.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Optional description shown as secondary text.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Section this action belongs to.
        /// </summary>
        public string Section { get; }

        /// <summary>
        /// Optional trailing visual (icon/badge).
        /// </summary>
        public string TrailingVisual { get; set; }

        /// <summary>
        /// If true, selecting this action keeps the palette open (e.g., model selection).
        /// </summary>
        public bool KeepMenuOpen { get; set; }

        /// <summary>
        /// If true, this action supports Tab-to-insert (for slash commands).
        /// </summary>
        public bool SupportsInsert { get; set; }

        /// <summary>
        /// Text to insert when user presses Tab (for slash commands).
        /// </summary>
        public string InsertText { get; set; }

        /// <summary>
        /// Callback invoked when user executes this action (Enter/click).
        /// </summary>
        public Action OnExecute { get; set; }

        /// <summary>
        /// If true, this action is gated behind the Pro package and will show a PRO badge.
        /// Clicking/Enter opens the paywall instead of executing the command.
        /// </summary>
        public bool IsProLocked { get; set; }

        /// <summary>
        /// Keywords for fuzzy search (in addition to Label).
        /// </summary>
        public string[] SearchKeywords { get; set; }

        public CommandAction(string id, string label, string section)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Section = section ?? CommandSections.Settings;
        }

        /// <summary>
        /// Checks if query matches this action (case-insensitive).
        /// </summary>
        public bool MatchesQuery(string query)
        {
            if (string.IsNullOrEmpty(query)) return true;

            var q = query.ToLowerInvariant();

            // Match label
            if (Label.ToLowerInvariant().Contains(q)) return true;

            // Match description
            if (!string.IsNullOrEmpty(Description) && Description.ToLowerInvariant().Contains(q)) return true;

            // Match keywords
            if (SearchKeywords != null)
            {
                foreach (var kw in SearchKeywords)
                {
                    if (!string.IsNullOrEmpty(kw) && kw.ToLowerInvariant().Contains(q)) return true;
                }
            }

            return false;
        }
    }
}

