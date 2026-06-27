// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Diagnostics;

namespace Ryx.Sidekick.Editor.Providers
{
    /// <summary>
    /// Selects where/how a spawned CLI process is surfaced.
    /// </summary>
    internal enum CliLaunchSurface
    {
        /// <summary>Headless process with stdio redirected — the normal in-editor streaming use.</summary>
        Streaming,

        /// <summary>A visible, interactive OS terminal window (what the legacy <c>debugMode</c> flag selected).</summary>
        InteractiveTerminal
    }

    /// <summary>
    /// Describes a CLI launch for <see cref="ICliProvider.CreateProcessStartInfo(CliLaunchRequest)"/>.
    /// </summary>
    internal struct CliLaunchRequest
    {
        /// <summary>Configured CLI path; providers re-resolve it against their platform candidates.</summary>
        public string CliPath { get; set; }

        /// <summary>CLI arguments. Empty for a bare interactive session.</summary>
        public string Arguments { get; set; }

        /// <summary>Working directory for the process (typically the project root).</summary>
        public string WorkingDirectory { get; set; }

        /// <summary>Launch surface (streaming vs. visible interactive terminal).</summary>
        public CliLaunchSurface Surface { get; set; }

        /// <summary>
        /// Environment variable overrides applied on top of the inherited process environment.
        /// A <c>null</c> value removes the variable (e.g. to clear an inherited one); any other
        /// value sets it. Provider-neutral — callers decide which variables a given CLI needs.
        /// </summary>
        public IReadOnlyDictionary<string, string> EnvironmentVariables { get; set; }

        /// <summary>
        /// Merges <see cref="EnvironmentVariables"/> into the given start info: a null value
        /// removes the key, anything else sets it. No-op when no overrides are supplied.
        /// </summary>
        public void ApplyEnvironmentTo(ProcessStartInfo startInfo)
        {
            if (startInfo == null || EnvironmentVariables == null)
            {
                return;
            }

            foreach (var pair in EnvironmentVariables)
            {
                if (pair.Value == null)
                {
                    startInfo.EnvironmentVariables.Remove(pair.Key);
                }
                else
                {
                    startInfo.EnvironmentVariables[pair.Key] = pair.Value;
                }
            }
        }
    }
}
