// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Domain.Commands;
using Ryx.Sidekick.Editor.UseCases.Commands;

namespace Ryx.Sidekick.Editor.Presentation.Shell.CommandPalette.Providers
{
    /// <summary>
    /// Stub provider for MCP prompts.
    /// Phase 1: Architecture only - no actual MCP prompts/list implementation.
    /// When MCP client is implemented, this provider will surface prompts as actions
    /// with "run/send" behavior (not insert).
    /// </summary>
    internal sealed class McpPromptsProvider : ICommandActionProvider
    {
        private readonly List<CommandAction> _actions = new();

        /// <summary>
        /// Callback when an MCP prompt is executed.
        /// Receives the prompt name/id.
        /// </summary>
        public Action<string> OnExecutePrompt { get; set; }

        public McpPromptsProvider() { }

        public IEnumerable<CommandAction> GetActions() => _actions;

        public void Refresh()
        {
            // Phase 1: No-op
            // When MCP client is implemented:
            // - Call prompts/list on connected MCP servers
            // - Populate _actions with MCP prompts
        }

        /// <summary>
        /// Sets MCP prompts when they become available.
        /// </summary>
        /// <param name="prompts">List of (name, description) tuples.</param>
        public void SetPrompts(IEnumerable<(string Name, string Description)> prompts)
        {
            _actions.Clear();

            if (prompts == null) return;

            foreach (var (name, description) in prompts)
            {
                if (string.IsNullOrEmpty(name)) continue;

                var action = new CommandAction($"mcp-prompt:{name}", name, CommandSections.McpPrompts)
                {
                    Description = description,
                    TrailingVisual = "cmd-plug",
                    SupportsInsert = false, // MCP prompts run/send immediately
                    SearchKeywords = new[] { name, "mcp", "prompt" }
                };

                var capturedName = name;
                action.OnExecute = () => OnExecutePrompt?.Invoke(capturedName);

                _actions.Add(action);
            }
        }

        /// <summary>
        /// Clears all MCP prompts (e.g., when MCP connection is lost).
        /// </summary>
        public void ClearPrompts()
        {
            _actions.Clear();
        }
    }
}
