// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Constants;
using Unity.AppUI.Core;
using Unity.AppUI.UI;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    /// <summary>
    /// Owns transient/banner-style notifications mounted on the App UI Panel's
    /// notification layer. Built in APPUI-T08-06 to replace the bespoke
    /// <c>refresh-banner</c> overlay that used to live inside the chat input area.
    /// </summary>
    internal interface INotificationPresenter
    {
        /// <summary>
        /// Show or hide the manual-refresh hint toast. The toast persists until
        /// <see cref="ShowRefreshHint"/> is called with <c>false</c> or the user
        /// taps the Refresh action.
        /// </summary>
        void ShowRefreshHint(bool show);

        /// <summary>Fired when the user taps the toast's Refresh action.</summary>
        event Action RefreshClicked;
    }

    /// <summary>
    /// Default <see cref="INotificationPresenter"/> implementation backed by
    /// <see cref="Toast"/> on the App UI notification layer.
    /// </summary>
    internal sealed class NotificationPresenter : INotificationPresenter
    {
        private readonly VisualElement _referenceView;

        private Toast _currentToast;

        public NotificationPresenter(VisualElement referenceView)
        {
            _referenceView = referenceView;
        }

        public event Action RefreshClicked;

        public void ShowRefreshHint(bool show)
        {
            if (show)
            {
                ShowRefreshToast();
            }
            else
            {
                DismissRefreshToast();
            }
        }

        private void ShowRefreshToast()
        {
            if (_referenceView == null)
            {
                return;
            }

            // Re-issue if the previous toast already expired or was dismissed
            // out from under us (NotificationDuration.Indefinite removes this
            // concern in practice, but auto-dismiss-on-action means a stale
            // _currentToast may still exist).
            if (_currentToast is { isShownOrQueued: true })
            {
                return;
            }

            _currentToast = Toast
                .Build(_referenceView, SidekickAppConstants.Notifications.RefreshAssetsMessage, NotificationDuration.Indefinite)
                .SetStyle(NotificationStyle.Informative)
                .SetPosition(PopupNotificationPlacement.Bottom)
                .AddAction(1, "Refresh", _ => RefreshClicked?.Invoke(), autoDismiss: true);
            _currentToast.Show();
        }

        private void DismissRefreshToast()
        {
            if (_currentToast == null)
            {
                return;
            }

            if (_currentToast.isShownOrQueued)
            {
                _currentToast.Dismiss(DismissType.Manual);
            }

            _currentToast = null;
        }
    }
}
