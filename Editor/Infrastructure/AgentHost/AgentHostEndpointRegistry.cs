// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Process-static registry of the live Agent Host daemon endpoints this Editor has resolved this
    /// session. Populated by <see cref="AgentHostConnector"/> when it successfully reuses-or-spawns a
    /// daemon; drained on a clean Editor quit so the daemon can stop its children immediately
    /// (<c>SHUTDOWN</c>) rather than waiting out its grace window.
    ///
    /// <para>
    /// This is deliberately a tiny process-static (not DI) because the clean-quit hook
    /// (<c>DomainReloadAutoResume.OnEditorQuitting</c>) is itself a static <c>[InitializeOnLoad]</c>
    /// member with no access to the per-window DI scope that owns the connector. It holds only loopback
    /// endpoints (host/port/token), never a live socket or a process handle, so nothing here keeps the
    /// daemon alive or survives the process. The connector is the single producer; the quit hook is the
    /// single consumer.
    /// </para>
    ///
    /// <para><b>Reload vs. quit.</b> A domain reload must NEVER touch this — the daemon child must
    /// survive a reload so the next domain re-attaches. Only a real Editor quit drains it (and that path
    /// is gated on <c>SidekickSettings.UseAgentHost</c> + presence of an endpoint, so it is a strict
    /// no-op when the feature is off).</para>
    /// </summary>
    internal static class AgentHostEndpointRegistry
    {
        private static readonly object Lock = new();
        // Keyed by "host:port" so re-resolving the same daemon (cache hit / second window) does not
        // register a duplicate. Latest token wins (a fresh spawn rewrote the token file).
        private static readonly Dictionary<string, AgentHostEndpoint> Endpoints = new();

        /// <summary>Record a resolved daemon endpoint as live for this Editor session.</summary>
        public static void Register(AgentHostEndpoint endpoint)
        {
            if (!endpoint.IsValid)
                return;

            lock (Lock)
            {
                Endpoints[Key(endpoint)] = endpoint;
            }
        }

        /// <summary>Forget a single endpoint (e.g. after a successful SHUTDOWN).</summary>
        public static void Remove(AgentHostEndpoint endpoint)
        {
            if (!endpoint.IsValid)
                return;

            lock (Lock)
            {
                Endpoints.Remove(Key(endpoint));
            }
        }

        /// <summary>Snapshot the currently-registered endpoints (safe to iterate without the lock).</summary>
        public static IReadOnlyList<AgentHostEndpoint> Snapshot()
        {
            lock (Lock)
            {
                return new List<AgentHostEndpoint>(Endpoints.Values);
            }
        }

        /// <summary>Drop all registered endpoints.</summary>
        public static void Clear()
        {
            lock (Lock)
            {
                Endpoints.Clear();
            }
        }

        private static string Key(in AgentHostEndpoint endpoint) => endpoint.Host + ":" + endpoint.Port;
    }
}
