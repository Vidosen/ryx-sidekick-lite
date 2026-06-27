// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Unity-side mirror of the Agent Host wire vocabulary. Kept in lock-step with the daemon's
    /// <c>Ryx.Sidekick.AgentHost.Protocol.MessageTypes</c> / <c>StreamNames</c> / <c>DaemonInfo</c>.
    /// The daemon is a separate <c>~</c>-ignored assembly Unity cannot reference, so these constants
    /// are duplicated here deliberately — they MUST match the daemon byte-for-byte.
    /// </summary>
    internal static class WireProtocol
    {
        /// <summary>Wire protocol version sent in <c>HELLO.proto</c>; must equal the daemon's <c>DaemonInfo.Protocol</c>.</summary>
        public const int ProtocolVersion = 1;

        // client -> daemon
        public const string Hello = "HELLO";
        public const string Start = "START";
        public const string Write = "WRITE";
        public const string CloseStdin = "CLOSE_STDIN";
        public const string Stop = "STOP";
        public const string Interrupt = "INTERRUPT";
        public const string Attach = "ATTACH";
        public const string Trim = "TRIM";
        public const string Ping = "PING";
        public const string Shutdown = "SHUTDOWN";

        // daemon -> client
        public const string HelloOk = "HELLO_OK";
        public const string Started = "STARTED";
        public const string Output = "OUTPUT";
        public const string Exited = "EXITED";
        public const string ReplayTruncated = "REPLAY_TRUNCATED";
        public const string Pong = "PONG";
        public const string Error = "ERROR";

        // OUTPUT.stream values
        public const string StreamStdout = "stdout";
        public const string StreamStderr = "stderr";
    }
}
