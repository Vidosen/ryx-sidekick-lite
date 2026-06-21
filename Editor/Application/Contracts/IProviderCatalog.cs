// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>
    /// Read-only registry of available CLI providers (Claude, Cursor, Codex, ...).
    /// Used by switch-provider and provider-selector use cases.
    /// </summary>
    internal interface IProviderCatalog
    {
        IReadOnlyList<IProviderModule> AllProviders { get; }
        IProviderModule GetProvider(string id);
    }

    /// <summary>
    /// A single provider's static description — its UI metadata and a factory
    /// for the provider-scoped runtime/repository graph.
    /// </summary>
    internal interface IProviderModule
    {
        string Id { get; }
        IProviderUiMetadata Metadata { get; }
        IProviderScope CreateScope(ISettingsStore settingsStore, IRuntimeLeaseManager leaseManager, ILogger logger);
    }

    /// <summary>
    /// Provider scope wires together the runtime, conversation repository,
    /// session backend, and tool mapper for one active provider. Disposing the
    /// scope tears the runtime/lease/backend down together.
    /// </summary>
    internal interface IProviderScope : IDisposable
    {
        IRuntimeOrchestrator Runtime { get; }
        IConversationRepository Conversations { get; }
        IPersistentSessionBackend SessionBackend { get; }
        IProviderModelCatalogSource ModelCatalogSource => null;
        IProviderSlashCommandSource SlashCommandSource => null;
        IProviderToolMapper ToolMapper { get; }
        IProviderUiMetadata Metadata { get; }
    }
}
