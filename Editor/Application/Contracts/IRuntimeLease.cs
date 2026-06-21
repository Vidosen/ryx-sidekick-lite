// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>
    /// Creates exclusive runtime leases for a provider scope. The lease manager
    /// is window-scoped today and a single lease at a time backs the active
    /// provider scope.
    /// </summary>
    internal interface IRuntimeLeaseManager
    {
        IRuntimeLease Acquire(
            IProviderModule providerModule,
            ISettingsStore settingsStore,
            ILogger logger,
            ISessionRuntimeClient sharedSessionRuntimeClient = null);
    }

    /// <summary>
    /// A single runtime lease owns one <see cref="IRuntimeOrchestrator"/>.
    /// Disposing the lease tears the runtime down.
    /// </summary>
    internal interface IRuntimeLease : IDisposable
    {
        IRuntimeOrchestrator Runtime { get; }
    }
}
