// SPDX-License-Identifier: GPL-3.0-only
using System.IO;

namespace Ryx.Sidekick.AgentHost
{
    /// <summary>
    /// Parsed command-line options for the daemon. Recognized flags:
    /// <c>--port-file</c>, <c>--token-file</c>, <c>--pid-file</c>,
    /// <c>--grace-seconds</c>, <c>--owner-pid</c>, <c>--project-hash</c>,
    /// plus <c>--help</c>/<c>--version</c>. When file paths are omitted, sensible
    /// per-user defaults under the project-hash dir are derived (see
    /// <see cref="DiscoveryPaths"/>).
    /// </summary>
    internal sealed class DaemonOptions
    {
        public string? PortFile { get; private set; }
        public string? TokenFile { get; private set; }
        public string? PidFile { get; private set; }
        public int GraceSeconds { get; private set; } = 120;
        public int OwnerPid { get; private set; } = 0;
        public string? ProjectHash { get; private set; }

        public bool ShowHelp { get; private set; }
        public bool ShowVersion { get; private set; }

        public static DaemonOptions Parse(string[] args)
        {
            var o = new DaemonOptions();
            if (args == null)
                return o;

            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                switch (a)
                {
                    case "--help":
                    case "-h":
                        o.ShowHelp = true;
                        break;
                    case "--version":
                    case "-v":
                        o.ShowVersion = true;
                        break;
                    case "--port-file":
                        o.PortFile = Next(args, ref i);
                        break;
                    case "--token-file":
                        o.TokenFile = Next(args, ref i);
                        break;
                    case "--pid-file":
                        o.PidFile = Next(args, ref i);
                        break;
                    case "--grace-seconds":
                        if (int.TryParse(Next(args, ref i), out var g) && g >= 0)
                            o.GraceSeconds = g;
                        break;
                    case "--owner-pid":
                        if (int.TryParse(Next(args, ref i), out var p))
                            o.OwnerPid = p;
                        break;
                    case "--project-hash":
                        o.ProjectHash = Next(args, ref i);
                        break;
                    default:
                        // Unknown flag: ignore for forward-compat.
                        break;
                }
            }

            // Derive default file paths from the project hash when not given.
            if (!string.IsNullOrEmpty(o.ProjectHash))
            {
                var dir = DiscoveryPaths.ProjectDir(o.ProjectHash!);
                o.PortFile ??= Path.Combine(dir, DiscoveryPaths.PortFileName);
                o.TokenFile ??= Path.Combine(dir, DiscoveryPaths.TokenFileName);
                o.PidFile ??= Path.Combine(dir, DiscoveryPaths.PidFileName);
            }

            return o;
        }

        private static string? Next(string[] args, ref int i)
            => i + 1 < args.Length ? args[++i] : null;
    }
}
