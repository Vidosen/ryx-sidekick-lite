// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Domain.Updates;

namespace Ryx.Sidekick.Editor.Presentation.Contracts
{
    /// <summary>
    /// Surfaces one in-editor update notice per available update.
    /// Implementations are free to use any notification mechanism (Toast, banner, etc.).
    /// The seam exists primarily so <see cref="Ryx.Sidekick.Editor.Presentation.ViewModels.UpdateNotificationViewModel"/>
    /// can be unit-tested without a live App UI panel.
    /// </summary>
    internal interface IUpdateNotifier
    {
        /// <summary>
        /// Show one notice per available update; wire the action callbacks.
        /// </summary>
        void ShowUpdates(IReadOnlyList<UpdateAvailability> updates, Action<UpdateAvailability> onUpdate, Action<UpdateAvailability> onWhatsNew);
    }
}
