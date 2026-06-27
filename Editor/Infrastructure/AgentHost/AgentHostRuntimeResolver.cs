// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.IO;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Resolves the .NET runtime that launches the materialized daemon dll (<c>dotnet SidekickAgentHost.dll</c>).
    ///
    /// <para>
    /// Locked decision (plan Phase 0): the daemon is a framework-dependent <c>net6.0</c> assembly and the
    /// primary runtime is the one Unity bundles under
    /// <c>Contents/Resources/Scripting/NetCoreRuntime/dotnet</c> (verified .NET 6.0.21 on this project),
    /// which the daemon's <c>RollForward=Major</c> runtimeconfig targets. An EditorPrefs override
    /// (<see cref="OverridePrefKey"/>) wins when set; the Mono fallback is deliberately out of scope here.
    /// </para>
    ///
    /// <para>The probe logic is pure (takes a contents-path string) so it is unit-testable against a fake
    /// contents directory; the Unity-facing entry point reads <c>EditorApplication.applicationContentsPath</c>.</para>
    /// </summary>
    internal static class AgentHostRuntimeResolver
    {
        /// <summary>EditorPrefs key holding an absolute override path to a <c>dotnet</c> executable.</summary>
        public const string OverridePrefKey = "Sidekick_AgentHostRuntime";

        /// <summary>
        /// Probe the bundled-runtime candidates under <paramref name="applicationContentsPath"/> and
        /// return the first existing <c>dotnet</c>(.exe). <paramref name="overridePath"/> (the EditorPrefs
        /// override) wins when it points at an existing file. Returns <c>null</c> when nothing resolves —
        /// the connector then logs a clear error and falls back to the in-process host.
        /// </summary>
        public static string Resolve(string applicationContentsPath, string overridePath)
        {
            if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
                return overridePath;

            foreach (var candidate in CandidatePaths(applicationContentsPath))
            {
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// Ordered bundled-runtime candidate paths under the Editor's contents dir. The
        /// <c>NetCoreRuntime/dotnet</c> (macOS/Linux) / <c>NetCoreRuntime\dotnet.exe</c> (Windows) layout
        /// is verified for Unity 6; both platform names are probed so the same code works cross-platform.
        /// </summary>
        public static IEnumerable<string> CandidatePaths(string applicationContentsPath)
        {
            if (string.IsNullOrEmpty(applicationContentsPath))
                yield break;

            var netCore = Path.Combine(applicationContentsPath, "Resources", "Scripting", "NetCoreRuntime");
            // Windows first by extension, then the POSIX name — File.Exists settles which one is real.
            yield return Path.Combine(netCore, "dotnet.exe");
            yield return Path.Combine(netCore, "dotnet");
        }
    }
}
