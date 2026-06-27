// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Distinguishes a <b>domain-reload teardown</b> from a <b>user-initiated teardown</b> for the
    /// dispose chain (Phase 3).
    ///
    /// <para>
    /// Background: a Unity domain reload tears the window down via
    /// <c>beforeAssemblyReload</c> → <c>SidekickWindow.OnDisable</c> → <c>SidekickEditorAppHost.Dispose</c>
    /// → <c>ProcessManager.Dispose</c> → <c>Stop()</c>, and <c>Stop()</c> force-kills the process. For a
    /// <see cref="RemoteProcessHost"/> that would kill the daemon-owned child and defeat the entire Agent
    /// Host feature (the child must survive the reload so the next domain can re-attach).
    /// </para>
    ///
    /// <para>
    /// Mechanism: <c>DomainReloadAutoResume.OnBeforeAssemblyReload</c> fires <b>before</b>
    /// <c>OnDisable</c> on a reload, and sets <see cref="IsReloadTeardownInProgress"/>. The subsequent
    /// dispose chain then runs while the flag is set, so <see cref="RemoteProcessHost"/> performs a
    /// <c>Detach()</c> (close the socket, leave the daemon + child alive) instead of <c>STOP</c>. A real
    /// window close / explicit user stop happens WITHOUT a prior <c>beforeAssemblyReload</c>, so the flag
    /// is clear and the host still sends <c>STOP</c> (kill). The flag is a process-static in the
    /// Infrastructure layer so <see cref="RemoteProcessHost"/> can read it without referencing
    /// Presentation (which owns the reload orchestration and is one layer up).
    /// </para>
    ///
    /// <para>
    /// Self-clearing across the reload edge: statics do not survive a domain reload, so the flag is
    /// naturally <c>false</c> in the new domain. It is also explicitly cleared in
    /// <c>OnAfterAssemblyReload</c> for the (harmless) same-domain edge cases and to keep tests
    /// deterministic.
    /// </para>
    /// </summary>
    internal static class AgentHostReloadCoordinator
    {
        // volatile: set on the main thread in OnBeforeAssemblyReload, read on the main thread during the
        // dispose chain. No background thread reads it, but volatile keeps the contract explicit.
        private static volatile bool _reloadTeardownInProgress;

        /// <summary>
        /// True while a domain-reload teardown is in flight. Reconnectable hosts detach (keep the daemon
        /// child alive) rather than stop (kill) while this is set.
        /// </summary>
        public static bool IsReloadTeardownInProgress
        {
            get => _reloadTeardownInProgress;
            set => _reloadTeardownInProgress = value;
        }
    }
}
