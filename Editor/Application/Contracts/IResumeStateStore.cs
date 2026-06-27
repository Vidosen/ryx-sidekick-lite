// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>
    /// Cross-domain-reload persistence for "pending resume" hints and the
    /// composer draft state. Implementations wrap the editor SessionState
    /// API in Infrastructure.
    /// </summary>
    internal interface IResumeStateStore
    {
        void SavePendingResume(string hostToken, string providerId, string sessionId);
        bool TryConsumePendingResume(out string hostToken, out string providerId, out string sessionId);
        void ClearPendingResume();
        void SaveInputFieldState(InputFieldState state);
        InputFieldState LoadAndClearInputFieldState();

        /// <summary>
        /// Persists the Agent Host reconnect keys for a host across a domain reload: the daemon
        /// <paramref name="sessionHandle"/> and the durable replay cursor <paramref name="lastDurableSeq"/>
        /// (the last seq the client can recover WITHOUT the daemon — a turn boundary, NOT last-delivered).
        /// Keyed by <paramref name="hostToken"/> so the next domain attaches the right session.
        /// </summary>
        void SaveAgentHostReconnect(string hostToken, string sessionHandle, long lastDurableSeq);

        /// <summary>
        /// Reads (without clearing) the Agent Host reconnect keys for <paramref name="hostToken"/>.
        /// Returns false when no reconnect snapshot was stored (or the handle is empty), in which case
        /// the caller uses the lossy <c>-r</c> resume.
        /// </summary>
        bool TryGetAgentHostReconnect(string hostToken, out string sessionHandle, out long lastDurableSeq);

        /// <summary>Clears the Agent Host reconnect keys for <paramref name="hostToken"/>.</summary>
        void ClearAgentHostReconnect(string hostToken);
    }
}
