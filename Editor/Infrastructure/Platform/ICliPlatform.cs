// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;

namespace Ryx.Sidekick.Editor.Infrastructure.Platform
{
    /// <summary>
    /// Platform abstraction for CLI operations.
    /// Implementations handle platform-specific CLI paths and process execution.
    /// </summary>
    internal interface ICliPlatform
    {
        /// <summary>
        /// Resolves the configured CLI path to an actual executable path.
        /// Checks provider-specific installation locations and uses platform-specific command lookup (where/which).
        /// </summary>
        /// <param name="configuredPath">The user-configured CLI path (may be just a binary name)</param>
        /// <param name="candidatePaths">Provider-specific candidate absolute paths ordered by likelihood.</param>
        /// <returns>The resolved absolute path to the CLI executable, or the original path as fallback</returns>
        string ResolveCliPath(string configuredPath, System.Collections.Generic.IReadOnlyList<string> candidatePaths);

        /// <summary>
        /// Creates a ProcessStartInfo configured for this platform.
        /// Handles shell wrapping (bash -l on Unix, cmd.exe on Windows) for proper PATH resolution.
        /// </summary>
        /// <param name="cliPath">The resolved CLI executable path</param>
        /// <param name="arguments">Command-line arguments to pass to the CLI</param>
        /// <param name="workingDirectory">Working directory for the process</param>
        /// <returns>A configured ProcessStartInfo ready for Process.Start()</returns>
        ProcessStartInfo CreateProcessStartInfo(string cliPath, string arguments, string workingDirectory);

        /// <summary>
        /// Creates a ProcessStartInfo that opens a visible terminal window for debugging.
        /// On macOS: opens Terminal.app; on Windows: opens cmd.exe window; on Linux: opens default terminal.
        /// </summary>
        /// <param name="cliPath">The resolved CLI executable path</param>
        /// <param name="arguments">Command-line arguments to pass to the CLI</param>
        /// <param name="workingDirectory">Working directory for the process</param>
        /// <returns>A configured ProcessStartInfo that launches a visible terminal</returns>
        ProcessStartInfo CreateDebugProcessStartInfo(string cliPath, string arguments, string workingDirectory);
    }
}
