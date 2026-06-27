// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Creates the concrete <see cref="IProcessHost"/> backing a runtime: the out-of-process
    /// <see cref="RemoteProcessHost"/> when the Agent Host feature is enabled and a daemon is
    /// reachable, otherwise the in-process <see cref="CliProcessHost"/> (the existing behavior and
    /// the safe fallback).
    /// </summary>
    internal interface IProcessHostFactory
    {
        IProcessHost Create();
    }

    /// <summary>
    /// Default factory. Returns a <see cref="RemoteProcessHost"/> only when BOTH
    /// <see cref="SidekickSettings.UseAgentHost"/> is true AND the injected
    /// <see cref="IAgentHostConnector"/> resolves a daemon endpoint; otherwise it returns the
    /// in-process <see cref="CliProcessHost"/>.
    ///
    /// <para>
    /// Registered in DI (<c>SidekickServiceRegistry</c>) with the configured connector injected. When
    /// constructed WITHOUT DI and no connector (e.g. the <c>?? new DefaultProcessHostFactory()</c>
    /// fallbacks in <c>ProcessManager</c> / <c>ClaudePersistentSessionClient</c>), it defaults to
    /// <see cref="UnavailableAgentHostConnector"/> and so behaves exactly like today — in-process —
    /// until Phase 4 swaps in a real discovery/launch connector. This is what guarantees
    /// "flag OFF (default) ⇒ zero production behavior change".
    /// </para>
    /// </summary>
    internal sealed class DefaultProcessHostFactory : IProcessHostFactory
    {
        private readonly IAgentHostConnector _connector;

        // SINGLE public constructor with an optional parameter — deliberate. App UI's ServiceProvider
        // picks the first constructor whose parameters are all registered; a separate parameterless
        // ctor would be selected first (reflection order is not guaranteed) and the connector would
        // never be injected. With one ctor: DI injects the registered IAgentHostConnector, while the
        // non-DI fallbacks (`new DefaultProcessHostFactory()`) omit it and get the safe Unavailable stub.
        public DefaultProcessHostFactory(IAgentHostConnector connector = null)
        {
            _connector = connector ?? new UnavailableAgentHostConnector();
        }

        public IProcessHost Create()
        {
            if (!SidekickSettings.instance.UseAgentHost)
            {
                // Feature off (the default): in-process host, zero behavior change.
                return new CliProcessHost();
            }

            // The non-DI safety-net ctor (`new DefaultProcessHostFactory()`) carries the
            // UnavailableAgentHostConnector stub: daemon capability was never composed into this factory
            // (a STRUCTURAL state, not a genuine daemon failure). Keep the documented "non-DI fallback
            // behaves like today" contract and stay in-process. In production the DI factory always
            // carries the real AgentHostConnector, so this branch is not reached with the feature on.
            if (_connector is UnavailableAgentHostConnector)
            {
                return new CliProcessHost();
            }

            if (_connector.TryConnect(out var endpoint) && endpoint.IsValid)
            {
                // The connector already proved an endpoint; RemoteProcessHost re-resolves it via the
                // same connector when it actually connects (cheap, and keeps a single source of truth).
                return new RemoteProcessHost(_connector);
            }

            // 'Use Agent Host' is ON but the daemon could not be established. The connector has already
            // logged the specific reason ([AgentHost] error: missing payload / no bundled runtime /
            // spawn failure / port timeout). Per the "no silent mines" rule we do NOT quietly downgrade
            // to the in-process host — that would mask a real failure and make the toggle lie. Return a
            // host that fails the turn loudly and actionably instead; the user fixes the cause or turns
            // the feature off.
            return new FailedAgentHostProcessHost();
        }
    }

    /// <summary>
    /// The <see cref="IProcessHost"/> the factory returns when <see cref="SidekickSettings.UseAgentHost"/>
    /// is ON but the Agent Host daemon could not be established. It deliberately does NOT run the CLI
    /// in-process: a silent in-process downgrade would mask a real failure and make the toggle lie
    /// ("no silent mines"). Instead the first attempt to start a turn fails loudly and actionably (the
    /// connector has already logged the specific root cause as an <c>[AgentHost]</c> error). The user
    /// either fixes the cause or turns the feature off in Project Settings → Sidekick → General.
    /// </summary>
    internal sealed class FailedAgentHostProcessHost : IProcessHost
    {
#pragma warning disable CS0067 // Events are part of the IProcessHost contract; this host only ever fails.
        public event Action<string> OnOutputLine;
        public event Action<string> OnErrorLine;
        public event Action OnProcessStarted;
        public event Action<int> OnProcessExited;
#pragma warning restore CS0067

        public bool IsRunning => false;
        public bool IsStdinOpen => false;

        public bool StartStreaming(string arguments)
        {
            const string message =
                "Agent Host is enabled but its daemon could not be started — see the [AgentHost] error " +
                "logged above for the specific cause. The turn was NOT silently run in-process. Fix the " +
                "cause, or turn off 'Use Agent Host' in Project Settings → Sidekick → General " +
                "to use the in-process CLI.";
            Debug.LogError("[AgentHost] " + message);
            // Surface it in the turn as well, so it is visible in the chat and not just the console.
            OnErrorLine?.Invoke(message);
            return false;
        }

        public bool WriteLineToStdin(string line) => false;
        public bool WriteToStdin(string text, bool appendNewLine = false) => false;
        public void CloseStdin() { }
        public bool TryCloseStdin() => false;
        public void Stop() { }
        public Task InterruptAsync() => Task.CompletedTask;
        public void Cleanup() { }
        public void Dispose() { }
    }
}
