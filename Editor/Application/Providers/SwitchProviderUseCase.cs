// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Providers
{
    internal sealed class SwitchProviderRequest
    {
        public string TargetProviderId { get; set; }
        public string CurrentProviderId { get; set; }
        public bool IsTurnInProgress { get; set; }
        public bool SynchronizeSettings { get; set; } = true;
        public ProviderDraftSnapshot OutgoingDraftSnapshot { get; set; }
        public ActiveProviderStateSnapshot OutgoingProviderState { get; set; }
        public ActiveProviderStateSnapshot ExternalAcceptedState { get; set; }
    }

    internal enum SwitchProviderStatus
    {
        NoOpSameProvider,
        BlockedByActiveTurn,
        InvalidProvider,
        Proceed
    }

    internal sealed class SwitchProviderResult
    {
        public SwitchProviderStatus Status { get; set; }
        public ActiveProviderStateSnapshot ResolvedProviderState { get; set; }
        public ProviderUiStateSnapshot PersistedOutgoingSnapshot { get; set; }
        public string RejectionDialogTitle { get; set; }
        public string RejectionDialogMessage { get; set; }
    }

    internal sealed class SwitchProviderUseCase
    {
        private readonly IProviderCatalog _providerCatalog;
        private readonly LoadProviderUiStateUseCase _loadProviderUiState;
        private readonly SaveProviderUiStateUseCase _saveProviderUiState;

        public SwitchProviderUseCase(
            IProviderCatalog providerCatalog,
            LoadProviderUiStateUseCase loadProviderUiState,
            SaveProviderUiStateUseCase saveProviderUiState)
        {
            _providerCatalog = providerCatalog ?? throw new ArgumentNullException(nameof(providerCatalog));
            _loadProviderUiState = loadProviderUiState ?? throw new ArgumentNullException(nameof(loadProviderUiState));
            _saveProviderUiState = saveProviderUiState ?? throw new ArgumentNullException(nameof(saveProviderUiState));
        }

        public SwitchProviderResult Execute(SwitchProviderRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var targetId = request.TargetProviderId;
            var currentId = request.CurrentProviderId;

            // Guard: null/empty/whitespace target or same provider — no-op.
            if (string.IsNullOrWhiteSpace(targetId) || string.Equals(targetId, currentId, StringComparison.Ordinal))
            {
                return new SwitchProviderResult { Status = SwitchProviderStatus.NoOpSameProvider };
            }

            // Guard: unknown provider (non-empty id that catalog doesn't know).
            if (_providerCatalog.GetProvider(targetId) == null)
            {
                return new SwitchProviderResult { Status = SwitchProviderStatus.InvalidProvider };
            }

            // Guard: turn in progress — reject.
            if (request.IsTurnInProgress)
            {
                return new SwitchProviderResult
                {
                    Status = SwitchProviderStatus.BlockedByActiveTurn,
                    RejectionDialogTitle = "Stop Current Turn",
                    RejectionDialogMessage = "Stop the current turn before switching providers."
                };
            }

            // Persist outgoing provider state before switching.
            var persistedOutgoing = _saveProviderUiState.Execute(
                request.OutgoingProviderState,
                request.OutgoingDraftSnapshot,
                fallbackSnapshot: null);

            // Resolve next provider state.
            ActiveProviderStateSnapshot resolvedState;
            if (!request.SynchronizeSettings && request.ExternalAcceptedState != null)
            {
                // External path (settings-driven): the caller already has the accepted snapshot.
                resolvedState = request.ExternalAcceptedState;
            }
            else
            {
                // UI-driven path: resolve the persisted state for the target provider.
                // The actual ProviderId write to ISettingsStore is deferred to the host's
                // RebuildProviderScope so it is protected by the _suppressSettingsSync gate.
                resolvedState = _loadProviderUiState.Execute(targetId);
            }

            return new SwitchProviderResult
            {
                Status = SwitchProviderStatus.Proceed,
                ResolvedProviderState = resolvedState,
                PersistedOutgoingSnapshot = persistedOutgoing
            };
        }
    }
}
