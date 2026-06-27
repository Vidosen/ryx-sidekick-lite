// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    /// <summary>
    /// Pure, Editor-static-free decision logic for what to do after a domain reload when a turn was
    /// active (Phase 3): try the Agent Host attach+replay path first, and fall back to the existing
    /// synthetic <c>-r</c> resume only when attach is impossible. Extracted from
    /// <c>DomainReloadAutoResume</c> (which is an <c>[InitializeOnLoad]</c> static glued to Unity
    /// statics) so this branch is unit-testable with a fake resume store and a fake window host.
    /// </summary>
    internal static class DomainReloadReconnectCoordinator
    {
        internal enum ResumeOutcome
        {
            /// <summary>Re-attached to the surviving daemon session; NO synthetic prompt was sent.</summary>
            Reattached,

            /// <summary>No reconnect keys / attach failed; fell back to the synthetic <c>-r</c> resume.</summary>
            FellBackToResume
        }

        /// <summary>
        /// Decides and performs the post-reload resume for one host. When durable reconnect keys were
        /// persisted for <paramref name="hostToken"/> AND the host re-attaches successfully, the
        /// surviving turn keeps streaming with no synthetic prompt. Otherwise the lossy resume prompt
        /// is sent (zero behavior change from before Phase 3). The reconnect keys are always cleared
        /// afterwards so a later cold open never replays a stale attach.
        /// </summary>
        internal static ResumeOutcome Resume(
            IResumeStateStore resumeStateStore,
            ISidekickWindowHost host,
            string hostToken,
            string providerId,
            string sessionId)
        {
            if (resumeStateStore != null
                && resumeStateStore.TryGetAgentHostReconnect(hostToken, out var sessionHandle, out var lastDurableSeq)
                && !string.IsNullOrEmpty(sessionHandle)
                && host != null
                && host.TryReattachAfterDomainReload(providerId, sessionId, sessionHandle, lastDurableSeq))
            {
                resumeStateStore.ClearAgentHostReconnect(hostToken);
                return ResumeOutcome.Reattached;
            }

            resumeStateStore?.ClearAgentHostReconnect(hostToken);
            host?.AutoResumeAfterDomainReload(providerId, sessionId);
            return ResumeOutcome.FellBackToResume;
        }
    }
}
