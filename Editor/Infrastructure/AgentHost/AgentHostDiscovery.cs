// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// The per-project discovery directory and the port/token/pid file paths the Unity client and the
    /// daemon BOTH use to rendezvous. Unity decides the project hash + dir and passes the file paths to
    /// the daemon as <c>--port-file</c>/<c>--token-file</c>/<c>--pid-file</c> (with <c>--project-hash</c>
    /// so an arg-less daemon derives the same defaults).
    /// </summary>
    internal readonly struct AgentHostDiscoveryPaths
    {
        public AgentHostDiscoveryPaths(string projectHash, string dir, string portFile, string tokenFile, string pidFile)
        {
            ProjectHash = projectHash;
            Dir = dir;
            PortFile = portFile;
            TokenFile = tokenFile;
            PidFile = pidFile;
        }

        public string ProjectHash { get; }
        public string Dir { get; }
        public string PortFile { get; }
        public string TokenFile { get; }
        public string PidFile { get; }
    }

    /// <summary>
    /// Pure (Unity-free) computation of the discovery layout. The directory rules here MUST stay
    /// byte-for-byte in lock-step with the daemon's <c>Ryx.Sidekick.AgentHost.DiscoveryPaths</c>
    /// (same base folder, same <c>Sidekick/AgentHost/&lt;hash&gt;</c> sub-path, same sanitize rules,
    /// same file names) — the daemon and the client are separate assemblies that cannot share code.
    /// </summary>
    internal static class AgentHostDiscovery
    {
        // Mirror of the daemon's DiscoveryPaths.* file-name constants.
        public const string PortFileName = "daemon.port";
        public const string TokenFileName = "daemon.token";
        public const string PidFileName = "daemon.pid";

        // Mirror of the daemon's bin/ materialization sub-path siblings.
        private const string RootFolder = "Sidekick";
        private const string AgentHostFolder = "AgentHost";

        /// <summary>
        /// Computes a stable, filesystem-safe project hash from the absolute project root. Stable across
        /// Editor restarts and domain reloads (same root ⇒ same hash ⇒ same daemon), and distinct per
        /// project (two projects ⇒ two daemons). SHA-256 over the lowercased full path, first 16 hex
        /// chars — short, collision-resistant enough for a per-user local dir.
        /// </summary>
        public static string ComputeProjectHash(string projectRoot)
        {
            var normalized = (projectRoot ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            // Lowercase so case-insensitive filesystems (macOS/Windows) map the same root to one hash.
            normalized = normalized.ToLowerInvariant();
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
            var sb = new StringBuilder(32);
            for (var i = 0; i < 8 && i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        /// <summary>
        /// The per-project discovery directory: <c>{LocalAppData}/Sidekick/AgentHost/{sanitized-hash}</c>.
        /// Identical to the daemon's <c>DiscoveryPaths.ProjectDir</c> so both sides resolve the same dir.
        /// </summary>
        public static string ProjectDir(string projectHash)
        {
            return ProjectDir(projectHash, LocalAppDataBase());
        }

        /// <summary>Overload that takes an explicit base dir (for tests).</summary>
        public static string ProjectDir(string projectHash, string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.Combine(Path.GetTempPath(), "Sidekick-LocalAppData");
            return Path.Combine(baseDir, RootFolder, AgentHostFolder, Sanitize(projectHash));
        }

        /// <summary>Resolve the full discovery layout (dir + 3 files) for a project root.</summary>
        public static AgentHostDiscoveryPaths Resolve(string projectRoot)
        {
            var hash = ComputeProjectHash(projectRoot);
            var dir = ProjectDir(hash);
            return new AgentHostDiscoveryPaths(
                hash,
                dir,
                Path.Combine(dir, PortFileName),
                Path.Combine(dir, TokenFileName),
                Path.Combine(dir, PidFileName));
        }

        /// <summary>
        /// The per-project materialization <c>bin/</c> directory for the daemon binaries:
        /// <c>{discoveryDir}/bin</c>. Kept under the same per-project dir so removing one project's
        /// AgentHost state is a single recursive delete.
        /// </summary>
        public static string MaterializationBinDir(string projectHash)
        {
            return Path.Combine(ProjectDir(projectHash), "bin");
        }

        private static string LocalAppDataBase()
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.Combine(Path.GetTempPath(), "Sidekick-LocalAppData");
            return baseDir;
        }

        // Mirror of the daemon's DiscoveryPaths.Sanitize.
        private static string Sanitize(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return "default";
            var chars = hash.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                         (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok)
                    chars[i] = '_';
            }
            var s = new string(chars);
            return string.IsNullOrEmpty(s) ? "default" : s;
        }
    }
}
