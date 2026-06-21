// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Domain.Commands
{
    /// <summary>
    /// Describes where a <see cref="SlashCommand"/> originated.
    /// </summary>
    internal enum SlashCommandOrigin
    {
        /// <summary>CLI built-in command shipped with the provider binary.</summary>
        Builtin,

        /// <summary>User-defined skill (description had a " (user)" suffix).</summary>
        User,

        /// <summary>Project-defined skill (description had a " (project)" suffix).</summary>
        Project,

        /// <summary>
        /// Plugin-provided command — either the name contains ":" (e.g. "codex:review")
        /// or the description began with a parenthesised plugin prefix (e.g. "(superpowers) ").
        /// </summary>
        Plugin,

        /// <summary>Dynamic workflow (description had a " (dynamic workflow)" suffix).</summary>
        Workflow,
    }
}
