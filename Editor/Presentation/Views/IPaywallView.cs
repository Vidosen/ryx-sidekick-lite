// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Presentation.State;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    internal interface IPaywallView
    {
        void Render(PaywallViewState state);

        /// <summary>Buy-mode CTA clicked (open the purchase/upsell URL).</summary>
        event Action PrimaryActionRequested;

        /// <summary>Install-mode CTA clicked (start the one-click Pro install).</summary>
        event Action InstallActionRequested;

        event Action DismissRequested;
    }
}
