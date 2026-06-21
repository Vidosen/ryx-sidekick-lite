// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.UseCases.Pro;
using UnityEditor;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    /// <summary>
    /// Bridges <see cref="ProPaywallLauncher"/> (raised from outside the App UI window, e.g. the MCP settings
    /// upsell) to the Sidekick window: opens/focuses the window when a paywall is requested. The window
    /// presenter then shows the paywall and consumes the pending highlight feature. This lives in Presentation
    /// because only Presentation may reference <see cref="SidekickWindow"/>; it must be a persistent
    /// [InitializeOnLoad] static so it reacts even when no window is currently open.
    /// </summary>
    [InitializeOnLoad]
    internal static class ProPaywallLauncherBridge
    {
        static ProPaywallLauncherBridge()
        {
            ProPaywallLauncher.OpenRequested += OnOpenRequested;
        }

        private static void OnOpenRequested()
        {
            // Opens or focuses the window; the live presenter / its CreateGUI consumes the pending feature.
            SidekickWindow.ShowWindow();
        }
    }
}
