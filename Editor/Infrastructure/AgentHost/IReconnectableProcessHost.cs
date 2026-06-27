// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// A <see cref="IProcessHost"/> whose underlying OS process is owned by an out-of-process
    /// Agent Host daemon and therefore survives a Unity domain reload. After reload the managed
    /// client is recreated and can re-attach to the still-alive session and replay its buffered
    /// output instead of spawning a fresh CLI.
    ///
    /// <para>
    /// This is intentionally a <b>sibling</b> interface (like <c>IPersistentTurnStarter</c> /
    /// <c>IRuntimeModeSwitch</c>): the small <see cref="IProcessHost"/> seam — and every existing
    /// in-process host and test fake that implements it — stays unchanged. Consumers that care
    /// about reconnect (Phase 3's <c>DomainReloadAutoResume</c>) probe for this interface via
    /// <c>as IReconnectableProcessHost</c>.
    /// </para>
    /// </summary>
    internal interface IReconnectableProcessHost : IProcessHost
    {
        /// <summary>
        /// Opaque per-session handle assigned by the daemon (its <c>STARTED.handle</c>). Empty until
        /// a session has been started or attached. Persisted by Phase 3 so the next domain can
        /// <see cref="TryAttach"/> the same session.
        /// </summary>
        string SessionHandle { get; }

        /// <summary>
        /// The highest output <c>seq</c> this host has SEEN/emitted so far (live + replayed),
        /// de-duplicated. This is the host's view of progress, NOT the durable replay cursor:
        /// the durable cursor (what the client can recover WITHOUT the daemon) is persisted by
        /// Phase 3 and advances only on turn boundaries.
        /// </summary>
        long LastObservedSequence { get; }

        /// <summary>
        /// Connect to an existing daemon session identified by <paramref name="sessionHandle"/> and
        /// replay buffered output with <c>seq &gt; afterSequence</c>, then resume live. Replayed
        /// lines are de-duplicated by seq (a line can appear in both the replay set and live
        /// stream). Returns false when the session is missing or the daemon responded
        /// <c>REPLAY_TRUNCATED</c> (requested seq below the durable floor) so the caller can fall
        /// back to a normal <c>-r</c> resume.
        /// </summary>
        bool TryAttach(string sessionHandle, long afterSequence);

        /// <summary>
        /// Detach from the daemon WITHOUT stopping the session: close the client socket only, leaving
        /// the daemon and its child process alive so the next domain can <see cref="TryAttach"/> the
        /// same session. This is the domain-reload teardown path — it must NOT send
        /// <c>STOP</c>/<c>INTERRUPT</c>/<c>SHUTDOWN</c>. (A user-initiated stop / real window close
        /// still goes through <c>Stop()</c>, which kills the child.) The daemon's own grace timer
        /// reclaims the orphaned session only if Unity never reconnects within the grace window.
        /// </summary>
        void Detach();

        /// <summary>
        /// Advance the daemon's durable-trim floor: tells the daemon that this client can recover up
        /// to <paramref name="safeSeq"/> WITHOUT the daemon (the turn is durably on disk), so its
        /// buffer below that seq may be discarded. Sent only at turn boundaries (see
        /// <c>ProcessManager</c>); NOT a "delivered" ack. No-op when not connected.
        /// </summary>
        void SendTrim(long safeSeq);
    }
}
