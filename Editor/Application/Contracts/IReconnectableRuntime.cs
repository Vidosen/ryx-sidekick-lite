// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>
    /// Sibling capability of <see cref="IRuntimeOrchestrator"/> for runtimes whose underlying process
    /// is owned by the out-of-process Agent Host daemon and can therefore be re-attached after a Unity
    /// domain reload instead of being respawned (Phase 3).
    ///
    /// <para>
    /// This is intentionally a <b>sibling</b> interface — like <c>IReconnectableProcessHost</c> one
    /// layer down — so the broad <see cref="IRuntimeOrchestrator"/> contract (and every test fake that
    /// implements it) stays unchanged. The reload orchestration probes the active runtime via
    /// <c>as IReconnectableRuntime</c>; a runtime that does not implement it (or whose host is the
    /// in-process <c>CliProcessHost</c>) simply falls back to the existing <c>-r</c> resume.
    /// </para>
    ///
    /// <para>
    /// Pure Application contract: it carries only primitives, so it does not drag any Editor / UI type
    /// into the use-case layer (enforced by <c>ArchitectureBoundaryTests</c>).
    /// </para>
    /// </summary>
    internal interface IReconnectableRuntime
    {
        /// <summary>
        /// The opaque daemon session handle backing the live runtime, or empty when the runtime is not
        /// backed by a reconnectable (daemon) host. Snapshotted on <c>beforeAssemblyReload</c>.
        /// </summary>
        string SessionHandle { get; }

        /// <summary>
        /// The highest output sequence the live host has observed so far. This is NOT the replay
        /// cursor: the replay cursor (<see cref="LastDurableSequence"/>) is what the client can recover
        /// WITHOUT the daemon and advances only at turn boundaries. Exposed for diagnostics /
        /// completeness.
        /// </summary>
        long LastObservedSequence { get; }

        /// <summary>
        /// The replay cursor to hand to <see cref="TryReattach"/> after a reload: the last sequence
        /// this runtime can rebuild WITHOUT the daemon (the last completed turn boundary, durably on
        /// disk). Mid-turn this stays pinned at the in-flight turn's start so the daemon replays the
        /// whole in-flight turn.
        /// </summary>
        long LastDurableSequence { get; }

        /// <summary>
        /// Re-attach the freshly-recreated managed runtime to the still-alive daemon session
        /// <paramref name="sessionHandle"/> and replay buffered output with <c>seq &gt; afterDurableSeq</c>.
        /// The replayed lines are fed through a fresh parser so the in-flight turn's UI/state is rebuilt
        /// idempotently (the host de-dups by seq; the parser re-parse from the turn boundary is
        /// idempotent). Returns false when the session is gone or the daemon responded
        /// <c>REPLAY_TRUNCATED</c>, so the caller falls back to the synthetic <c>-r</c> resume.
        /// </summary>
        bool TryReattach(string sessionHandle, long afterDurableSeq);
    }
}
