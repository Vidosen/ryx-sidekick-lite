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
    }
}
