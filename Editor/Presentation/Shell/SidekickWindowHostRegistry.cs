// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    internal static class SidekickWindowHostRegistry
    {
        private static readonly List<ISidekickWindowHost> Hosts = new();

        public static void Register(ISidekickWindowHost host)
        {
            if (host == null || Hosts.Contains(host))
                return;

            Hosts.Add(host);
        }

        public static void Unregister(ISidekickWindowHost host)
        {
            if (host == null)
                return;

            Hosts.Remove(host);
        }

        public static IReadOnlyList<ISidekickWindowHost> Snapshot()
        {
            return Hosts.Where(host => host != null).ToArray();
        }

        public static bool TryFindByHostToken(string hostToken, out ISidekickWindowHost host)
        {
            host = null;
            if (string.IsNullOrEmpty(hostToken))
                return false;

            foreach (var candidate in Snapshot())
            {
                if (string.Equals(candidate.HostToken, hostToken, StringComparison.Ordinal))
                {
                    host = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
