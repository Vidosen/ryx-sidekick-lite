// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor.Domain.Commands
{
    /// <summary>
    /// Represents a slash command discovered from the CLI.
    /// </summary>
    [Serializable]
    internal sealed class SlashCommand
    {
        /// <summary>
        /// Command name without the leading slash (e.g., "compact", "context").
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Human-readable description of what the command does.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Whether this command accepts arguments after the name.
        /// </summary>
        public bool AcceptsArguments { get; set; }

        /// <summary>
        /// Hint text describing the expected argument (e.g., "[file]", "[query]").
        /// Empty when the command takes no arguments.
        /// </summary>
        public string ArgumentHint { get; set; }

        /// <summary>
        /// Override for the command text used in completions. When null or empty,
        /// <see cref="FullCommand"/> returns <c>"/{Name}"</c> as usual.
        /// Populated for provider-specific commands (e.g., Codex skills) that have
        /// a distinct invocation form.
        /// </summary>
        public string CommandText { get; set; }

        /// <summary>
        /// Where this command originated (built-in CLI, user skill, project skill, plugin, workflow).
        /// </summary>
        public SlashCommandOrigin Origin { get; set; } = SlashCommandOrigin.Builtin;

        /// <summary>
        /// Gets the full command text including leading slash.
        /// When <see cref="CommandText"/> is set it is returned as-is; otherwise <c>"/{Name}"</c>.
        /// </summary>
        public string FullCommand => !string.IsNullOrEmpty(CommandText) ? CommandText : $"/{Name}";

        public SlashCommand() { }

        public SlashCommand(string name, string description = null, bool acceptsArguments = false)
        {
            Name = name;
            Description = description;
            AcceptsArguments = acceptsArguments;
        }
    }
}

