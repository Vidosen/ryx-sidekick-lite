// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.UseCases.Pro;

namespace Ryx.Sidekick.Editor.Presentation.Presenters
{
    /// <summary>
    /// What the single status-bar chip currently represents — drives both its label and its click action.
    /// </summary>
    internal enum ProChipMode
    {
        Hidden,
        UpgradePro,   // Locked, no update → open the buy paywall
        InstallPro,   // OwnedNotInstalled → open the install paywall
        UpdateLite,   // Locked, a Lite update is available → free Lite update flow
        UpdatePro     // Installed, a Pro update is available → Pro update flow
    }

    internal readonly struct ProChipPresentation
    {
        public readonly ProChipMode Mode;
        public readonly string Label;
        public readonly bool Visible;

        public ProChipPresentation(ProChipMode mode, string label, bool visible)
        {
            Mode = mode;
            Label = label;
            Visible = visible;
        }
    }

    /// <summary>
    /// Pure decision for the status-bar chip. Priority: an available update outranks the Pro upsell
    /// (Update &gt; Install Pro &gt; Upgrade to Pro). Kept side-effect-free so the matrix is unit-tested
    /// without a live view or scope graph; <see cref="WindowViewBindingPresenter"/> applies the result.
    /// </summary>
    internal static class ProChipDecider
    {
        public const string UpgradeLabel = "★ Upgrade to Pro";
        public const string InstallLabel = "Install Pro";
        public const string UpdateLabel = "Update";

        public static ProChipPresentation Decide(ProAccessState access, bool liteUpdateAvailable, bool proUpdateAvailable)
        {
            switch (access)
            {
                case ProAccessState.Installed:
                    // Pro is installed: the chip only appears to offer a Pro update, otherwise it stays hidden.
                    return proUpdateAvailable
                        ? new ProChipPresentation(ProChipMode.UpdatePro, UpdateLabel, true)
                        : new ProChipPresentation(ProChipMode.Hidden, null, false);

                case ProAccessState.OwnedNotInstalled:
                    // Owns Pro but the package is absent: the install action takes precedence.
                    return new ProChipPresentation(ProChipMode.InstallPro, InstallLabel, true);

                default: // ProAccessState.Locked — free Lite user
                    return liteUpdateAvailable
                        ? new ProChipPresentation(ProChipMode.UpdateLite, UpdateLabel, true)
                        : new ProChipPresentation(ProChipMode.UpgradePro, UpgradeLabel, true);
            }
        }
    }
}
