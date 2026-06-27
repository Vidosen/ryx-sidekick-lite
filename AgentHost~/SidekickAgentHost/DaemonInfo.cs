// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.AgentHost
{
    /// <summary>Daemon identity advertised in the HELLO_OK handshake.</summary>
    internal static class DaemonInfo
    {
        public const string Version = "0.1.2";

        /// <summary>
        /// Wire protocol version. The Unity client must send a matching
        /// <c>proto</c> in HELLO; a mismatch is rejected so a stale daemon
        /// from an older plugin build is drained and replaced (plan risk #7).
        /// </summary>
        public const int Protocol = 1;
    }
}
