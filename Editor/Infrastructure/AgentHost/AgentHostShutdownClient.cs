// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Sends a one-shot <c>HELLO</c> + <c>SHUTDOWN</c> to a daemon over a short-lived loopback socket so
    /// it stops its children and exits immediately instead of waiting out its grace window. Used on a
    /// clean Editor quit (NOT a domain reload — see <see cref="AgentHostEndpointRegistry"/>).
    ///
    /// <para>
    /// The flow mirrors the probe handshake in <see cref="AgentHostConnector"/>: connect, write the
    /// authenticated HELLO (the daemon validates token + proto), then write the SHUTDOWN frame and
    /// close. We do not wait for the daemon to ack — SHUTDOWN is fire-and-forget and the daemon closes
    /// the socket as it stops. Every step is bounded and exception-safe so a quit is never delayed or
    /// blocked by a missing/dead daemon.
    /// </para>
    /// </summary>
    internal static class AgentHostShutdownClient
    {
        private const int ConnectTimeoutMs = 500;
        private const int IoTimeoutMs = 500;

        /// <summary>
        /// Best-effort: open a socket to <paramref name="endpoint"/>, HELLO, send SHUTDOWN, close.
        /// Returns true if the SHUTDOWN frame was written (the daemon was reachable + authenticated),
        /// false otherwise. Never throws.
        /// </summary>
        public static bool TrySendShutdown(in AgentHostEndpoint endpoint, Action<string> logError = null)
        {
            if (!endpoint.IsValid)
                return false;

            TcpClient client = null;
            try
            {
                client = new TcpClient { NoDelay = true };
                var connect = client.BeginConnect(endpoint.Host, endpoint.Port, null, null);
                if (!connect.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(ConnectTimeoutMs)))
                    return false;
                client.EndConnect(connect);

                using var stream = client.GetStream();
                stream.ReadTimeout = IoTimeoutMs;
                stream.WriteTimeout = IoTimeoutMs;

                // HELLO so the daemon authenticates us (token + matching proto) before accepting SHUTDOWN.
                var hello = new JObject
                {
                    ["t"] = WireProtocol.Hello,
                    ["token"] = endpoint.Token ?? string.Empty,
                    ["proto"] = WireProtocol.ProtocolVersion,
                    ["ownerPid"] = endpoint.OwnerPid,
                };
                if (!WriteLine(stream, hello))
                    return false;

                // Drain the HELLO_OK (or ERROR) so we do not race the daemon's reader; ignore content —
                // even on an ERROR (proto mismatch) we just close, and a mismatched daemon is not ours
                // to shut down anyway. A short bounded read keeps quit snappy.
                TryReadLine(stream);

                var shutdown = new JObject { ["t"] = WireProtocol.Shutdown };
                return WriteLine(stream, shutdown);
            }
            catch (Exception ex)
            {
                logError?.Invoke($"[AgentHost] Clean-quit SHUTDOWN to {endpoint.Host}:{endpoint.Port} failed: {ex.Message}");
                return false;
            }
            finally
            {
                try { client?.Close(); } catch { /* ignore */ }
            }
        }

        private static bool WriteLine(NetworkStream stream, JObject frame)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(frame.ToString(Formatting.None) + "\n");
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryReadLine(NetworkStream stream)
        {
            try
            {
                using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 1024, leaveOpen: true);
                reader.ReadLine();
            }
            catch { /* ignore — SHUTDOWN is fire-and-forget */ }
        }
    }
}
