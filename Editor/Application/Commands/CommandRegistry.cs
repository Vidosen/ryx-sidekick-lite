// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Ryx.Sidekick.Editor.Domain.Commands;

namespace Ryx.Sidekick.Editor.UseCases.Commands
{
    /// <summary>
    /// Interface for providers that supply command actions to the palette.
    /// </summary>
    internal interface ICommandActionProvider
    {
        /// <summary>
        /// Returns the actions this provider supplies.
        /// </summary>
        IEnumerable<CommandAction> GetActions();

        /// <summary>
        /// Called when the provider should refresh its actions (e.g., after CLI init).
        /// </summary>
        void Refresh();
    }

    /// <summary>
    /// Central registry for command palette actions.
    /// Aggregates actions from multiple providers and supports filtering/ordering.
    /// </summary>
    internal sealed class CommandRegistry
    {
        private readonly List<ICommandActionProvider> _providers = new();
        private readonly Dictionary<string, CommandAction> _actionsById = new();
        private List<CommandAction> _cachedActions;
        private bool _isDirty = true;

        /// <summary>
        /// Event fired when registry contents change.
        /// </summary>
        public event Action OnRegistryChanged;

        /// <summary>
        /// Registers a provider and marks registry as dirty.
        /// </summary>
        public void RegisterProvider(ICommandActionProvider provider)
        {
            if (provider == null) return;
            if (_providers.Contains(provider)) return;

            _providers.Add(provider);
            _isDirty = true;
            OnRegistryChanged?.Invoke();
        }

        /// <summary>
        /// Unregisters a provider.
        /// </summary>
        public void UnregisterProvider(ICommandActionProvider provider)
        {
            if (provider == null) return;
            if (_providers.Remove(provider))
            {
                _isDirty = true;
                OnRegistryChanged?.Invoke();
            }
        }

        /// <summary>
        /// Marks the registry as dirty, forcing a rebuild on next access.
        /// </summary>
        public void Invalidate()
        {
            _isDirty = true;
            OnRegistryChanged?.Invoke();
        }

        /// <summary>
        /// Refreshes all providers and rebuilds action cache.
        /// </summary>
        public void RefreshAll()
        {
            foreach (var provider in _providers)
            {
                provider.Refresh();
            }
            _isDirty = true;
            OnRegistryChanged?.Invoke();
        }

        /// <summary>
        /// Gets all actions, optionally filtered by query.
        /// Actions are grouped by section and ordered.
        /// </summary>
        public IReadOnlyList<CommandAction> GetActions(string query = null)
        {
            EnsureCache();

            if (string.IsNullOrEmpty(query))
                return _cachedActions;

            return _cachedActions.Where(a => a.MatchesQuery(query)).ToList();
        }

        /// <summary>
        /// Gets actions grouped by section, optionally filtered.
        /// </summary>
        public IReadOnlyList<(string Section, IReadOnlyList<CommandAction> Actions)> GetGroupedActions(string query = null)
        {
            var filtered = GetActions(query);

            return filtered
                .GroupBy(a => a.Section)
                .OrderBy(g => CommandSections.GetOrder(g.Key))
                .ThenBy(g => g.Key)
                .Select(g => (g.Key, (IReadOnlyList<CommandAction>)g.ToList()))
                .ToList();
        }

        /// <summary>
        /// Gets action by ID.
        /// </summary>
        public CommandAction GetActionById(string id)
        {
            EnsureCache();
            return _actionsById.TryGetValue(id, out var action) ? action : null;
        }

        /// <summary>
        /// Executes an action by ID.
        /// </summary>
        public bool ExecuteAction(string id)
        {
            var action = GetActionById(id);
            if (action?.OnExecute == null) return false;

            action.OnExecute();
            return true;
        }

        private void EnsureCache()
        {
            if (!_isDirty && _cachedActions != null) return;

            _actionsById.Clear();
            var all = new List<CommandAction>();

            foreach (var provider in _providers)
            {
                var actions = provider.GetActions();
                if (actions == null) continue;

                foreach (var action in actions)
                {
                    if (action == null) continue;
                    if (_actionsById.ContainsKey(action.Id)) continue; // skip duplicates

                    _actionsById[action.Id] = action;
                    all.Add(action);
                }
            }

            // Order by section, then by label
            _cachedActions = all
                .OrderBy(a => CommandSections.GetOrder(a.Section))
                .ThenBy(a => a.Section)
                .ThenBy(a => a.Label)
                .ToList();

            _isDirty = false;
        }
    }
}

