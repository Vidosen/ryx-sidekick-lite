// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>
    /// Aggregates the live capability sources for a single provider scope.
    /// Disposing this object cancels any in-flight fetches and releases resources.
    /// </summary>
    internal interface IProviderCapabilitySources : IDisposable
    {
        /// <summary>Live source for the provider's available models.</summary>
        IProviderModelCatalogSource ModelCatalogSource { get; }

        /// <summary>Live source for the provider's available slash commands.</summary>
        IProviderSlashCommandSource SlashCommandSource { get; }
    }

    /// <summary>
    /// Factory implemented by CLI provider implementations that can supply live
    /// capability data (model catalog, slash commands) for a provider scope.
    /// Placed in Application/Contracts because the factory parameters are
    /// Application-layer types (<see cref="ISettingsStore"/>, <see cref="ILogger"/>).
    /// </summary>
    internal interface IProviderCapabilitySourcesFactory
    {
        /// <summary>
        /// Creates a new <see cref="IProviderCapabilitySources"/> bound to the given settings.
        /// The returned object is owned by the caller and must be disposed with the provider scope.
        /// </summary>
        IProviderCapabilitySources CreateCapabilitySources(ISettingsStore settingsStore, ILogger logger);
    }
}
