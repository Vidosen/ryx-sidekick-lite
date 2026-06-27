// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Ryx.Sidekick.Editor;
using Ryx.Sidekick.Editor.Domain.Commands;
using Ryx.Sidekick.Editor.UseCases.Commands;
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor.Presentation.Shell.CommandPalette.Providers
{
    /// <summary>
    /// Provides built-in actions for the command palette (context, model, settings, etc.).
    /// </summary>
    internal sealed class BuiltInActionsProvider : ICommandActionProvider
    {
        private readonly List<CommandAction> _actions = new();

        public Action OnAttachFile { get; set; }
        public Action OnAttachSelection { get; set; }
        public Action OnAttachSceneScreenshot { get; set; }
        public Action OnAttachGameScreenshot { get; set; }
        public Action OnOpenSettings { get; set; }
        public Action OnOpenHelp { get; set; }
        public Action OnNewChat { get; set; }
        public Action OnOpenInTerminal { get; set; }
        public Action<string> OnSelectModel { get; set; }
        public Func<string> GetCurrentModel { get; set; }
        public Func<IEnumerable<string>> GetAvailableModels { get; set; }

        public BuiltInActionsProvider()
        {
            BuildActions();
        }

        public IEnumerable<CommandAction> GetActions()
        {
            UnityEngine.Debug.Log($"[CommandPalette] BuiltInActionsProvider.GetActions() - returning {_actions.Count} actions");
            return _actions;
        }

        public void Refresh()
        {
            BuildActions();
        }

        private void BuildActions()
        {
            _actions.Clear();

            // Context section
            _actions.Add(new CommandAction("context:attach-file", "Attach File...", CommandSections.Context)
            {
                Description = "Add a file from the project as context",
                TrailingVisual = "tool-folder",
                OnExecute = () => OnAttachFile?.Invoke(),
                SearchKeywords = new[] { "file", "add", "context", "attachment" }
            });

            _actions.Add(new CommandAction("context:attach-selection", "Attach Selection", CommandSections.Context)
            {
                Description = "Add current selection (GameObject or asset) as context",
                TrailingVisual = "cmd-selection",
                OnExecute = () => OnAttachSelection?.Invoke(),
                SearchKeywords = new[] { "selection", "gameobject", "asset", "add", "context" }
            });

            // Screenshot slash commands (use "slash:" prefix so they're treated like slash commands)
            _actions.Add(new CommandAction("slash:screenshot-scene", "/screenshot-scene", CommandSections.SlashCommands)
            {
                Description = "Capture Scene View screenshot as context",
                TrailingVisual = "cmd-screenshot",
                SupportsInsert = true,
                InsertText = "/screenshot-scene",
                OnExecute = () => OnAttachSceneScreenshot?.Invoke(),
                SearchKeywords = new[] { "screenshot", "scene", "view", "capture", "image" }
            });

            _actions.Add(new CommandAction("slash:screenshot-game", "/screenshot-game", CommandSections.SlashCommands)
            {
                Description = "Capture Game View screenshot as context",
                TrailingVisual = "cmd-gameview",
                SupportsInsert = true,
                InsertText = "/screenshot-game",
                OnExecute = () => OnAttachGameScreenshot?.Invoke(),
                SearchKeywords = new[] { "screenshot", "game", "view", "capture", "image" }
            });

            // Model section — dynamic from active provider
            var currentModel = GetCurrentModel?.Invoke() ?? "";
            var presets = GetAvailableModels?.Invoke()
                ?? SidekickSettings.instance.ActiveProvider?.ModelPresets
                ?? System.Array.Empty<string>();

            foreach (var preset in presets)
            {
                var localPreset = preset;
                _actions.Add(new CommandAction($"model:{localPreset}", localPreset, CommandSections.Model)
                {
                    TrailingVisual = currentModel == localPreset ? "cmd-check" : null,
                    OnExecute = () => OnSelectModel?.Invoke(localPreset),
                    SearchKeywords = new[] { "model", localPreset }
                });
            }

            // Settings section
            _actions.Add(new CommandAction("settings:open", "Open Settings", CommandSections.Settings)
            {
                Description = "Open Ryx Sidekick project settings",
                TrailingVisual = "cmd-settings",
                OnExecute = () => OnOpenSettings?.Invoke(),
                SearchKeywords = new[] { "settings", "preferences", "config", "configuration" }
            });

            _actions.Add(new CommandAction("settings:new-chat", "New Chat", CommandSections.Settings)
            {
                Description = "Start a new conversation",
                TrailingVisual = "cmd-chat",
                OnExecute = () => OnNewChat?.Invoke(),
                SearchKeywords = new[] { "new", "chat", "conversation", "clear" }
            });

            _actions.Add(new CommandAction("settings:open-in-terminal", "Open in Terminal", CommandSections.Settings)
            {
                Description = "Launch the active CLI in a terminal at the project root",
                TrailingVisual = "tool-bash",
                OnExecute = () => OnOpenInTerminal?.Invoke(),
                SearchKeywords = new[] { "terminal", "open", "cli", "shell", "console", "interactive" }
            });

            // Feedback section
            _actions.Add(new CommandAction("feedback:help", "Help & Documentation", CommandSections.Feedback)
            {
                Description = "Open documentation",
                TrailingVisual = "cmd-help",
                OnExecute = () => OnOpenHelp?.Invoke(),
                SearchKeywords = new[] { "help", "docs", "documentation", "guide" }
            });
        }
    }
}
