// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Post-construction seam for routing the <see cref="IProcessHostFactory"/> into an
    /// <c>ISessionRuntimeClient</c> that a provider created via the parameterless, Domain-layer
    /// <c>ICliProvider.CreateSessionRuntimeClient()</c> (which cannot take the Infrastructure factory).
    ///
    /// <para>
    /// Why this exists: <c>ICliProvider</c> lives in the Domain asmdef, which must not reference the
    /// Infrastructure <see cref="IProcessHostFactory"/>; so its <c>CreateSessionRuntimeClient()</c>
    /// stays parameterless. But <c>ProcessManager</c> (Infrastructure) <i>does</i> hold the factory.
    /// After it obtains the client from the provider, it calls
    /// <see cref="SetProcessHostFactory"/> so a daemon-capable client (e.g.
    /// <c>ClaudePersistentSessionClient</c>) rebuilds its process host through the factory — which,
    /// with <c>UseAgentHost</c> ON and a reachable daemon, is a <see cref="RemoteProcessHost"/>.
    /// </para>
    ///
    /// <para>
    /// The contract is: <see cref="SetProcessHostFactory"/> is called <b>before any turn starts</b>
    /// (immediately after construction), while the host has not yet launched a process, so swapping
    /// the host is safe. Implementations must no-op if a real host is already explicitly injected (the
    /// test ctor path) or once a process has started.
    /// </para>
    /// </summary>
    internal interface IProcessHostFactoryAware
    {
        void SetProcessHostFactory(IProcessHostFactory factory);
    }
}
