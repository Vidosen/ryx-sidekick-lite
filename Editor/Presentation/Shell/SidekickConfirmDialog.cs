// SPDX-License-Identifier: GPL-3.0-only
using System;
using Unity.AppUI.Core;
using Unity.AppUI.UI;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    /// <summary>
    /// Sidekick-styled confirmation dialog backed by App UI <see cref="AlertDialog"/> hosted in a
    /// <see cref="Modal"/>. Drop-in replacement for <c>EditorUtility.DisplayDialog</c> that stays
    /// entirely inside the UI Toolkit panel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Action callbacks (<c>onPrimary</c> / <c>onCancel</c>) are NOT invoked inline from the
    /// AlertDialog button click. Instead they are deferred to the panel's next tick (via
    /// <see cref="IVisualElementScheduler.Execute(Action)"/>) from inside <c>Modal.dismissed</c> —
    /// i.e. AFTER <c>Popup&lt;T&gt;.InvokeDismissedEventHandlers</c> has synchronously removed
    /// this AlertDialog's view from <c>popupContainer</c> and restored focus.
    /// </para>
    /// <para>
    /// Reason: if the callback opens another App UI <see cref="Modal"/> (e.g. the contextual
    /// Setup wizard chained from this dialog's primary action), building that modal while this
    /// AlertDialog's view is still mounted in <c>popupContainer</c> (mid-AnimateViewOut) leaves
    /// the new modal's descendant buttons unable to receive ClickEvents. Waiting for this
    /// dialog to fully unwind first leaves <c>popupContainer</c> in a clean state for the next
    /// modal — same shape as the auto-launcher / agent-callback paths that already work.
    /// </para>
    /// </remarks>
    internal static class SidekickConfirmDialog
    {
        private const string DialogClass = "sk-confirm-dialog";
        private const string ModalContentClass = "sk-modal-confirm-content";
        private const int PrimaryActionId = 1;
        private const int CancelActionId = 2;

        /// <summary>
        /// Show a confirmation dialog over the App UI panel containing
        /// <paramref name="referenceView"/>.
        /// </summary>
        /// <param name="referenceView">
        /// Any element inside the target App UI panel — typically <c>SidekickWindowView.Root</c>.
        /// </param>
        /// <param name="title">Dialog title (header text).</param>
        /// <param name="description">Body text under the title.</param>
        /// <param name="primaryActionText">Primary button label.</param>
        /// <param name="cancelActionText">Cancel button label.</param>
        /// <param name="onPrimary">
        /// Invoked when the user clicks the primary button — runs AFTER this dialog has fully
        /// dismissed and unmounted, so any followup modal it opens lands in a clean
        /// <c>popupContainer</c>.
        /// </param>
        /// <param name="onCancel">
        /// Optional. Invoked when the user dismisses via Cancel, ESC, or outside-click — runs
        /// AFTER this dialog has fully dismissed. Not invoked if the primary action ran.
        /// </param>
        /// <param name="variant">App UI <see cref="AlertSemantic"/> variant. Defaults to <c>Default</c>.</param>
        internal static void Show(
            VisualElement referenceView,
            string title,
            string description,
            string primaryActionText,
            string cancelActionText,
            Action onPrimary,
            Action onCancel = null,
            AlertSemantic variant = AlertSemantic.Default)
        {
            if (referenceView == null)
                return;

            var dialog = new AlertDialog
            {
                title = title,
                description = description,
                variant = variant,
            };
            dialog.AddToClassList(DialogClass);

            var modal = Modal.Build(referenceView, dialog)
                .SetOutsideClickDismiss(true)
                .SetKeyboardDismiss(true);
            
            dialog.parent?.AddToClassList(ModalContentClass);

            var primaryTriggered = false;
            
            dialog.SetPrimaryAction(PrimaryActionId, primaryActionText, () => primaryTriggered = true);
            dialog.SetCancelAction(CancelActionId, cancelActionText);
            
            dialog.dismissRequested += modal.Dismiss;

            modal.dismissed += (_, _) =>
            {
                var action = primaryTriggered ? onPrimary : onCancel;
                if (action == null)
                    return;

                referenceView.schedule.Execute(action);
            };

            modal.Show();
        }
    }
}
