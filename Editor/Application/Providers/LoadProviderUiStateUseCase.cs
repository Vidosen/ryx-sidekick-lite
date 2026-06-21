// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Providers
{
    internal sealed class LoadProviderUiStateUseCase
    {
        private readonly ISettingsStore _settingsStore;
        private readonly IProviderCatalog _providerCatalog;

        public LoadProviderUiStateUseCase(ISettingsStore settingsStore, IProviderCatalog providerCatalog)
        {
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            _providerCatalog = providerCatalog ?? throw new ArgumentNullException(nameof(providerCatalog));
        }

        /// <summary>
        /// Returns an <see cref="ActiveProviderStateSnapshot"/> for the given provider id by consulting
        /// <see cref="ISettingsStore.GetProviderUiState"/> and falling back to
        /// <see cref="IProviderUiMetadata"/> catalog defaults when no persisted entry exists.
        /// </summary>
        public ActiveProviderStateSnapshot Execute(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return null;
            }

            var persisted = _settingsStore.GetProviderUiState(providerId);
            if (persisted != null)
            {
                return BuildFromSnapshot(persisted, providerId);
            }

            // Fall back to catalog metadata defaults.
            var module = _providerCatalog.GetProvider(providerId);
            var metadata = module?.Metadata;

            var defaultModel = metadata?.DefaultModel ?? string.Empty;
            var defaultEffort = metadata?.FallbackModelCatalog?.Models?
                .FirstOrDefault(model => string.Equals(model?.Id, defaultModel, StringComparison.Ordinal))?
                .DefaultReasoningEffort ?? string.Empty;
            var defaultCollaboration = metadata?.CollaborationModes?.Length > 0
                ? metadata.CollaborationModes[0].Value
                : string.Empty;
            var defaultPermission = metadata?.GetPermissionModes(defaultCollaboration)?.Length > 0
                ? metadata.GetPermissionModes(defaultCollaboration)[0].Value
                : string.Empty;

            return new ActiveProviderStateSnapshot(
                providerId,
                defaultModel,
                defaultCollaboration,
                defaultPermission,
                defaultEffort);
        }

        private static ActiveProviderStateSnapshot BuildFromSnapshot(ProviderUiStateSnapshot snapshot, string fallbackProviderId)
        {
            var id = !string.IsNullOrWhiteSpace(snapshot.ProviderId)
                ? snapshot.ProviderId
                : fallbackProviderId ?? string.Empty;

            return new ActiveProviderStateSnapshot(
                id,
                snapshot.Model,
                snapshot.CollaborationMode,
                snapshot.PermissionMode,
                snapshot.ReasoningEffort);
        }
    }
}
