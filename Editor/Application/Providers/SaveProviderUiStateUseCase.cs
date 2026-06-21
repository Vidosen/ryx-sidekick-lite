// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Providers
{
    internal sealed class SaveProviderUiStateUseCase
    {
        private readonly ISettingsStore _settingsStore;

        public SaveProviderUiStateUseCase(ISettingsStore settingsStore)
        {
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        }

        /// <summary>
        /// Builds a <see cref="ProviderUiStateSnapshot"/> from the current active provider state,
        /// merges in the selected session id from the draft (falling back to the cached fallback snapshot),
        /// persists it via <see cref="ISettingsStore.SaveProviderUiState"/>, and returns the saved snapshot.
        /// Returns <c>null</c> (and performs no write) when <paramref name="providerState"/> is null or has no provider id.
        /// </summary>
        public ProviderUiStateSnapshot Execute(
            ActiveProviderStateSnapshot providerState,
            ProviderDraftSnapshot draftSnapshot,
            ProviderUiStateSnapshot fallbackSnapshot)
        {
            if (providerState == null || string.IsNullOrWhiteSpace(providerState.ProviderId))
            {
                return null;
            }

            var snapshot = new ProviderUiStateSnapshot
            {
                ProviderId = providerState.ProviderId,
                SelectedSessionId = !string.IsNullOrWhiteSpace(draftSnapshot?.SelectedSessionId)
                    ? draftSnapshot.SelectedSessionId
                    : fallbackSnapshot?.SelectedSessionId ?? string.Empty,
                Model = providerState.Model,
                ReasoningEffort = providerState.ReasoningEffort,
                CollaborationMode = providerState.CollaborationMode,
                PermissionMode = providerState.PermissionMode
            };

            _settingsStore.SaveProviderUiState(snapshot);
            return snapshot;
        }
    }
}
