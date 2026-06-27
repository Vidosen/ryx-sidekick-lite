// SPDX-License-Identifier: GPL-3.0-only
using System;
using UnityEditor;
using Ryx.Sidekick.Editor.Infrastructure.Net;
using Ryx.Sidekick.Editor.Infrastructure.Pro;
using Ryx.Sidekick.Editor.Infrastructure.UnityEditor;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Pro;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Composes the B4 "Recommended for Unity" section for the MCP Project Settings page and registers it.
    /// The settings page runs OUTSIDE the App UI DI host, so this is a deliberate, documented [InitializeOnLoad]
    /// static (deviates from CLAUDE.md "no new global statics" — justified: SettingsProvider instantiation is
    /// Unity-owned; precedent: SidekickSettings.instance, CliProviderRegistry). The window keeps its own
    /// DI-scoped remote-config instances; the file-backed cache is the shared meeting point.
    ///
    /// LAZY: composing the source touches the file cache + baked Resources, and RefreshAsync hits the network.
    /// None of that may happen at domain load (B4 Requirement 4) — the Lazy defers it all to the first Build.
    /// </summary>
    [InitializeOnLoad]
    internal static class McpSettingsServicesBootstrap
    {
        // One shared remote-config source for every settings-page consumer (recommendations + Pro upsell);
        // composing it touches the file cache + baked Resources, so it stays behind a Lazy and only
        // materializes when a section is first built (page opened) — never at [InitializeOnLoad] time.
        private static readonly Lazy<IRemoteConfigSource> Source = new Lazy<IRemoteConfigSource>(ComposeSource);

        static McpSettingsServicesBootstrap() => EnsureRegistered();

        private static IRemoteConfigSource ComposeSource() => RemoteConfigSourceFactory.Create();

        // Idempotent (RegisterPermanent is replace-by-Id) so repeated domain loads / test calls keep ONE slot each.
        internal static void EnsureRegistered()
        {
            McpSettingsSectionRegistry.RegisterPermanent(
                new McpRecommendationsSection(() => new GetMcpRecommendationsQuery(Source.Value), new UnityExternalUrlOpener()));
            // The upsell CTA routes into the in-app paywall (ProPaywallLauncher), so it needs no offer/url deps.
            McpSettingsSectionRegistry.RegisterPermanent(new McpExternalServersUpsellSection());
        }
    }
}
