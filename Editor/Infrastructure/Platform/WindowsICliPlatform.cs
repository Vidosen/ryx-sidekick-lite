// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;

namespace Ryx.Sidekick.Editor.Infrastructure.Platform
{
    /// <summary>
    /// Windows-specific implementation of CLI platform operations.
    /// Prefers direct execution when possible for proper streaming support.
    /// Falls back to cmd.exe for PATH resolution when needed.
    /// </summary>
    internal class WindowsICliPlatform : ICliPlatform
    {
        public string ResolveCliPath(string configuredPath, System.Collections.Generic.IReadOnlyList<string> candidatePaths)
        {
            // Respect explicit absolute paths so validation can signal missing binaries.
            if (Path.IsPathRooted(configuredPath))
                return configuredPath;

            // Check provider-specific installation paths
            if (candidatePaths != null)
            {
                foreach (var path in candidatePaths)
                {
                    if (!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) && File.Exists(path))
                        return path;
                }
            }

            // Try 'where' command to find the executable
            try
            {
                var whereInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c where {configuredPath}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var whereProcess = Process.Start(whereInfo);
                if (whereProcess != null)
                {
                    var result = whereProcess.StandardOutput.ReadLine()?.Trim();
                    whereProcess.WaitForExit(3000);
                    if (whereProcess.ExitCode == 0 && !string.IsNullOrEmpty(result) && File.Exists(result))
                        return result;
                }
            }
            catch
            {
                // Ignore errors from where command
            }

            return configuredPath; // Return original as fallback
        }

        public ProcessStartInfo CreateProcessStartInfo(string cliPath, string arguments, string workingDirectory)
        {
            // For .cmd files (npm scripts), we need to use cmd.exe even when the path is absolute.
            if (cliPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                return new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{cliPath}\" {arguments}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory
                };
            }

            // If we have a resolved absolute path that exists, run it directly
            // This avoids shell buffering issues that break JSON streaming
            if (Path.IsPathRooted(cliPath) && File.Exists(cliPath))
            {
                return new ProcessStartInfo
                {
                    FileName = cliPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory
                };
            }

            // Fallback: run through cmd.exe for PATH resolution
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{cliPath}\" {arguments}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };
        }

        public ProcessStartInfo CreateInteractiveTerminalStartInfo(string cliPath, string arguments, string workingDirectory)
        {
            // On Windows, open cmd.exe in a visible window with /K to keep it open after execution
            var escapedCliPath = cliPath.Replace("\"", "\\\"");

            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/K cd /d \"{workingDirectory}\" && \"{escapedCliPath}\" {arguments}",
                UseShellExecute = true,
                CreateNoWindow = false,
                WorkingDirectory = workingDirectory
            };
        }
    }
}
