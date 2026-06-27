// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Uniform reconnect surface over the two transports <c>ProcessManager</c> drives: the CliProcess
    /// path (whose host is the <see cref="IReconnectableProcessHost"/> directly) and the persistent
    /// session path (whose host is hidden behind <see cref="IReconnectableSessionClient"/>). Lets
    /// <c>ProcessManager</c> read the observed seq, send a durable TRIM, and re-attach without a
    /// per-transport branch at every call site.
    /// </summary>
    internal interface IReconnectableHostView
    {
        string SessionHandle { get; }
        long LastObservedSequence { get; }

        /// <summary>Forward a durable-trim floor to the daemon (turn-boundary only).</summary>
        void SendTrim(long safeSeq);

        /// <summary>
        /// Re-attach to the surviving daemon session and replay from <paramref name="afterDurableSeq"/>.
        /// For the persistent transport this returns a started turn ack (so the caller can observe the
        /// in-flight turn); for the CliProcess transport it returns a non-null sentinel ack on success
        /// and null on failure (CliProcess completion is driven by the parser's <c>result</c>, not a
        /// completion task).
        /// </summary>
        PersistentTurnStartAck TryReattach(string sessionHandle, long afterDurableSeq);
    }

    /// <summary>CliProcess-transport adapter over a raw <see cref="IReconnectableProcessHost"/>.</summary>
    internal sealed class ProcessHostReconnectableView : IReconnectableHostView
    {
        private readonly IReconnectableProcessHost _host;

        public ProcessHostReconnectableView(IReconnectableProcessHost host)
        {
            _host = host;
        }

        public string SessionHandle => _host.SessionHandle;
        public long LastObservedSequence => _host.LastObservedSequence;

        public void SendTrim(long safeSeq) => _host.SendTrim(safeSeq);

        public PersistentTurnStartAck TryReattach(string sessionHandle, long afterDurableSeq)
        {
            // CliProcess has no completion task — re-parsing the replayed `result` drives turn
            // completion. Return a "started, already-complete" ack as a success sentinel so the caller's
            // shared success/failure branch works; the caller does not observe this task for CliProcess.
            return _host.TryAttach(sessionHandle, afterDurableSeq)
                ? PersistentTurnStartAck.Started(sessionHandle, System.Threading.Tasks.Task.FromResult(true))
                : null;
        }
    }

    /// <summary>Persistent-session-transport adapter over <see cref="IReconnectableSessionClient"/>.</summary>
    internal sealed class SessionClientReconnectableView : IReconnectableHostView
    {
        private readonly IReconnectableSessionClient _client;

        public SessionClientReconnectableView(IReconnectableSessionClient client)
        {
            _client = client;
        }

        public string SessionHandle => _client.SessionHandle;
        public long LastObservedSequence => _client.LastObservedSequence;

        public void SendTrim(long safeSeq) => _client.SendTrim(safeSeq);

        public PersistentTurnStartAck TryReattach(string sessionHandle, long afterDurableSeq) =>
            _client.TryReattach(sessionHandle, afterDurableSeq);
    }
}
