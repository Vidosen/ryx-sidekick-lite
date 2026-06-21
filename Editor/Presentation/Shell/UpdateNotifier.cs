// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Domain.Updates;
using Ryx.Sidekick.Editor.Presentation.Contracts;
using Unity.AppUI.Core;
using Unity.AppUI.UI;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    /// <summary>
    /// <see cref="IUpdateNotifier"/> backed by App UI <see cref="Toast"/>.
    /// Each available update receives its own persistent toast on the App UI
    /// notification layer.
    ///
    /// The Toast API supports multiple action buttons via chained
    /// <c>.AddAction(id, label, callback, autoDismiss)</c> calls (same API used
    /// by <see cref="NotificationPresenter"/> for the Refresh action). Two
    /// actions are wired per toast:
    /// <list type="bullet">
    ///   <item><description>"Update" — invokes <c>onUpdate</c>.</description></item>
    ///   <item>
    ///     <description>
    ///       "What's new" — invokes <c>onWhatsNew</c>; only added when
    ///       <see cref="UpdateAvailability.ChangelogUrl"/> is non-empty.
    ///     </description>
    ///   </item>
    /// </list>
    /// </summary>
    internal sealed class UpdateNotifier : IUpdateNotifier
    {
        private readonly UnityEngine.UIElements.VisualElement _referenceView;

        public UpdateNotifier(UnityEngine.UIElements.VisualElement referenceView)
        {
            _referenceView = referenceView;
        }

        public void ShowUpdates(
            IReadOnlyList<UpdateAvailability> updates,
            Action<UpdateAvailability> onUpdate,
            Action<UpdateAvailability> onWhatsNew)
        {
            if (updates == null || updates.Count == 0) return;
            if (_referenceView == null) return;

            foreach (var u in updates)
            {
                var captured = u;
                var message = $"Ryx Sidekick {captured.LatestVersion} available";

                var toast = Toast
                    .Build(_referenceView, message, NotificationDuration.Indefinite)
                    .SetStyle(NotificationStyle.Informative)
                    .SetPosition(PopupNotificationPlacement.Bottom)
                    .AddAction(1, "Update", _ => onUpdate?.Invoke(captured), autoDismiss: true);

                if (!string.IsNullOrWhiteSpace(captured.ChangelogUrl))
                {
                    toast = toast.AddAction(2, "What's new", _ => onWhatsNew?.Invoke(captured), autoDismiss: false);
                }

                toast.Show();
            }
        }
    }
}
