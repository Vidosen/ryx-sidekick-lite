// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace Ryx.Sidekick.Editor.Presentation.State
{
    /// <summary>
    /// Which experience the paywall renders:
    /// <see cref="Buy"/> = upsell/purchase (not entitled); <see cref="Install"/> = one-click install
    /// for a user who already owns Pro but hasn't installed the package.
    /// </summary>
    internal enum PaywallMode
    {
        Buy,
        Install
    }

    internal readonly struct PaywallFeatureItem
    {
        public readonly string Id, DisplayName, Description, IconKey, Surface;
        public readonly bool IsHighlighted;

        public PaywallFeatureItem(string id, string displayName, string description, string iconKey, string surface, bool isHighlighted)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            IconKey = iconKey;
            Surface = surface;
            IsHighlighted = isHighlighted;
        }

        /// <summary>Provider features (Surface "provider:*") collapse into the engines hero card.</summary>
        public bool IsProvider =>
            !string.IsNullOrEmpty(Surface) && Surface.StartsWith("provider:", System.StringComparison.Ordinal);
    }

    internal readonly struct PaywallViewState
    {
        public readonly bool IsVisible;
        public readonly string Headline, Subhead, CtaLabel, Price, RequiresLiteVersion;
        public readonly bool CtaEnabled;
        public readonly IReadOnlyList<PaywallFeatureItem> Features;

        /// <summary>Buy (purchase) vs Install (owned, one-click install) presentation.</summary>
        public readonly PaywallMode Mode;

        /// <summary>Transient status line shown during an in-progress install (Install mode only).</summary>
        public readonly string InstallStatus;

        /// <summary>True while an install is running — the install button is disabled.</summary>
        public readonly bool InstallInProgress;

        public PaywallViewState(bool isVisible, string headline, string subhead, string ctaLabel,
            bool ctaEnabled, string price, string requiresLiteVersion, IReadOnlyList<PaywallFeatureItem> features,
            PaywallMode mode = PaywallMode.Buy, string installStatus = null, bool installInProgress = false)
        {
            IsVisible = isVisible;
            Headline = headline;
            Subhead = subhead;
            CtaLabel = ctaLabel;
            CtaEnabled = ctaEnabled;
            Price = price;
            RequiresLiteVersion = requiresLiteVersion;
            Features = features;
            Mode = mode;
            InstallStatus = installStatus;
            InstallInProgress = installInProgress;
        }

        public static readonly PaywallViewState Hidden =
            new PaywallViewState(false, null, null, null, false, null, null, System.Array.Empty<PaywallFeatureItem>());
    }
}
