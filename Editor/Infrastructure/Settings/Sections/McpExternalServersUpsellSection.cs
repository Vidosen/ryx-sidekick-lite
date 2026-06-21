// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Infrastructure.Pro;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Pro;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Lite-only Pro-upsell teaser occupying the "external servers" slot (Order 30) of the MCP page.
    /// Live MCP server status + tool/resource browsing is a Pro feature with no introspection code in Lite
    /// (B5 boundary correction) — so Lite shows only this teaser. When Sidekick Pro is installed it registers
    /// its real <c>McpExternalServersSection</c> (its own Id) AND this teaser self-hides via <see cref="IProPresence"/>,
    /// so the two never both render (independent of [InitializeOnLoad] order between the Lite and Pro bootstraps).
    /// The CTA routes into the existing in-app paywall (which owns the sale funnel) via <see cref="ProPaywallLauncher"/>,
    /// rather than jumping straight to a store URL.
    /// </summary>
    internal sealed class McpExternalServersUpsellSection : IMcpSettingsSection
    {
        private readonly IProPresence _proPresence;
        private readonly IProEntitlement _entitlement;
        private readonly Action _onUpgrade;

        public string Id => "mcp-external-servers-upsell";
        public int Order => 30;

        public McpExternalServersUpsellSection(
            IProPresence proPresence = null,
            Action onUpgrade = null,
            IProEntitlement entitlement = null)
        {
            _proPresence = proPresence ?? new SidekickProPresence();
            _entitlement = entitlement ?? new LicenseProEntitlement(
                new Ryx.Sidekick.Editor.Infrastructure.Licensing.SettingsEntitlementCache(),
                new Ryx.Sidekick.Editor.Infrastructure.Entitlements.DefaultEntitlementVerifier(),
                new Ryx.Sidekick.Editor.Infrastructure.SystemClock());
            // Default: route into the in-app paywall (the sale funnel), highlighting the MCP feature.
            _onUpgrade = onUpgrade ?? (() => ProPaywallLauncher.Request("mcp-management"));
        }

        public VisualElement Build(McpSettingsSectionContext ctx)
        {
            var root = new VisualElement();

            // Pro present → its live inventory section renders instead; the teaser must not show.
            if (_proPresence.IsInstalled)
                return root;

            root.AddToClassList("sk-mcpset-group");
            root.AddToClassList("sk-mcpset-pro-group");

            var pill = new Label("RYX SIDEKICK PRO");
            pill.AddToClassList("sk-mcpset-pro-pill");
            root.Add(pill);

            root.Add(SidekickSettingsSectionBuilder.SectionHeader("Live MCP server status & tools"));

            var body = new Label(
                "See what each CLI actually loads — live server status, tools and resources — in Sidekick Pro.");
            body.AddToClassList("sk-mcpset-reco-subtext");
            body.style.whiteSpace = WhiteSpace.Normal;
            root.Add(body);

            // Route into the in-app paywall (the existing sale funnel) via the injected action.
            // An entitled user (owns Pro, not yet installed) sees "Install Pro"; everyone else "Upgrade to Pro".
            var ctaText = _entitlement.Get().OwnsPro ? "Install Pro" : "Upgrade to Pro";
            var cta = new Button(() => _onUpgrade()) { text = ctaText };
            cta.AddToClassList("sk-mcpset-pro-cta");
            cta.style.alignSelf = Align.FlexStart;
            cta.style.marginTop = 8;
            root.Add(cta);

            return root;
        }
    }
}
