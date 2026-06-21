// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;

namespace Ryx.Sidekick.Editor.Infrastructure.Platform
{
    /// <summary>
    /// macOS-specific implementation of CLI platform operations.
    /// Prefers direct execution when possible for proper streaming support.
    /// Falls back to bash -l for PATH resolution when needed.
    /// Handles Homebrew paths (Intel and Apple Silicon) and common macOS installation locations.
    /// </summary>
    internal class MacOSCliPlatform : ICliPlatform
    {
        public string ResolveCliPath(string configuredPath, System.Collections.Generic.IReadOnlyList<string> candidatePaths)
        {
            // If it's an absolute path, respect it (even if missing) so validation reports the issue.
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
                    "/opt/homebrew/bin",
                    Path.Combine(home, ".local/bin"),
                    Path.Combine(home, ".nvm/versions/node")
                );
                startInfo.EnvironmentVariables["PATH"] = $"{additionalPaths}:{path}";

                return startInfo;
            }

            // Fallback: run through bash -l for PATH resolution
            // Use 'script' to force line buffering for streaming
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
            var scriptPath = CreateCommandScript(cliPath, arguments, workingDirectory);

            return new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                Arguments = ShellQuote(scriptPath),
                UseShellExecute = true,
                CreateNoWindow = false
            };
        }

        private static string CreateCommandScript(string cliPath, string arguments, string workingDirectory)
        {
            var tempScript = Path.Combine(Path.GetTempPath(), $"claude_debug_{Guid.NewGuid():N}.command");
            Directory.CreateDirectory(Path.GetDirectoryName(tempScript)!);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("#!/bin/bash");
            sb.AppendLine("set +e");
            sb.AppendLine("export TERM=xterm-256color");

            if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
            {
                sb.AppendLine($"cd \"{workingDirectory}\"");
            }
            else
            {
                sb.AppendLine($"echo \"[Ryx Sidekick] Working directory not found: {workingDirectory}\"");
                sb.AppendLine("cd ~");
            }

            var safeCliPath = cliPath.Replace("\"", "\\\"");
            var safeArgs = arguments.Replace("\"", "\\\"");
            sb.AppendLine($"\"{safeCliPath}\" {safeArgs}");
            sb.AppendLine("status=$?");
            sb.AppendLine("echo");
            sb.AppendLine("echo \"[Process exited with code $status]\"");
            sb.AppendLine("echo \"[Press Enter to close]\"");
            sb.AppendLine("read");

            File.WriteAllText(tempScript, sb.ToString());

            try
            {
                var chmod = Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/chmod",
                    Arguments = $"+x \"{tempScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                chmod?.WaitForExit(1000);
            }
            catch { }

            return tempScript;
        }

        private static string ShellQuote(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "''";

            return $"'{value.Replace("'", "'\"'\"'")}'";
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
