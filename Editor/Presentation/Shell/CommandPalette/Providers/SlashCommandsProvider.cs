// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Ryx.Sidekick.Editor.Domain.Commands;
using Ryx.Sidekick.Editor.UseCases.Commands;

namespace Ryx.Sidekick.Editor.Presentation.Shell.CommandPalette.Providers
{
    /// <summary>
    /// Provides slash commands discovered from the Claude CLI.
    /// </summary>
    internal sealed class SlashCommandsProvider : ICommandActionProvider
    {
        private readonly List<SlashCommand> _slashCommands = new();
        private readonly List<CommandAction> _actions = new();

        /// <summary>
        /// Callback when a slash command is executed (run immediately).
        /// Receives the full command text (e.g., "/compact").
        /// </summary>
        public Action<string> OnExecuteSlashCommand { get; set; }

        /// <summary>
        /// Callback when a slash command is inserted (Tab).
        /// Receives the insert text (e.g., "/compact ").
        /// </summary>
        public Action<string> OnInsertSlashCommand { get; set; }

        /// <summary>
        /// When true, non-builtin (skill) commands are displayed with a PRO badge and
        /// execute the paywall flow instead of sending to chat.
        /// </summary>
        public bool SkillsLocked { get; set; }

        /// <summary>
        /// Callback invoked when a locked skill action is activated.
        /// Typically opens the Pro paywall modal.
        /// </summary>
        public Action OnLockedSkillActivated { get; set; }

        public SlashCommandsProvider() { }

        public IEnumerable<CommandAction> GetActions() => _actions;

        public void Refresh()
        {
            RebuildActions();
        }

        /// <summary>
        /// Gets the current list of slash commands.
        /// </summary>
        public IReadOnlyList<SlashCommand> SlashCommands => _slashCommands;

        /// <summary>
        /// Updates the slash commands from CLI discovery.
        /// </summary>
        public void SetSlashCommands(IEnumerable<SlashCommand> commands)
        {
            _slashCommands.Clear();
            if (commands != null)
            {
                _slashCommands.AddRange(commands.Where(c => c != null && !string.IsNullOrEmpty(c.Name)));
            }
            RebuildActions();
        }

        /// <summary>
        /// Adds a single slash command.
        /// </summary>
        public void AddSlashCommand(SlashCommand command)
        {
            if (command == null || string.IsNullOrEmpty(command.Name)) return;

            // Remove existing with same name
            _slashCommands.RemoveAll(c => c.Name == command.Name);
            _slashCommands.Add(command);
            RebuildActions();
        }

        private void RebuildActions()
        {
            _actions.Clear();

            foreach (var cmd in _slashCommands.OrderBy(c => c.Name))
            {
                var isSkill = cmd.Origin != SlashCommandOrigin.Builtin;
                var section = isSkill ? CommandSections.Skills : CommandSections.SlashCommands;

                var insertText = cmd.AcceptsArguments ? $"{cmd.FullCommand} " : cmd.FullCommand;
                var description = !string.IsNullOrEmpty(cmd.ArgumentHint)
                    ? $"{cmd.Description} — {cmd.ArgumentHint}"
                    : cmd.Description;

                var locked = isSkill && SkillsLocked;
                var action = new CommandAction($"slash:{cmd.Name}", $"/{cmd.Name}", section)
                {
                    Description = description,
                    SupportsInsert = !locked,
                    InsertText = insertText,
                    IsProLocked = locked,
                    SearchKeywords = isSkill
                        ? new[] { cmd.Name, "slash", "command", "skill" }
                        : new[] { cmd.Name, "slash", "command" }
                };

                var capturedCmd = cmd;
                if (locked)
                {
                    action.OnExecute = () => OnLockedSkillActivated?.Invoke();
                }
                else
                {
                    action.OnExecute = () => OnExecuteSlashCommand?.Invoke(capturedCmd.FullCommand);
                }

                _actions.Add(action);
            }
        }
    }
}

