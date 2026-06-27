// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Presentation.Shell.Modals;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    /// <summary>
    /// Sidekick-styled confirmation dialog backed by a <see cref="SidekickModalLayer"/> scrim.
    /// Drop-in replacement for <c>EditorUtility.DisplayDialog</c> that stays entirely inside UI Toolkit.
    /// </summary>
    internal static class SidekickConfirmDialog
    {
        private const string DialogClass = "sk-confirm-dialog";
        private const string ModalContentClass = "sk-modal-confirm-content";

        /// <summary>
        /// Show a confirmation dialog over the <see cref="SidekickModalLayer"/>.
        /// </summary>
        internal static void Show(
            SidekickModalLayer layer,
            string title,
            string description,
            string primaryActionText,
            string cancelActionText,
            Action onPrimary,
            Action onCancel = null)
        {
            if (layer == null) return;

            var card = new VisualElement();
            card.AddToClassList(DialogClass);

            var titleLabel = new Label(title ?? string.Empty);
            titleLabel.AddToClassList("sk-confirm-dialog-title");
            card.Add(titleLabel);

            if (!string.IsNullOrEmpty(description))
            {
                var descLabel = new Label(description);
                descLabel.AddToClassList("sk-confirm-dialog-desc");
                card.Add(descLabel);
            }

            var buttons = new VisualElement();
            buttons.AddToClassList("sk-confirm-dialog-buttons");

            var primaryTriggered = false;

            var primaryBtn = new Button(() => primaryTriggered = true);
            primaryBtn.text = primaryActionText ?? string.Empty;
            primaryBtn.AddToClassList("sk-confirm-dialog-primary");
            buttons.Add(primaryBtn);

            var cancelBtn = new Button();
            cancelBtn.text = cancelActionText ?? string.Empty;
            cancelBtn.AddToClassList("sk-confirm-dialog-cancel");
            buttons.Add(cancelBtn);

            card.Add(buttons);

            var handle = layer.Show(card, new SidekickModalOptions(true, true, ModalContentClass));

            // Wire both buttons after handle creation so they can dismiss. Both close the dialog;
            // primaryTriggered (set by the primary button's ctor action above) selects onPrimary
            // vs onCancel in the Dismissed handler below. Without dismissing here the primary button
            // would set the flag but leave the dialog open (App UI's AlertDialog auto-dismissed on
            // any action; the plain buttons must do it explicitly).
            primaryBtn.clicked += () => handle.Dismiss(SidekickModalDismissType.Manual);
            cancelBtn.clicked += () => handle.Dismiss(SidekickModalDismissType.Manual);

            handle.Dismissed += type =>
            {
                var action = primaryTriggered ? onPrimary : onCancel;
                action?.Invoke();
            };
        }
    }
}
