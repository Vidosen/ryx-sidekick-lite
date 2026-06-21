// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor.UseCases.Pro
{
    /// <summary>
    /// Cross-context request to open the in-app Pro paywall from a surface that lives OUTSIDE the App UI
    /// window / DI host — e.g. the MCP Project Settings page upsell. Infrastructure raises <see cref="Request"/>;
    /// Presentation reacts: a [InitializeOnLoad] bridge opens the Sidekick window, and the window presenter
    /// shows the paywall (consuming the pending highlight feature on init or via <see cref="OpenRequested"/>
    /// when the window is already open). Pure C# so it stays in the Application layer (no Editor/UI deps),
    /// which lets Infrastructure invoke it without referencing Presentation.
    /// </summary>
    internal static class ProPaywallLauncher
    {
        private static string _pendingFeatureId;

        /// <summary>Raised whenever a surface requests the paywall. Subscribers: the window-open bridge and the live window presenter.</summary>
        public static event Action OpenRequested;

        /// <summary>Request the paywall, optionally highlighting a feature id (e.g. "mcp-management").</summary>
        public static void Request(string highlightFeatureId = null)
        {
            _pendingFeatureId = highlightFeatureId;
            OpenRequested?.Invoke();
        }

        /// <summary>Returns and clears the pending highlight feature id (null when nothing is pending).</summary>
        public static string ConsumePending()
        {
            var id = _pendingFeatureId;
            _pendingFeatureId = null;
            return id;
        }
    }
}
