// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Loopback endpoint of a running Sidekick Agent Host daemon: the TCP host/port plus
    /// the per-daemon auth token (and the owner pid the client advertises in HELLO).
    /// </summary>
    internal readonly struct AgentHostEndpoint
    {
        public AgentHostEndpoint(string host, int port, string token, int ownerPid)
        {
            Host = host;
            Port = port;
            Token = token;
            OwnerPid = ownerPid;
        }

        /// <summary>Loopback host the daemon is bound to (always <c>127.0.0.1</c> in practice).</summary>
        public string Host { get; }

        /// <summary>OS-assigned loopback port the daemon listener bound to.</summary>
        public int Port { get; }

        /// <summary>Per-daemon auth token (constant-time compared in the daemon HELLO).</summary>
        public string Token { get; }

        /// <summary>
        /// The Editor process id the client advertises to the daemon in <c>HELLO.ownerPid</c> so
        /// the daemon can self-terminate once the owning Editor dies and the grace window elapses.
        /// </summary>
        public int OwnerPid { get; }

        public bool IsValid => !string.IsNullOrEmpty(Host) && Port > 0;
    }

    /// <summary>
    /// Seam that resolves a connectable Agent Host daemon endpoint for the current project.
    ///
    /// <para>
    /// Phase 2 ships only this contract plus a trivial "unavailable" stub
    /// (<see cref="UnavailableAgentHostConnector"/>) so that turning on
    /// <see cref="SidekickSettings.UseAgentHost"/> with no daemon present still safely
    /// falls back to the in-process <see cref="CliProcessHost"/>. The REAL connector — which
    /// spawns the daemon via the bundled runtime, reads the per-project discovery files
    /// (<c>daemon.port</c>/<c>daemon.token</c>/<c>daemon.pid</c>), materializes the daemon DLL
    /// and waits for the port file — is Phase 4 work.
    /// </para>
    ///
    /// <para>EditMode tests supply a fake connector that points at an in-test
    /// <c>TcpListener</c> speaking the Phase 1 protocol.</para>
    /// </summary>
    internal interface IAgentHostConnector
    {
        /// <summary>
        /// Attempts to resolve (and, in Phase 4, ensure-launched) a daemon endpoint.
        /// Returns false when no daemon is available; the caller then uses the in-process host.
        /// </summary>
        bool TryConnect(out AgentHostEndpoint endpoint);
    }

    /// <summary>
    /// Phase 2 default connector: always reports "unavailable" so the factory falls back to the
    /// in-process <see cref="CliProcessHost"/>. This is what keeps the feature flag OFF-equivalent
    /// in production until the Phase 4 launcher/discovery connector replaces it in DI.
    /// </summary>
    internal sealed class UnavailableAgentHostConnector : IAgentHostConnector
    {
        public bool TryConnect(out AgentHostEndpoint endpoint)
        {
            endpoint = default;
            return false;
        }
    }
}
