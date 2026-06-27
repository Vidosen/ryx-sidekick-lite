// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Ryx.Sidekick.AgentHost
{
    /// <summary>
    /// Resolves the per-user, per-project discovery directory and writes the
    /// port/token/pid files the Unity client uses to find &amp; authenticate.
    ///
    /// Base dir is <see cref="Environment.SpecialFolder.LocalApplicationData"/>
    /// (≈ <c>~/Library/Application Support</c> on macOS, <c>%LOCALAPPDATA%</c> on
    /// Windows, <c>~/.local/share</c> / <c>$XDG_DATA_HOME</c> on Linux), under
    /// <c>Sidekick/AgentHost/&lt;projectHash&gt;/</c>.
    ///
    /// The port file is written ONLY after the listener has bound — its existence
    /// signals "ready". Token file is chmod 0600 where the platform supports it.
    /// </summary>
    internal static class DiscoveryPaths
    {
        public const string PortFileName = "daemon.port";
        public const string TokenFileName = "daemon.token";
        public const string PidFileName = "daemon.pid";

        public static string ProjectDir(string projectHash)
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.Combine(Path.GetTempPath(), "Sidekick-LocalAppData");

            return Path.Combine(baseDir, "Sidekick", "AgentHost", Sanitize(projectHash));
        }

        public static void WriteTokenFile(string path, string token)
        {
            EnsureDir(path);
            File.WriteAllText(path, token);
            TryChmod600(path);
        }

        public static void WritePidFile(string path, int pid)
        {
            EnsureDir(path);
            File.WriteAllText(path, pid.ToString());
        }

        /// <summary>
        /// Write the port file. Call ONLY after the TcpListener has bound, since its
        /// presence is the readiness signal for the client.
        /// </summary>
        public static void WritePortFile(string path, int port)
        {
            EnsureDir(path);
            File.WriteAllText(path, port.ToString());
        }

        public static string ReadToken(string path) => File.ReadAllText(path).Trim();

        public static void TryDelete(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best effort */ }
        }

        private static void EnsureDir(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        private static void TryChmod600(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return; // POSIX perms not applicable; NTFS ACLs are out of scope here.

            // File.SetUnixFileMode is .NET 7+, but we target net6.0 (Unity's bundled
            // runtime). Shell out to chmod instead — dependency-free and POSIX-safe.
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod",
                    ArgumentList = { "600", path },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                });
                p?.WaitForExit(2000);
            }
            catch { /* best effort */ }
        }

        private static string Sanitize(string hash)
        {
            // Keep only filesystem-safe chars so a caller-supplied hash can never
            // escape the AgentHost dir.
            var chars = hash.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                var ok = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_';
                if (!ok)
                    chars[i] = '_';
            }
            var s = new string(chars);
            return string.IsNullOrEmpty(s) ? "default" : s;
        }
    }
}
