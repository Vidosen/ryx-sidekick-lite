// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;

namespace Ryx.Sidekick.Editor.Infrastructure.Platform
{
    /// <summary>
    /// Linux-specific implementation of CLI platform operations.
    /// Prefers direct execution when possible for proper streaming support.
    /// Falls back to bash -l for PATH resolution when needed.
    /// Handles common Linux installation locations including .local/bin and nvm.
    /// </summary>
    internal class LinuxCliPlatform : ICliPlatform
    {
        public string ResolveCliPath(string configuredPath, System.Collections.Generic.IReadOnlyList<string> candidatePaths)
        {
            // Respect explicit absolute paths (even if missing) so users get clear errors.
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

            // Check nvm paths
            var nvmPath = ResolveNvmPath();
            if (nvmPath != null)
                return nvmPath;

            // Try 'which' or 'type' command to find the executable (handles aliases)
            try
            {
                var typeInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-l -c \"type -P {configuredPath} 2>/dev/null || which {configuredPath} 2>/dev/null\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var typeProcess = Process.Start(typeInfo);
                if (typeProcess != null)
                {
                    var result = typeProcess.StandardOutput.ReadToEnd().Trim();
                    typeProcess.WaitForExit(3000);
                    if (typeProcess.ExitCode == 0 && !string.IsNullOrEmpty(result) && File.Exists(result))
                        return result;
                }
            }
            catch
            {
                // Ignore errors from type/which command
            }

            return configuredPath; // Return original as fallback
        }

        public ProcessStartInfo CreateProcessStartInfo(string cliPath, string arguments, string workingDirectory)
        {
            // If we have a resolved absolute path that exists, run it directly
            // This avoids shell buffering issues that break JSON streaming
            if (Path.IsPathRooted(cliPath) && File.Exists(cliPath))
            {
                var startInfo = new ProcessStartInfo
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

                // Ensure PATH includes common locations for any child processes
                var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var additionalPaths = string.Join(":",
                    "/usr/local/bin",
                    Path.Combine(home, ".local/bin"),
                    "/snap/bin",
                    Path.Combine(home, ".nvm/versions/node")
                );
                startInfo.EnvironmentVariables["PATH"] = $"{additionalPaths}:{path}";

                return startInfo;
            }

            // Fallback: run through bash -l for PATH resolution
            var escapedArgs = arguments.Replace("\"", "\\\"");
            return new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-l -c \"exec {cliPath} {escapedArgs}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };
        }

        public ProcessStartInfo CreateDebugProcessStartInfo(string cliPath, string arguments, string workingDirectory)
        {
            // On Linux, try common terminal emulators in order of preference
            // Use x-terminal-emulator if available (Debian/Ubuntu default), fallback to gnome-terminal, xterm
            var escapedCliPath = cliPath.Replace("\"", "\\\"");
            var escapedArgs = arguments.Replace("\"", "\\\"");
            var escapedWorkDir = workingDirectory.Replace("\"", "\\\"");

            var command = $"cd \"{escapedWorkDir}\" && \"{escapedCliPath}\" {escapedArgs}; echo; echo '[Press Enter to close]'; read";

            // Try to detect available terminal
            string terminal = null;
            string terminalArgs = null;

            if (File.Exists("/usr/bin/x-terminal-emulator"))
            {
                terminal = "/usr/bin/x-terminal-emulator";
                terminalArgs = $"-e bash -c \"{command}\"";
            }
            else if (File.Exists("/usr/bin/gnome-terminal"))
            {
                terminal = "/usr/bin/gnome-terminal";
                terminalArgs = $"-- bash -c \"{command}\"";
            }
            else if (File.Exists("/usr/bin/konsole"))
            {
                terminal = "/usr/bin/konsole";
                terminalArgs = $"-e bash -c \"{command}\"";
            }
            else if (File.Exists("/usr/bin/xterm"))
            {
                terminal = "/usr/bin/xterm";
                terminalArgs = $"-e bash -c \"{command}\"";
            }
            else
            {
                // Fallback: just run in background with xterm as default
                terminal = "xterm";
                terminalArgs = $"-e bash -c \"{command}\"";
            }

            return new ProcessStartInfo
            {
                FileName = terminal,
                Arguments = terminalArgs,
                UseShellExecute = true,
                CreateNoWindow = false,
                WorkingDirectory = workingDirectory
            };
        }

        private string ResolveNvmPath()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var nvmDir = Path.Combine(home, ".nvm/versions/node");

            if (!Directory.Exists(nvmDir))
                return null;

            foreach (var nodeDir in Directory.GetDirectories(nvmDir))
            {
                var claudePath = Path.Combine(nodeDir, "bin", "claude");
                if (File.Exists(claudePath))
                    return claudePath;
            }

            return null;
        }
    }
}
