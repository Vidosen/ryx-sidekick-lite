// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// The real Phase 4 <see cref="IAgentHostConnector"/>: materializes the shipped daemon payload, resolves
    /// the bundled .NET runtime, computes the per-project discovery layout, then reuses-or-spawns the daemon
    /// and returns its loopback endpoint. Replaces <see cref="UnavailableAgentHostConnector"/> in DI.
    ///
    /// <para>Flow (all on the Editor main thread — <see cref="TryConnect"/> is called from
    /// <c>RemoteProcessHost.EnsureConnected</c> / the factory, both main-thread):</para>
    /// <list type="number">
    /// <item><b>Materialize</b> the <c>.bytes</c> TextAssets under <c>Editor/AgentHostPayload/</c> into a
    ///   per-user, per-project <c>bin/</c> dir; re-extract when the staged VERSION changed.</item>
    /// <item><b>Resolve runtime</b> from <see cref="EditorApplication.applicationContentsPath"/>
    ///   (<c>NetCoreRuntime/dotnet</c>), honoring the <c>Sidekick_AgentHostRuntime</c> EditorPrefs override.</item>
    /// <item><b>Discovery</b> dir + port/token/pid paths (mirrors the daemon's <c>DiscoveryPaths</c>).</item>
    /// <item><b>Reuse-or-spawn</b> via <see cref="AgentHostLauncher"/>; wait (bounded) for the port file.</item>
    /// </list>
    ///
    /// <para>Any failure logs a clear warning/error and returns <c>IsValid=false</c> so
    /// <see cref="IProcessHostFactory"/> falls back to the in-process <see cref="CliProcessHost"/> — zero
    /// regression if anything goes wrong.</para>
    /// </summary>
    internal sealed class AgentHostConnector : IAgentHostConnector
    {
        // Where the .bytes TextAssets live (imported, so they carry a .meta and survive ExportPackage).
        private const string PayloadFolder =
            "Packages/com.ryxinteractive.sidekick/Editor/AgentHostPayload";

        private const string DllAsset = PayloadFolder + "/SidekickAgentHost.dll.bytes";
        private const string RuntimeConfigAsset = PayloadFolder + "/SidekickAgentHost.runtimeconfig.json.bytes";
        private const string DepsAsset = PayloadFolder + "/SidekickAgentHost.deps.json.bytes";
        private const string VersionAsset = PayloadFolder + "/VERSION.txt";

        // Grace window (seconds) the daemon stays up with no client connected before self-terminating —
        // covers BOTH a slow first connect (the daemon arms grace the moment it binds, before Unity's
        // first connection) and a domain reload (UI gone for seconds). 30s proved too tight: a slow
        // capabilities refresh between spawn and the first turn's connect let the daemon grace-expire →
        // "Connection refused". 120s gives generous headroom; the --owner-pid watchdog still reclaims the
        // daemon immediately if the Editor process itself dies, so a longer grace adds no orphan risk.
        private const int DefaultGraceSeconds = 120;

        private readonly AgentHostLauncher _launcher;

        public AgentHostConnector()
            : this(new ProcessSpawner(), new TcpProbe())
        {
        }

        // Test/extension seam.
        internal AgentHostConnector(IAgentHostSpawner spawner, IAgentHostProbe probe)
        {
            _launcher = new AgentHostLauncher(
                spawner,
                probe,
                logInfo: msg => Debug.Log(msg),
                logWarning: msg => Debug.LogWarning(msg),
                logError: msg => Debug.LogError(msg));
        }

        public bool TryConnect(out AgentHostEndpoint endpoint)
        {
            // No endpoint cache here, by design. Every call runs through EnsureRunning, whose TryReuse
            // validates an existing daemon with a pid-alive + HELLO handshake (and spawns a fresh one if
            // it died / was grace-reclaimed). A cached endpoint was both buggy and redundant: a
            // grace-dead daemon kept a valid-looking endpoint (IsValid is just "an endpoint was resolved",
            // not "the daemon is alive") → "Connection refused", and nothing ever re-probed or
            // invalidated it, so the failure was permanent until the next domain reload recreated the
            // connector. Materialize + runtime-resolve are no-ops once warm and the handshake is a single
            // quick loopback round-trip, so the per-call cost is negligible. Self-healing by construction.
            endpoint = default;
            var verbose = false;
            try { verbose = SidekickSettings.instance.VerboseLogging; }
            catch { /* settings unavailable in some test contexts — default to quiet */ }

            try
            {
                // 1. Materialize.
                var projectRoot = ResolveProjectRoot();
                var discovery = AgentHostDiscovery.Resolve(projectRoot);
                var binDir = AgentHostDiscovery.MaterializationBinDir(discovery.ProjectHash);

                var (version, files) = LoadStagedPayload();
                if (files == null)
                {
                    Debug.LogError(
                        "[AgentHost] Daemon payload .bytes not found under " + PayloadFolder +
                        ". Run 'Tools/Sidekick/Release/Build Agent Host' to stage it. Falling back to the in-process host.");
                    return false;
                }

                var dllPath = AgentHostPayloadStore.EnsureMaterialized(binDir, version, files);
                if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
                {
                    Debug.LogError("[AgentHost] Failed to materialize the daemon dll. Falling back to the in-process host.");
                    return false;
                }

                if (verbose)
                    Debug.Log($"[AgentHost] Materialized daemon payload v{version} → {dllPath}");

                // 2. Resolve runtime.
                var dotnetPath = AgentHostRuntimeResolver.Resolve(
                    EditorApplication.applicationContentsPath,
                    EditorPrefs.GetString(AgentHostRuntimeResolver.OverridePrefKey, string.Empty));
                if (string.IsNullOrEmpty(dotnetPath))
                {
                    Debug.LogError(
                        "[AgentHost] Could not resolve a bundled .NET runtime (NetCoreRuntime/dotnet) under " +
                        EditorApplication.applicationContentsPath +
                        ". Set EditorPrefs '" + AgentHostRuntimeResolver.OverridePrefKey +
                        "' to a dotnet path to override. Falling back to the in-process host.");
                    return false;
                }

                // 3 + 4. Reuse-or-spawn. Pass the staged VERSION as the expected daemon version so a
                // stale daemon from an older Sidekick build is drained + replaced (version-skew guard).
                // The "unknown" sentinel (a payload staged without a VERSION stamp) is treated as "no
                // expected version" so we never thrash on uncertainty (plan: drain only on a real skew).
                var ownerPid = Process.GetCurrentProcess().Id;
                var expectedVersion = string.Equals(version, "unknown", StringComparison.Ordinal) ? null : version;
                var result = _launcher.EnsureRunning(
                    discovery, dotnetPath, dllPath, ownerPid, DefaultGraceSeconds, projectRoot,
                    expectedDaemonVersion: expectedVersion, verbose: verbose);

                if (!result.IsValid)
                    return false;

                endpoint = result;

                // Register the live endpoint so a clean Editor quit can SHUTDOWN the daemon immediately
                // (stop its children) instead of waiting out its grace window. A domain reload never
                // touches this — the child must survive a reload for the next domain to re-attach.
                AgentHostEndpointRegistry.Register(result);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AgentHost] Connector failed: {ex.Message}. Falling back to the in-process host.");
                return false;
            }
        }

        private static string ResolveProjectRoot()
        {
            // Same root SidekickSettings.GetProjectRoot uses (parent of Assets/) so the discovery hash is
            // stable and matches what the daemon derives from --project-hash.
            return Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        }

        /// <summary>
        /// Loads the staged version + payload files from the imported <c>.bytes</c> TextAssets. Returns
        /// (null files) when the dll asset is missing (payload not staged).
        /// </summary>
        private static (string version, List<AgentHostPayloadFile> files) LoadStagedPayload()
        {
            var dll = AssetDatabase.LoadAssetAtPath<TextAsset>(DllAsset);
            if (dll == null || dll.bytes == null || dll.bytes.Length == 0)
                return (null, null);

            var files = new List<AgentHostPayloadFile>
            {
                new AgentHostPayloadFile(AgentHostPayloadNames.DaemonDll, dll.bytes),
            };

            var runtimeConfig = AssetDatabase.LoadAssetAtPath<TextAsset>(RuntimeConfigAsset);
            if (runtimeConfig != null && runtimeConfig.bytes != null && runtimeConfig.bytes.Length > 0)
                files.Add(new AgentHostPayloadFile(AgentHostPayloadNames.RuntimeConfig, runtimeConfig.bytes));

            var deps = AssetDatabase.LoadAssetAtPath<TextAsset>(DepsAsset);
            if (deps != null && deps.bytes != null && deps.bytes.Length > 0)
                files.Add(new AgentHostPayloadFile(AgentHostPayloadNames.Deps, deps.bytes));

            // Version: prefer the shipped VERSION.txt; fall back to a constant so a missing stamp still
            // produces a deterministic re-extract key.
            var versionAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(VersionAsset);
            var version = versionAsset != null && !string.IsNullOrWhiteSpace(versionAsset.text)
                ? versionAsset.text.Trim()
                : "unknown";

            return (version, files);
        }

        /// <summary>Real spawner: a plain detached child process (survives the Editor's domain reload).</summary>
        private sealed class ProcessSpawner : IAgentHostSpawner
        {
            public bool Spawn(string dotnetPath, string dllPath, string arguments, string workingDir)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = dotnetPath,
                        // "<dll>" <daemon-args> — dll path quoted (LocalAppData can contain spaces).
                        Arguments = "\"" + dllPath + "\" " + arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false,
                        RedirectStandardInput = false,
                    };
                    if (!string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir))
                        psi.WorkingDirectory = workingDir;

                    var proc = Process.Start(psi);
                    // We do NOT hold/own the process: it is intentionally detached so it survives our
                    // domain reload. Its grace timer + --owner-pid watchdog reclaim it. Dispose only our
                    // handle (does not kill the child).
                    proc?.Dispose();
                    return proc != null;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AgentHost] Process.Start failed for '{dotnetPath}': {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>Real probe: pid liveness via <see cref="Process"/>; HELLO over a short-lived TCP socket.</summary>
        private sealed class TcpProbe : IAgentHostProbe
        {
            public bool IsProcessAlive(int pid)
            {
                try
                {
                    var p = Process.GetProcessById(pid);
                    return !p.HasExited;
                }
                catch (ArgumentException) { return false; }
                catch (InvalidOperationException) { return false; }
                catch { return false; }
            }

            public bool TryHandshake(int port, string token, int ownerPid, out string daemonVersion)
            {
                daemonVersion = string.Empty;
                TcpClient client = null;
                try
                {
                    client = new TcpClient { NoDelay = true };
                    var connect = client.BeginConnect("127.0.0.1", port, null, null);
                    if (!connect.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(750)))
                        return false;
                    client.EndConnect(connect);

                    using var stream = client.GetStream();
                    stream.ReadTimeout = 1000;
                    stream.WriteTimeout = 1000;

                    var hello = new JObject
                    {
                        ["t"] = WireProtocol.Hello,
                        ["token"] = token ?? string.Empty,
                        ["proto"] = WireProtocol.ProtocolVersion,
                        ["ownerPid"] = ownerPid,
                    };
                    var bytes = Encoding.UTF8.GetBytes(hello.ToString(Newtonsoft.Json.Formatting.None) + "\n");
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();

                    using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 1024, leaveOpen: true);
                    var line = reader.ReadLine();
                    if (string.IsNullOrEmpty(line))
                        return false;

                    var parsed = JObject.Parse(line);
                    if (!string.Equals((string)parsed["t"], WireProtocol.HelloOk, StringComparison.Ordinal))
                        return false;

                    daemonVersion = (string)parsed["daemonVersion"] ?? string.Empty;
                    return true;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    try { client?.Close(); } catch { /* ignore */ }
                }
            }
        }
    }
}
