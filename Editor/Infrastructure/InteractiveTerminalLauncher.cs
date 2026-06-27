// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using Ryx.Sidekick.Editor.Providers;
using UnityEngine;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Opens a visible, interactive OS terminal at the project working directory running the
    /// active provider's CLI as a fresh session — the "Open in Terminal" command-palette action.
    /// Reuses the provider's <see cref="CliLaunchSurface.InteractiveTerminal"/> launch surface
    /// (the same primitive Debug Mode uses), independent of the persisted Debug Mode setting.
    /// </summary>
    internal sealed class InteractiveTerminalLauncher
    {
        public void OpenInteractiveSession()
        {
            var settings = SidekickSettings.instance;
            var provider = settings.ActiveProvider;
            if (provider == null)
            {
                UnityEngine.Debug.LogError("[Sidekick] No active provider to open in terminal.");
                return;
            }

            try
            {
                var startInfo = provider.CreateProcessStartInfo(new CliLaunchRequest
                {
                    CliPath = settings.CliPath,                  // provider re-resolves against its candidates
                    Arguments = string.Empty,                   // bare interactive session
                    WorkingDirectory = settings.WorkingDirectory, // already falls back to the project root
                    Surface = CliLaunchSurface.InteractiveTerminal,
                });

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Sidekick] Failed to open terminal: {ex.Message}");
            }
        }
    }
}
