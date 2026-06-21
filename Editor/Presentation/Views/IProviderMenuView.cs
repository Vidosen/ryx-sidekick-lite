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
using TextField = UnityEngine.UIElements.TextField;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    internal interface IProviderMenuView
    {
        event Action ProviderRequested;

        event Action ProviderPopupDismissed;

        event Action ModelPopupDismissed;

        event Action ModelRequested;

        event Action CollaborationModeRequested;

        event Action PermissionModeRequested;

        event Action<string> CustomModelApplied;

        event Action<bool> ThinkingChanged;
        event Action<string> ReasoningEffortSelected
        {
            add { }
            remove { }
        }
        event Action ModelCatalogRetryRequested
        {
            add { }
            remove { }
        }

        event Action<string> ProviderOptionSelected;

        event Action<string> ModelPresetSelected;

        /// <summary>Fired when the user clicks a locked Pro provider row.
        /// Arg is the <see cref="ProviderOptionViewState.FeatureId"/>.</summary>
        event Action<string> LockedFeatureClicked
        {
            add { }
            remove { }
        }

        void RenderProviderOptions(IReadOnlyList<ProviderOptionViewState> options);

        void RenderModelPresets(IReadOnlyList<ModelPresetViewState> presets);
        void RenderReasoningEfforts(IReadOnlyList<ReasoningEffortViewState> efforts) { }
        void SetModelCatalogStatus(bool isLoading, string error) { }

        void SetProviderDisplay(string displayName);

        void SetModelDisplay(string displayName);

        void ShowProviderPopup(bool show);

        bool IsProviderPopupVisible { get; }

        void ShowModelPopup(bool show);

        bool IsModelPopupVisible { get; }

        void ShowThinkingSection(bool show);

        void SetThinkingEnabled(bool enabled);

        void SetCollaborationModeDisplay(string displayName);

        void SetPermissionModeDisplay(string displayName);

        void SetCollaborationModeVisible(bool visible);

        void SetPermissionModeVisible(bool visible);

        void SetCollaborationModeIcon(string iconName);

        void SetPermissionModeIcon(string iconName);
    }

    internal sealed class ProviderMenuView : IProviderMenuView
    {
        private readonly Unity.AppUI.UI.Button _providerButton;
        private readonly VisualElement _providerPopoverContent;
        private readonly VisualElement _providerOptionsContainer;
        private Popover _providerPopover;
        private bool _suppressProviderDismissEvent;
        private readonly Unity.AppUI.UI.Button _modelButton;
        private readonly VisualElement _modelPopoverContent;
        private readonly VisualElement _modelPresetsContainer;
        private readonly VisualElement _reasoningEffortSection;
        private readonly VisualElement _reasoningEffortsContainer;
        private readonly Label _modelCatalogStatus;
        private readonly Button _modelCatalogRetry;
        private readonly TextField _modelCustomInput;
        private readonly VisualElement _thinkingSection;
        private readonly VisualElement _thinkingToggle;
        private Popover _modelPopover;
        private bool _suppressModelDismissEvent;
        private readonly Button _collaborationModeButton;
        private readonly Label _collaborationModeLabel;
        private readonly Button _permissionModeButton;
        private readonly Label _permissionModeLabel;
        private bool _thinkingEnabled;

        public ProviderMenuView(
            Unity.AppUI.UI.Button providerButton,
            VisualTreeAsset providerPopoverTemplate,
            Unity.AppUI.UI.Button modelButton,
            VisualTreeAsset modelPopoverTemplate,
            Button collaborationModeButton,
            Label collaborationModeLabel,
            Button permissionModeButton,
            Label permissionModeLabel)
        {
            _providerButton = providerButton;

            // Instantiate provider popover content fragment.
            var providerInstance = providerPopoverTemplate?.Instantiate();
            _providerPopoverContent = providerInstance?.contentContainer ?? providerInstance;
            _providerOptionsContainer = _providerPopoverContent?.Q<VisualElement>("provider-options-container");

            _modelButton = modelButton;

            // Instantiate model popover content fragment.
            var modelInstance = modelPopoverTemplate?.Instantiate();
            _modelPopoverContent = modelInstance?.contentContainer ?? modelInstance;
            _modelPresetsContainer = _modelPopoverContent?.Q<VisualElement>("model-presets-container");
            _reasoningEffortSection = _modelPopoverContent?.Q<VisualElement>("reasoning-effort-section");
            _reasoningEffortsContainer = _modelPopoverContent?.Q<VisualElement>("reasoning-efforts-container");
            _modelCatalogStatus = _modelPopoverContent?.Q<Label>("model-catalog-status");
            _modelCustomInput = _modelPopoverContent?.Q<TextField>("model-custom-input");
            var modelCustomApply = _modelPopoverContent?.Q<Button>("model-custom-apply");
            _modelCatalogRetry = _modelPopoverContent?.Q<Button>("model-catalog-retry");
            _thinkingSection = _modelPopoverContent?.Q<VisualElement>("thinking-section");
            _thinkingToggle = _modelPopoverContent?.Q<VisualElement>("thinking-toggle");

            if (_modelCustomInput != null)
            {
                _modelCustomInput.textEdition.placeholder = "e.g. us.anthropic.claude-opus-4-...";
            }

            _collaborationModeButton = collaborationModeButton;
            _collaborationModeLabel = collaborationModeLabel;
            _permissionModeButton = permissionModeButton;
            _permissionModeLabel = permissionModeLabel;

            providerButton?.RegisterCallback<ClickEvent>(_ => ProviderRequested?.Invoke());
            modelButton?.RegisterCallback<ClickEvent>(_ => ModelRequested?.Invoke());
            _collaborationModeButton?.RegisterCallback<ClickEvent>(_ => CollaborationModeRequested?.Invoke());
            _permissionModeButton?.RegisterCallback<ClickEvent>(_ => PermissionModeRequested?.Invoke());
            modelCustomApply?.RegisterCallback<ClickEvent>(_ => ApplyCustomModel());
            _modelCatalogRetry?.RegisterCallback<ClickEvent>(_ => ModelCatalogRetryRequested?.Invoke());
            _modelCustomInput?.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                {
                    return;
                }

                ApplyCustomModel();
                evt.StopPropagation();
            });
            _thinkingToggle?.RegisterCallback<ClickEvent>(_ =>
            {
                _thinkingEnabled = !_thinkingEnabled;
                ThinkingChanged?.Invoke(_thinkingEnabled);
            });
        }

        public event Action ProviderRequested;

        public event Action ProviderPopupDismissed;

        public event Action ModelPopupDismissed;

        public event Action ModelRequested;

        public event Action CollaborationModeRequested;

        public event Action PermissionModeRequested;

        public event Action<string> CustomModelApplied;

        public event Action<bool> ThinkingChanged;
        public event Action<string> ReasoningEffortSelected;
        public event Action ModelCatalogRetryRequested;

        public event Action<string> ProviderOptionSelected;

        public event Action<string> ModelPresetSelected;

        public event Action<string> LockedFeatureClicked;

        public void RenderProviderOptions(IReadOnlyList<ProviderOptionViewState> options)
        {
            if (_providerOptionsContainer == null)
            {
                return;
            }

            _providerOptionsContainer.Clear();
            if (options == null)
            {
                return;
            }

            var hasRenderedLockedHeader = false;

            foreach (var option in options)
            {
                var localOption = option;

                if (localOption.IsLocked)
                {
                    // Render "Unlock with Pro" section header once before first locked row.
                    if (!hasRenderedLockedHeader)
                    {
                        hasRenderedLockedHeader = true;
                        var sectionHeader = new Label("Unlock with Pro");
                        sectionHeader.AddToClassList("sk-pro-section");
                        _providerOptionsContainer.Add(sectionHeader);
                    }

                    // Locked row — shows provider name + PRO badge; click opens paywall.
                    var lockedBtn = new Button();
                    lockedBtn.name = localOption.Id;
                    lockedBtn.AddToClassList("sk-model-option");
                    lockedBtn.AddToClassList("sk-model-option--locked");

                    var lockedNameLabel = new Label(localOption.DisplayName);
                    lockedNameLabel.AddToClassList("sk-model-option-name");
                    lockedBtn.Add(lockedNameLabel);

                    var proBadge = new Label("PRO");
                    proBadge.AddToClassList("sk-pro-badge");
                    lockedBtn.Add(proBadge);

                    lockedBtn.RegisterCallback<ClickEvent>(_ => LockedFeatureClicked?.Invoke(localOption.FeatureId));
                    _providerOptionsContainer.Add(lockedBtn);
                }
                else
                {
                    var btn = new Button();
                    btn.name = localOption.Id;
                    btn.AddToClassList("sk-model-option");
                    if (localOption.IsActive)
                    {
                        btn.AddToClassList("active");
                    }

                    var nameLabel = new Label(localOption.DisplayName);
                    nameLabel.AddToClassList("sk-model-option-name");
                    btn.Add(nameLabel);
                    btn.RegisterCallback<ClickEvent>(_ => ProviderOptionSelected?.Invoke(localOption.Id));
                    _providerOptionsContainer.Add(btn);
                }
            }
        }

        public void RenderModelPresets(IReadOnlyList<ModelPresetViewState> presets)
        {
            if (_modelPresetsContainer == null)
            {
                return;
            }

            _modelPresetsContainer.Clear();
            if (presets == null)
            {
                return;
            }

            foreach (var preset in presets)
            {
                var localPreset = preset;
                var btn = new Button();
                btn.name = localPreset.Name;
                btn.AddToClassList("sk-model-option");
                if (localPreset.IsActive)
                {
                    btn.AddToClassList("active");
                }

                var nameLabel = new Label(localPreset.DisplayName);
                nameLabel.AddToClassList("sk-model-option-name");
                btn.Add(nameLabel);

                if (!string.IsNullOrEmpty(localPreset.Description))
                {
                    var descLabel = new Label(localPreset.Description);
                    descLabel.AddToClassList("sk-model-option-desc");
                    btn.Add(descLabel);
                }

                // The display name can hide the raw id ("Fable" vs "claude-fable-5[1m]") —
                // keep the id discoverable via tooltip.
                if (!string.Equals(localPreset.DisplayName, localPreset.Name, StringComparison.Ordinal))
                {
                    btn.tooltip = localPreset.Name;
                }

                btn.RegisterCallback<ClickEvent>(_ => ModelPresetSelected?.Invoke(localPreset.Name));
                _modelPresetsContainer.Add(btn);
            }
        }

        public void RenderReasoningEfforts(IReadOnlyList<ReasoningEffortViewState> efforts)
        {
            if (_reasoningEffortsContainer == null)
            {
                return;
            }

            _reasoningEffortsContainer.Clear();
            if (_reasoningEffortSection != null)
            {
                _reasoningEffortSection.style.display = efforts?.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (efforts == null)
            {
                return;
            }

            foreach (var effort in efforts)
            {
                var localEffort = effort;
                var button = new Button { text = localEffort.Value };
                button.tooltip = localEffort.Description;
                button.AddToClassList("sk-reasoning-effort-option");
                if (localEffort.IsActive)
                {
                    button.AddToClassList("active");
                }

                button.RegisterCallback<ClickEvent>(_ => ReasoningEffortSelected?.Invoke(localEffort.Value));
                _reasoningEffortsContainer.Add(button);
            }
        }

        public void SetModelCatalogStatus(bool isLoading, string error)
        {
            if (_modelCatalogStatus == null)
            {
                return;
            }

            _modelCatalogStatus.text = isLoading
                ? "Refreshing available models..."
                : string.IsNullOrWhiteSpace(error)
                    ? string.Empty
                    : $"Could not refresh models: {error}";
            _modelCatalogStatus.style.display = string.IsNullOrEmpty(_modelCatalogStatus.text)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            if (_modelCatalogRetry != null)
            {
                _modelCatalogRetry.style.display = !isLoading && !string.IsNullOrWhiteSpace(error)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
        }

        public void SetProviderDisplay(string displayName)
        {
            if (_providerButton != null)
            {
                _providerButton.title = displayName ?? string.Empty;
            }
        }

        public void SetModelDisplay(string displayName)
        {
            if (_modelButton == null)
            {
                return;
            }

            var model = displayName ?? string.Empty;
            _modelButton.title = model.Length > 20 ? model.Substring(0, 17) + "..." : model;
            _modelButton.tooltip = model;
        }

        public bool IsProviderPopupVisible => _providerPopover != null;

        public void ShowProviderPopup(bool show)
        {
            if (show)
            {
                if (_providerButton == null || _providerPopoverContent == null) return;
                if (_providerPopover != null && IsProviderPopupVisible) return;

                _providerPopover = Popover.Build(_providerButton, _providerPopoverContent)
                    .SetPlacement(PopoverPlacement.Top)
                    .SetOutsideClickDismiss(true)
                    .SetKeyboardDismiss(true);

                _providerPopover.dismissed += OnProviderPopoverDismissed;
                _providerPopover.Show();
            }
            else
            {
                if (_providerPopover == null) return;
                _suppressProviderDismissEvent = true;
                _providerPopover.Dismiss(DismissType.Manual);
            }
        }

        private void OnProviderPopoverDismissed(Popover popup, DismissType reason)
        {
            _providerPopover.dismissed -= OnProviderPopoverDismissed;
            _providerPopover = null;

            if (_suppressProviderDismissEvent)
            {
                _suppressProviderDismissEvent = false;
                return;
            }

            // User-driven dismiss (outside click, ESC, etc.) — sync VM.
            ProviderPopupDismissed?.Invoke();
        }

        public bool IsModelPopupVisible => _modelPopover != null;

        public void ShowModelPopup(bool show)
        {
            if (show)
            {
                if (_modelButton == null || _modelPopoverContent == null) return;
                if (_modelPopover != null && IsModelPopupVisible) return;

                _modelPopover = Popover.Build(_modelButton, _modelPopoverContent)
                    .SetPlacement(PopoverPlacement.Top)
                    .SetOutsideClickDismiss(true)
                    .SetKeyboardDismiss(true);

                _modelPopover.dismissed += OnModelPopoverDismissed;
                _modelPopover.Show();
            }
            else
            {
                if (_modelPopover == null) return;
                _suppressModelDismissEvent = true;
                _modelPopover.Dismiss(DismissType.Manual);
            }
        }

        private void OnModelPopoverDismissed(Popover popup, DismissType reason)
        {
            _modelPopover.dismissed -= OnModelPopoverDismissed;
            _modelPopover = null;

            if (_suppressModelDismissEvent)
            {
                _suppressModelDismissEvent = false;
                return;
            }

            // User-driven dismiss (outside click, ESC, etc.) — sync VM.
            ModelPopupDismissed?.Invoke();
        }

        public void ShowThinkingSection(bool show)
        {
            if (_thinkingSection != null)
            {
                _thinkingSection.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void SetThinkingEnabled(bool enabled)
        {
            _thinkingEnabled = enabled;
            if (_thinkingToggle == null)
            {
                return;
            }

            if (enabled)
            {
                _thinkingToggle.AddToClassList("active");
            }
            else
            {
                _thinkingToggle.RemoveFromClassList("active");
            }
        }

        public void SetCollaborationModeDisplay(string displayName)
        {
            if (_collaborationModeLabel != null)
            {
                _collaborationModeLabel.text = displayName ?? string.Empty;
            }
        }

        public void SetPermissionModeDisplay(string displayName)
        {
            if (_permissionModeLabel != null)
            {
                _permissionModeLabel.text = displayName ?? string.Empty;
            }
        }

        public void SetCollaborationModeVisible(bool visible)
        {
            if (_collaborationModeButton != null)
            {
                _collaborationModeButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void SetPermissionModeVisible(bool visible)
        {
            if (_permissionModeButton != null)
            {
                _permissionModeButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void SetCollaborationModeIcon(string iconName)
        {
            var iconLabel = _collaborationModeButton?.Q<Label>(className: "sk-edit-mode-icon");
            if (iconLabel != null)
            {
                SidekickIconCatalog.ApplyToLabel(iconLabel, iconName, "O", 13f);
            }
        }

        public void SetPermissionModeIcon(string iconName)
        {
            var iconLabel = _permissionModeButton?.Q<Label>(className: "sk-edit-mode-icon");
            if (iconLabel != null)
            {
                SidekickIconCatalog.ApplyToLabel(iconLabel, iconName, "*", 13f);
            }
        }

        private void ApplyCustomModel()
        {
            var customModel = _modelCustomInput?.value?.Trim();
            if (!string.IsNullOrEmpty(customModel))
            {
                CustomModelApplied?.Invoke(customModel);
            }
        }
    }
}
