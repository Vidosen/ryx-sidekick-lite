// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Linq;
using Ryx.Sidekick.Editor.Providers.Claude;

namespace Ryx.Sidekick.Editor.Providers
{
    /// <summary>
    /// Maintains the list of registered CLI providers and resolves the active one.
    /// The Lite package seeds only the built-in Claude provider. Extension packages
    /// (e.g. Sidekick Pro) add further providers via <see cref="Register"/>.
    /// </summary>
    internal static class CliProviderRegistry
    {
        // Insertion-ordered map. Claude is seeded by the static ctor; extensions
        // append via Register(...) preserving registration order.
        private static readonly Dictionary<string, ICliProvider> _providers;
        private static readonly List<string> _order;

        // Providers that make up the baseline restored by ResetForTests — the built-in
        // Claude provider plus any extension-package providers (e.g. Sidekick Pro's Codex
        // and Cursor) registered via RegisterPermanent at editor load. Test fakes are added
        // through Register (not RegisterPermanent), so they are never part of the baseline
        // and ResetForTests drops them.
        private static readonly List<ICliProvider> _permanent;

        static CliProviderRegistry()
        {
            _providers = new Dictionary<string, ICliProvider>();
            _order = new List<string>();
            _permanent = new List<ICliProvider>();
            RegisterPermanent(new ClaudeCliProvider());
        }

        /// <summary>
        /// Registers a provider. Idempotent by <see cref="ICliProvider.Id"/>: the first
        /// registration for an Id wins and later duplicates are ignored. Registration
        /// order is preserved (Claude first, then extension registration order).
        /// </summary>
        public static void Register(ICliProvider provider)
        {
            if (provider == null || string.IsNullOrEmpty(provider.Id))
                return;
            if (!_providers.TryAdd(provider.Id, provider))
                return;
            _order.Add(provider.Id);
        }

        /// <summary>
        /// Registers a provider that is part of the permanent baseline — i.e. it survives
        /// <see cref="ResetForTests"/>. Extension packages (e.g. Sidekick Pro) must use this
        /// instead of <see cref="Register"/> so their providers are restored after a test reset
        /// without requiring a domain reload to re-run their [InitializeOnLoad] bootstrap.
        /// </summary>
        internal static void RegisterPermanent(ICliProvider provider)
        {
            if (provider == null || string.IsNullOrEmpty(provider.Id))
                return;
            if (!_permanent.Any(p => p.Id == provider.Id))
                _permanent.Add(provider);
            Register(provider);
        }

        /// <summary>All registered providers in registration order.</summary>
        public static IReadOnlyList<ICliProvider> AllProviders => _order.Select(id => _providers[id]).ToList();

        /// <summary>
        /// Test-only: clears all registered providers and restores the permanent baseline
        /// (built-in Claude plus any extension-package providers registered via
        /// <see cref="RegisterPermanent"/>). Tests that register fakes via <see cref="Register"/>
        /// MUST call this in teardown — the registry is static and would otherwise leak fakes
        /// into the live Editor until the next domain reload.
        /// </summary>
        internal static void ResetForTests()
        {
            _providers.Clear();
            _order.Clear();
            foreach (var provider in _permanent)
                Register(provider);
        }

        /// <summary>
        /// Returns the provider by ID, or the Claude provider as fallback.
        /// </summary>
        public static ICliProvider GetProvider(string id)
        {
            if (!string.IsNullOrEmpty(id) && _providers.TryGetValue(id, out var provider))
                return provider;
            return _providers["claude"];
        }

        public static ProviderModeSelection NormalizeModeSelection(ICliProvider provider, string collaborationMode, string permissionMode)
        {
            return (provider ?? GetProvider(null)).NormalizeModeSelection(collaborationMode, permissionMode);
        }

        public static string[] GetCollaborationModeValues(ICliProvider provider)
        {
            return provider.CollaborationModes.Select(m => m.Value).ToArray();
        }

        public static string[] GetCollaborationModeLabels(ICliProvider provider)
        {
            return provider.CollaborationModes.Select(m => m.Label).ToArray();
        }

        /// <summary>
        /// Returns the permission mode values (string array) for the given provider,
        /// suitable for IMGUI popup display.
        /// </summary>
        public static string[] GetPermissionModeValues(ICliProvider provider, string collaborationMode)
        {
            return provider.GetPermissionModes(collaborationMode).Select(m => m.Value).ToArray();
        }

        /// <summary>
        /// Returns the permission mode labels for the given provider.
        /// </summary>
        public static string[] GetPermissionModeLabels(ICliProvider provider, string collaborationMode)
        {
            return provider.GetPermissionModes(collaborationMode).Select(m => m.Label).ToArray();
        }
    }
}
