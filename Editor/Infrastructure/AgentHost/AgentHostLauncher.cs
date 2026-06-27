// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Spawns the daemon OS process. Seam so the reuse-vs-spawn decision in <see cref="AgentHostLauncher"/>
    /// is unit-testable without launching a real process.
    /// </summary>
    internal interface IAgentHostSpawner
    {
        /// <summary>
        /// Launch <c>{dotnet} {dllPath} {args}</c> detached enough to survive the Editor's domain reload
        /// (a plain child OS process; NOT in a job object that dies with the parent). Returns false on failure.
        /// </summary>
        bool Spawn(string dotnetPath, string dllPath, string arguments, string workingDir);
    }

    /// <summary>
    /// Probes daemon liveness. Seam so the reuse decision is testable: a fake can report "pid alive" /
    /// "HELLO ok" deterministically.
    /// </summary>
    internal interface IAgentHostProbe
    {
        /// <summary>True if the OS process <paramref name="pid"/> is currently running.</summary>
        bool IsProcessAlive(int pid);

        /// <summary>
        /// Open a short-lived loopback connection to <paramref name="port"/>, send a HELLO with
        /// <paramref name="token"/>, and return true iff the daemon answers HELLO_OK. On success,
        /// <paramref name="daemonVersion"/> is the <c>HELLO_OK.daemonVersion</c> the daemon reported
        /// (empty if it could not be read). Used to confirm a discovered daemon is actually ours (right
        /// token + protocol) AND not a stale build (version skew) before reusing it.
        /// </summary>
        bool TryHandshake(int port, string token, int ownerPid, out string daemonVersion);
    }

    /// <summary>
    /// Decides whether to reuse an already-running daemon or spawn a fresh one, and returns a connectable
    /// endpoint. All side effects (spawn, liveness probe, handshake) go through injected seams so the
    /// decision logic is covered by EditMode tests with no real process.
    ///
    /// <para><b>Token coordination.</b> For reuse we read the token from the existing <c>daemon.token</c>.
    /// For a fresh spawn Unity generates the token, writes it to <c>daemon.token</c> FIRST, then passes
    /// <c>--token-file</c>; the daemon reads it (see the daemon's <c>Program.ResolveToken</c>: it uses an
    /// existing token-file when present, only generating one when absent). So Unity always holds the token
    /// deterministically without racing the daemon.</para>
    /// </summary>
    internal sealed class AgentHostLauncher
    {
        private readonly IAgentHostSpawner _spawner;
        private readonly IAgentHostProbe _probe;
        private readonly Action<string> _logInfo;
        private readonly Action<string> _logWarning;
        private readonly Action<string> _logError;

        // Bounded poll for the port file to appear after a fresh spawn.
        private readonly int _portFileTimeoutMs;
        private readonly int _portFilePollMs;

        // Bounded wait for a drained stale daemon's process to exit before the fresh spawn re-writes the
        // shared discovery files (so the dying daemon's self-cleanup cannot erase them). The daemon's own
        // graceful-stop ladder is bounded to ~5s, so this is the same order of magnitude.
        private const int StaleDrainTimeoutMs = 5000;

        public AgentHostLauncher(
            IAgentHostSpawner spawner,
            IAgentHostProbe probe,
            Action<string> logInfo = null,
            Action<string> logWarning = null,
            Action<string> logError = null,
            int portFileTimeoutMs = 8000,
            int portFilePollMs = 50)
        {
            _spawner = spawner ?? throw new ArgumentNullException(nameof(spawner));
            _probe = probe ?? throw new ArgumentNullException(nameof(probe));
            _logInfo = logInfo ?? (_ => { });
            _logWarning = logWarning ?? (_ => { });
            _logError = logError ?? (_ => { });
            _portFileTimeoutMs = portFileTimeoutMs;
            _portFilePollMs = portFilePollMs;
        }

        /// <summary>
        /// Ensure a daemon is reachable for <paramref name="discovery"/> and return its endpoint. Reuses a
        /// live daemon (pid alive + port file present + HELLO ok + matching <paramref name="expectedDaemonVersion"/>),
        /// otherwise spawns one. A discovered daemon that fails the version check (a stale daemon left
        /// running from an older Sidekick build) is drained (SHUTDOWN) and replaced. Returns an invalid
        /// endpoint (<see cref="AgentHostEndpoint.IsValid"/> == false) on any failure so the caller falls
        /// back to the in-process host.
        /// </summary>
        public AgentHostEndpoint EnsureRunning(
            in AgentHostDiscoveryPaths discovery,
            string dotnetPath,
            string dllPath,
            int ownerPid,
            int graceSeconds,
            string workingDir,
            string expectedDaemonVersion = null,
            bool verbose = false)
        {
            // 1. Try to REUSE an existing daemon.
            if (TryReuse(discovery, ownerPid, expectedDaemonVersion, verbose, out var existing))
            {
                if (verbose)
                    _logInfo($"[AgentHost] Reusing running daemon at 127.0.0.1:{existing.Port}.");
                return existing;
            }

            // 2. SPAWN a fresh one. Clean any stale discovery files first so we never read a previous
            //    daemon's port/pid (the dead-daemon case).
            Directory.CreateDirectory(discovery.Dir);
            SafeDelete(discovery.PortFile); // port file presence is the readiness signal — must be fresh

            var token = GenerateToken();
            try
            {
                WriteTokenFile(discovery.TokenFile, token);
            }
            catch (Exception ex)
            {
                _logError($"[AgentHost] Failed to write token file '{discovery.TokenFile}': {ex.Message}");
                return default;
            }

            var args = BuildDaemonArgs(discovery, ownerPid, graceSeconds);
            if (verbose)
                _logInfo($"[AgentHost] Spawning daemon: {dotnetPath} {dllPath} {args}");
            if (!_spawner.Spawn(dotnetPath, dllPath, args, workingDir))
            {
                _logError("[AgentHost] Failed to spawn the daemon process.");
                return default;
            }

            // 3. Wait (bounded) for the daemon to bind and write the port file.
            var port = WaitForPort(discovery.PortFile);
            if (port <= 0)
            {
                _logError($"[AgentHost] Daemon did not report a port within {_portFileTimeoutMs}ms.");
                return default;
            }

            if (verbose)
                _logInfo($"[AgentHost] Spawned fresh daemon on 127.0.0.1:{port}.");
            return new AgentHostEndpoint("127.0.0.1", port, token, ownerPid);
        }

        private bool TryReuse(
            in AgentHostDiscoveryPaths discovery,
            int ownerPid,
            string expectedDaemonVersion,
            bool verbose,
            out AgentHostEndpoint endpoint)
        {
            endpoint = default;

            if (!File.Exists(discovery.PidFile) || !File.Exists(discovery.PortFile) || !File.Exists(discovery.TokenFile))
                return false;

            if (!TryReadInt(discovery.PidFile, out var pid) || pid <= 0 || !_probe.IsProcessAlive(pid))
                return false;

            if (!TryReadInt(discovery.PortFile, out var port) || port <= 0)
                return false;

            string token;
            try { token = File.ReadAllText(discovery.TokenFile).Trim(); }
            catch { return false; }
            if (string.IsNullOrEmpty(token))
                return false;

            // Confirm it is actually our daemon (right token + protocol) before reusing. The handshake
            // also returns the daemon's reported version so we can detect a stale build (version skew).
            if (!_probe.TryHandshake(port, token, ownerPid, out var daemonVersion))
                return false;

            // Version-skew guard (plan risk #7): if the user updated Sidekick while an old daemon is
            // still running, the running daemon's version will not match the staged payload's. That
            // daemon owns no sessions we care about at first connect, so DRAIN it (SHUTDOWN, which stops
            // its children + exits) and fall through to spawn a fresh one from the current payload. On
            // any uncertainty (we could not read the staged version, or the daemon did not report one)
            // we DO NOT drain — better to reuse a possibly-current daemon than to thrash.
            if (!string.IsNullOrEmpty(expectedDaemonVersion)
                && !string.IsNullOrEmpty(daemonVersion)
                && !string.Equals(daemonVersion, expectedDaemonVersion, StringComparison.Ordinal))
            {
                _logWarning(
                    $"[AgentHost] Running daemon version '{daemonVersion}' != expected '{expectedDaemonVersion}' " +
                    "(stale daemon from an older Sidekick build). Draining it and spawning a fresh one.");
                DrainStaleDaemon(port, token, ownerPid, pid, verbose);
                return false;
            }

            endpoint = new AgentHostEndpoint("127.0.0.1", port, token, ownerPid);
            return true;
        }

        /// <summary>
        /// Stop a stale (version-skewed) daemon so a fresh one can take over its discovery files: send
        /// SHUTDOWN (the daemon stops its children, deletes its own port/token/pid, then exits) and wait
        /// (bounded) for its process to actually exit. The wait matters: the dying daemon deletes the
        /// SAME discovery file paths the fresh spawn is about to write, so we must let it finish its own
        /// cleanup BEFORE EnsureRunning re-writes the token + the fresh daemon writes its port — otherwise
        /// the stale daemon's late delete could erase the fresh daemon's files.
        /// </summary>
        private void DrainStaleDaemon(int port, string token, int ownerPid, int stalePid, bool verbose)
        {
            var stale = new AgentHostEndpoint("127.0.0.1", port, token, ownerPid);
            var sent = AgentHostShutdownClient.TrySendShutdown(stale, _logError);
            AgentHostEndpointRegistry.Remove(stale);

            // Bounded wait for the stale daemon to exit so its self-cleanup cannot race the fresh spawn.
            var exited = WaitForProcessExit(stalePid, StaleDrainTimeoutMs);
            if (verbose)
            {
                _logInfo($"[AgentHost] Drain SHUTDOWN to stale daemon on 127.0.0.1:{port}: " +
                         $"{(sent ? "sent" : "unreachable")}, process {(exited ? "exited" : "still alive after wait")}.");
            }
        }

        private bool WaitForProcessExit(int pid, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (!_probe.IsProcessAlive(pid))
                    return true;
                Thread.Sleep(_portFilePollMs);
            }
            return !_probe.IsProcessAlive(pid);
        }

        private int WaitForPort(string portFile)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < _portFileTimeoutMs)
            {
                if (File.Exists(portFile) && TryReadInt(portFile, out var port) && port > 0)
                    return port;
                Thread.Sleep(_portFilePollMs);
            }
            return 0;
        }

        internal static string BuildDaemonArgs(in AgentHostDiscoveryPaths discovery, int ownerPid, int graceSeconds)
        {
            // ProcessStartInfo.Arguments string (the spawner quotes the dll path + this); each path is
            // quoted because LocalAppData can contain spaces ("Application Support").
            return string.Join(" ",
                "--port-file", Quote(discovery.PortFile),
                "--token-file", Quote(discovery.TokenFile),
                "--pid-file", Quote(discovery.PidFile),
                "--grace-seconds", graceSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--owner-pid", ownerPid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--project-hash", Quote(discovery.ProjectHash));
        }

        private static string Quote(string s) => "\"" + (s ?? string.Empty) + "\"";

        private static string GenerateToken()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static void WriteTokenFile(string path, string token)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, token);
            TryChmod600(path);
        }

        private static void TryChmod600(string path)
        {
            if (Path.DirectorySeparatorChar == '\\')
                return; // Windows: POSIX perms n/a.
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = "600 \"" + path + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                p?.WaitForExit(2000);
            }
            catch { /* best effort */ }
        }

        private static bool TryReadInt(string path, out int value)
        {
            value = 0;
            try
            {
                var text = File.ReadAllText(path).Trim();
                return int.TryParse(text, out value);
            }
            catch
            {
                return false;
            }
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best effort */ }
        }
    }
}
