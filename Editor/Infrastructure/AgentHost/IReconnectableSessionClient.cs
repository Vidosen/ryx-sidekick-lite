// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Infrastructure-side sibling capability for an <c>ISessionRuntimeClient</c> whose underlying
    /// process is owned by the Agent Host daemon (Phase 3). Lets <c>ProcessManager</c> drive
    /// detach / re-attach / durable-trim on the persistent-session client's hidden process host
    /// WITHOUT widening the Domain-layer <c>ISessionRuntimeClient</c> contract (which would force
    /// every test fake to implement reconnect).
    ///
    /// <para>
    /// <c>ProcessManager</c> probes <c>_sessionRuntimeClient as IReconnectableSessionClient</c>; a
    /// client that does not implement it (or that is backed by the in-process <c>CliProcessHost</c>)
    /// simply reports no handle, so the reload orchestration falls back to the <c>-r</c> resume.
    /// </para>
    /// </summary>
    internal interface IReconnectableSessionClient
    {
        /// <summary>Daemon session handle backing the client's process host, or empty when in-process.</summary>
        string SessionHandle { get; }

        /// <summary>Highest output seq the client's host has observed (live + replayed).</summary>
        long LastObservedSequence { get; }

        /// <summary>
        /// True when the client's process host is the reconnectable (daemon) host — i.e. re-attach is
        /// possible after a reload. False for the in-process host.
        /// </summary>
        bool SupportsReattach { get; }

        /// <summary>
        /// Re-attach the client's host to an existing daemon session and replay buffered output with
        /// <c>seq &gt; afterDurableSeq</c>. Re-primes the parser so replay rebuilds the in-flight turn
        /// idempotently, and arms a fresh turn-completion task so the caller can observe the in-flight
        /// turn the same way it observes a normal start. Returns a started
        /// <see cref="Ryx.Sidekick.Editor.Providers.PersistentTurnStartAck"/> on success, or
        /// <c>null</c> on a missing session / <c>REPLAY_TRUNCATED</c> (caller falls back to <c>-r</c>).
        /// </summary>
        Ryx.Sidekick.Editor.Providers.PersistentTurnStartAck TryReattach(string sessionHandle, long afterDurableSeq);

        /// <summary>Forward a durable-trim to the daemon (turn-boundary only). No-op when in-process.</summary>
        void SendTrim(long safeSeq);
    }
}
