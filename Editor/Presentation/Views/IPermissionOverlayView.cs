// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    internal interface IPermissionOverlayView
    {
        event Action ClosedRequested;

        event Action ShowMoreRequested;

        event Action<bool> AllowRequested;

        event Action<bool> DenyRequested;

        void Render(PermissionRequestViewState state);
    }

    internal sealed class PermissionOverlayView : IPermissionOverlayView
    {
        private readonly VisualElement _container;
        private readonly VisualElement _overlay;
        private readonly Label _icon;
        private readonly Label _title;
        private readonly Label _counter;
        private readonly Label _toolName;
        private readonly VisualElement _pathRow;
        private readonly Label _path;
        private readonly VisualElement _commandRow;
        private readonly Label _command;
        private readonly ScrollView _previewScroll;
        private readonly VisualElement _preview;
        private readonly Button _showMoreButton;
        private readonly Label _reason;

        public PermissionOverlayView(
            VisualElement container,
            VisualElement overlay,
            VisualElement backdrop,
            Label icon,
            Label title,
            Label counter,
            Button closeButton,
            Label toolName,
            VisualElement pathRow,
            Label path,
            VisualElement commandRow,
            Label command,
            ScrollView previewScroll,
            VisualElement preview,
            Button showMoreButton,
            Label reason,
            Button denyButton,
            Button denyRememberButton,
            Button allowButton,
            Button rememberButton)
        {
            _container = container;
            _overlay = overlay;
            _icon = icon;
            _title = title;
            _counter = counter;
            _toolName = toolName;
            _pathRow = pathRow;
            _path = path;
            _commandRow = commandRow;
            _command = command;
            _previewScroll = previewScroll;
            _preview = preview;
            _showMoreButton = showMoreButton;
            _reason = reason;

            // Apply the static file-path icon once.
            var pathIcon = overlay?.Q<Label>(className: "sk-perm-overlay__path-icon");
            if (pathIcon != null)
            {
                SidekickIconCatalog.ApplyToLabel(pathIcon, "ui-file", string.Empty, 14f);
            }

            closeButton?.RegisterCallback<ClickEvent>(_ => ClosedRequested?.Invoke());
            // Backdrop is visual-only (picking-mode="Ignore") — no click handler.
            _showMoreButton?.RegisterCallback<ClickEvent>(_ => ShowMoreRequested?.Invoke());
            denyButton?.RegisterCallback<ClickEvent>(_ => DenyRequested?.Invoke(false));
            denyRememberButton?.RegisterCallback<ClickEvent>(_ => DenyRequested?.Invoke(true));
            allowButton?.RegisterCallback<ClickEvent>(_ => AllowRequested?.Invoke(false));
            rememberButton?.RegisterCallback<ClickEvent>(_ => AllowRequested?.Invoke(true));
            _overlay?.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape)
                {
                    return;
                }

                ClosedRequested?.Invoke();
                evt.StopPropagation();
            });
        }

        public event Action ClosedRequested;

        public event Action ShowMoreRequested;

        public event Action<bool> AllowRequested;

        public event Action<bool> DenyRequested;

        /// <summary>
        /// Exposes the overlay element for tests that need to introspect the rendered DOM
        /// without standing up an App UI Panel/Modal.
        /// </summary>
        internal VisualElement ContentForTests => _overlay;

        public void Render(PermissionRequestViewState state)
        {
            SetVisible(state.IsVisible);
            UpdateContent(state);
        }

        private void SetVisible(bool show)
        {
            if (_container != null)
            {
                _container.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_overlay != null)
            {
                _overlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void UpdateContent(PermissionRequestViewState state)
        {
            if (_icon != null)
            {
                SidekickIconCatalog.ApplyToLabel(_icon, state.IconName, "*", 16f);
            }

            if (_title != null)
            {
                _title.text = state.Title ?? string.Empty;
            }

            if (_counter != null)
            {
                _counter.text = state.CounterText ?? string.Empty;
                _counter.style.display = string.IsNullOrEmpty(state.CounterText) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_toolName != null)
            {
                _toolName.text = state.ToolName ?? string.Empty;
            }

            if (_pathRow != null)
            {
                _pathRow.style.display = string.IsNullOrEmpty(state.PathText) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_path != null)
            {
                _path.text = state.PathText ?? string.Empty;
            }

            if (_commandRow != null)
            {
                _commandRow.style.display = string.IsNullOrEmpty(state.CommandText) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_command != null)
            {
                _command.text = state.CommandText ?? string.Empty;
            }

            if (_reason != null)
            {
                _reason.text = state.ReasonText ?? string.Empty;
                _reason.style.display = string.IsNullOrEmpty(state.ReasonText) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_preview != null)
            {
                _preview.Clear();

                if (!string.IsNullOrEmpty(state.PreviewText))
                {
                    var previewLabel = new Label(state.PreviewText);
                    previewLabel.AddToClassList("sk-perm-preview__content");
                    _preview.Add(previewLabel);
                }
            }

            if (_previewScroll != null)
            {
                _previewScroll.style.display = string.IsNullOrEmpty(state.PreviewText) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_showMoreButton != null)
            {
                _showMoreButton.style.display = state.CanShowMore ? DisplayStyle.Flex : DisplayStyle.None;
                _showMoreButton.text = state.IsPreviewExpanded ? "Show less ▲" : "Show more ▼";
            }
        }
    }
}
