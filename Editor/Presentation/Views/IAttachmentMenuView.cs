// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using Unity.AppUI.Core;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    internal interface IAttachmentMenuView
    {
        event Action PopupDismissed;

        event Action<string> AttachmentMenuItemSelected;

        void RenderAttachmentMenuItems(IReadOnlyList<AttachmentMenuItemViewState> items);

        void ShowPopup(bool show);

        bool IsPopupVisible { get; }
    }

    internal sealed class AttachmentMenuView : IAttachmentMenuView
    {
        private readonly Unity.AppUI.UI.Button _addContextButton;
        private readonly VisualElement _attachmentMenuContent;
        private readonly VisualElement _attachmentOptionsContainer;
        private Popover _popover;
        private bool _suppressDismissEvent;

        public AttachmentMenuView(
            Unity.AppUI.UI.Button addContextButton,
            VisualTreeAsset attachmentMenuTemplate)
        {
            _addContextButton = addContextButton;

            var instance = attachmentMenuTemplate?.Instantiate();
            _attachmentMenuContent = instance?.contentContainer ?? instance;
            _attachmentOptionsContainer = _attachmentMenuContent?.Q<VisualElement>("attachment-options-container");
        }

        public event Action PopupDismissed;

        public event Action<string> AttachmentMenuItemSelected;

        public void RenderAttachmentMenuItems(IReadOnlyList<AttachmentMenuItemViewState> items)
        {
            if (_attachmentOptionsContainer == null)
            {
                return;
            }

            _attachmentOptionsContainer.Clear();
            if (items == null)
            {
                return;
            }

            foreach (var item in items)
            {
                var localItem = item;
                var btn = new Button();
                btn.name = localItem.Id;
                btn.AddToClassList("sk-attachment-option");
                if (!localItem.IsEnabled)
                {
                    btn.AddToClassList("disabled");
                    btn.SetEnabled(false);
                }

                if (!string.IsNullOrEmpty(localItem.IconName))
                {
                    var iconLabel = new Label();
                    iconLabel.AddToClassList("sk-attachment-option-icon");
                    SidekickIconCatalog.ApplyToLabel(iconLabel, localItem.IconName, string.Empty, 14f);
                    btn.Add(iconLabel);
                }

                var nameLabel = new Label(localItem.Label);
                nameLabel.AddToClassList("sk-model-option-name");
                btn.Add(nameLabel);

                if (localItem.IsEnabled)
                {
                    btn.RegisterCallback<ClickEvent>(_ => AttachmentMenuItemSelected?.Invoke(localItem.Id));
                }

                _attachmentOptionsContainer.Add(btn);
            }
        }

        public bool IsPopupVisible => _popover != null;

        public void ShowPopup(bool show)
        {
            if (show)
            {
                if (_addContextButton == null || _attachmentMenuContent == null) return;
                if (_popover != null && IsPopupVisible) return;

                _popover = Popover.Build(_addContextButton, _attachmentMenuContent)
                    .SetPlacement(PopoverPlacement.Top)
                    .SetOutsideClickDismiss(true)
                    .SetKeyboardDismiss(true);

                _popover.dismissed += OnPopoverDismissed;
                _popover.Show();
            }
            else
            {
                if (_popover == null) return;
                _suppressDismissEvent = true;
                _popover.Dismiss(DismissType.Manual);
            }
        }

        private void OnPopoverDismissed(Popover popup, DismissType reason)
        {
            _popover.dismissed -= OnPopoverDismissed;
            _popover = null;

            if (_suppressDismissEvent)
            {
                _suppressDismissEvent = false;
                return;
            }

            PopupDismissed?.Invoke();
        }
    }
}
