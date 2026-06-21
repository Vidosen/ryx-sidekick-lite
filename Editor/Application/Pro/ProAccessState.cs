// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.UseCases.Pro
{
    /// <summary>
    /// Tri-state describing the user's relationship to Sidekick Pro, driving the gate UI:
    /// <list type="bullet">
    /// <item><see cref="Locked"/> — not entitled: show the buy/upsell paywall.</item>
    /// <item><see cref="OwnedNotInstalled"/> — entitled but the Pro package is absent: show the
    ///   "Install Pro" experience (one-click install).</item>
    /// <item><see cref="Installed"/> — entitled and the Pro package is present: full experience, no gate.</item>
    /// </list>
    /// </summary>
    internal enum ProAccessState
    {
        Locked,
        OwnedNotInstalled,
        Installed
    }
}
