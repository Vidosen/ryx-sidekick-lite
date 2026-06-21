// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>
    /// Provider-implemented factory that constructs the persistent session
    /// backend (e.g. Cursor's ACP client) when the provider scope is created.
    /// </summary>
    internal interface IPersistentSessionBackendFactory
    {
        IPersistentSessionBackend CreateSessionBackend(ISettingsStore settingsStore, IProviderToolMapper toolMapper, ILogger logger);
    }
}
