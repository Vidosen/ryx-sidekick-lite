// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>
    /// Bootstrap state of a persistent session backend (e.g. the Cursor ACP
    /// client) so the shell can show progress / failure UI before the runtime
    /// becomes usable.
    /// </summary>
    internal enum PersistentSessionBootstrapState
    {
        Uninitialized,
        Initializing,
        Ready,
        Failed
    }

    /// <summary>
    /// Backing provider session that lives across multiple turns and owns the
    /// JSON-RPC / persistent runtime client. Implemented by providers (Cursor,
    /// Codex) that don't fit the spawn-per-turn CLI model.
    /// </summary>
    internal interface IPersistentSessionBackend : IDisposable
    {
        event Action<PersistentSessionBootstrapState> BootstrapStateChanged;

        PersistentSessionBootstrapState BootstrapState { get; }
        string BootstrapErrorMessage { get; }
        ISessionRuntimeClient RuntimeClient { get; }

        Task EnsureInitializedAsync();
        void DetachActiveConversationState();
    }
}
