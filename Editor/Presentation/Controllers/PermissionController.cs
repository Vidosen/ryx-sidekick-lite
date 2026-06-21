// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Presentation.Renderers;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Permissions;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Ryx.Sidekick.Editor.Presentation.Controllers
{
    internal sealed class PermissionController
    {
        private readonly PermissionService _service;
        private readonly ResolvePermissionUseCase _resolvePermissionUseCase;
        private readonly ISettingsStore _settingsStore;
        private readonly Func<IProviderUiMetadata> _getActiveProviderMetadata;

        private IPermissionBannerHost _bannerHost;
        private readonly HashSet<string> _autoAcceptedToolUseIds = new();

        /// <summary>
        /// Event raised when AskUserQuestion permission is requested.
        /// Subscriber should show the AskUserQuestion modal overlay.
        /// </summary>
        public event Action<PendingPermission> OnAskUserQuestionPermission;

        /// <summary>
        /// Event raised when a permission request should be shown in the modal overlay.
        /// Replaces inline banners for non-auto-accepted permissions.
        /// </summary>
        public event Action<PendingPermission> OnPermissionModalRequested;

        public PermissionController(
            PermissionService service,
            ISettingsStore settingsStore,
            Func<IProviderUiMetadata> getActiveProviderMetadata,
            ResolvePermissionUseCase resolvePermissionUseCase = null)
        {
            _service = service;
            _settingsStore = settingsStore;
            _getActiveProviderMetadata = getActiveProviderMetadata;
            _resolvePermissionUseCase = resolvePermissionUseCase ?? new ResolvePermissionUseCase(
                _service,
                new AutoAcceptPermissionPolicy(),
                _settingsStore,
                _getActiveProviderMetadata);
        }

        public PermissionController(PermissionService service)
            : this(
                service,
                new SidekickSettingsStore(),
                () => new ProviderUiMetadataAdapter(SidekickSettings.instance.ActiveProvider))
        {
        }

        public void BindBannerHost(IPermissionBannerHost bannerHost)
        {
            _bannerHost = bannerHost;
        }

        public void ResetPending()
        {
            _service?.ResetPending();
        }

        public void HandlePermissionRequest(PendingPermission permission)
        {
            if (permission == null || string.IsNullOrEmpty(permission.ToolUseId)) return;

            var action = _resolvePermissionUseCase.Resolve(permission);

            if (action == PermissionResolutionAction.Ignored)
            {
                if (_settingsStore?.VerboseLogging == true)
                {
                    Debug.Log($"[Ryx Sidekick] Skipping duplicate or unresolved permission: {permission.ToolUseId}");
                }
                return;
            }

            if (action == PermissionResolutionAction.AskUserQuestion)
            {
                if (_settingsStore?.VerboseLogging == true)
                {
                    Debug.Log($"[Ryx Sidekick] Routing {permission.ToolName} permission to AskUserQuestion modal overlay");
                }

                OnAskUserQuestionPermission?.Invoke(permission);
                return;
            }

            if (action == PermissionResolutionAction.AutoAccepted)
            {
                var activeProvider = _getActiveProviderMetadata?.Invoke();
                if (_settingsStore?.VerboseLogging == true
                    && activeProvider != null
                    && activeProvider.IsAutoApprovePermissionMode(_settingsStore.PermissionMode))
                {
                    Debug.Log($"[Ryx Sidekick] Auto-accepting {permission.ToolName} ({activeProvider.Id}:{_settingsStore?.PermissionMode})");
                }

                AddAutoAcceptedBannerToUI(permission);
                return;
            }

            if (_settingsStore?.VerboseLogging == true)
            {
                Debug.Log($"[Ryx Sidekick] Routing permission to modal overlay: {permission.ToolName}");
            }

            OnPermissionModalRequested?.Invoke(permission);
        }

        public bool IsToolUseAutoAccepted(string toolUseId)
            => !string.IsNullOrEmpty(toolUseId) && _autoAcceptedToolUseIds.Contains(toolUseId);

        private void AddAutoAcceptedBannerToUI(PendingPermission permission)
        {
            _autoAcceptedToolUseIds.Add(permission.ToolUseId);
            _bannerHost?.RefreshToolAutoAccepted(permission.ToolUseId);
        }

        private void AddPermissionBannerToUI(PendingPermission permission)
        {
            if (_bannerHost == null) return;

            var banner = CreatePermissionBanner(permission);
            banner.name = $"permission-{permission.ToolUseId}";
            _bannerHost.AddBanner(banner);
            _bannerHost.ScrollToBottom(50);
        }

        private VisualElement CreatePermissionBanner(PendingPermission permission)
        {
            var container = new VisualElement();
            container.AddToClassList("sk-permission-banner");

            // Header with icon and title
            var header = new VisualElement();
            header.AddToClassList("sk-permission-header");

            var icon = new Label();
            icon.AddToClassList("sk-permission-icon");
            SidekickIconCatalog.ApplyToLabel(icon, MessageElementFactory.GetToolIcon(ToolPresentationCatalog.GetEffectiveKind(permission)), "*", 16f);
            header.Add(icon);

            var title = new Label($"Permission Required: {permission.ToolName ?? "Unknown"}");
            title.AddToClassList("sk-permission-title");
            header.Add(title);

            container.Add(header);

            // File path / description
            var description = new VisualElement();
            description.AddToClassList("sk-permission-description");

            if (!string.IsNullOrEmpty(permission.FilePath))
            {
                var pathLabel = new Label(permission.FilePath);
                pathLabel.AddToClassList("sk-permission-filepath");
                description.Add(pathLabel);
            }

            var kind = ToolPresentationCatalog.GetEffectiveKind(permission);

            // Show command for Bash operations
            if (kind == ToolKind.Bash && !string.IsNullOrEmpty(permission.Command))
            {
                var cmdContainer = new VisualElement();
                cmdContainer.AddToClassList("sk-permission-preview");

                var cmdLabel = new Label($"$ {permission.Command}");
                cmdLabel.AddToClassList("sk-permission-preview-text");
                cmdContainer.Add(cmdLabel);

                description.Add(cmdContainer);
            }

            // Show a preview of the content for Write operations
            if (kind == ToolKind.Write && permission.Input != null)
            {
                var content = permission.Input["content"]?.ToString();
                if (!string.IsNullOrEmpty(content))
                {
                    var preview = content.Length > 200 ? content.Substring(0, 200) + "..." : content;
                    var previewContainer = new VisualElement();
                    previewContainer.AddToClassList("sk-permission-preview");

                    var previewLabel = new Label(preview);
                    previewLabel.AddToClassList("sk-permission-preview-text");
                    previewContainer.Add(previewLabel);

                    description.Add(previewContainer);
                }
            }

            // Show diff preview for Edit operations
            if (kind == ToolKind.Edit && permission.Input != null)
            {
                var oldStr = permission.Input["old_string"]?.ToString();
                var newStr = permission.Input["new_string"]?.ToString();
                if (!string.IsNullOrEmpty(oldStr) || !string.IsNullOrEmpty(newStr))
                {
                    var previewContainer = new VisualElement();
                    previewContainer.AddToClassList("sk-permission-preview");

                    if (!string.IsNullOrEmpty(oldStr))
                    {
                        var oldPreview = oldStr.Length > 100 ? oldStr.Substring(0, 100) + "..." : oldStr;
                        var oldLabel = new Label($"- {oldPreview}");
                        oldLabel.AddToClassList("sk-permission-diff-removed");
                        previewContainer.Add(oldLabel);
                    }

                    if (!string.IsNullOrEmpty(newStr))
                    {
                        var newPreview = newStr.Length > 100 ? newStr.Substring(0, 100) + "..." : newStr;
                        var newLabel = new Label($"+ {newPreview}");
                        newLabel.AddToClassList("sk-permission-diff-added");
                        previewContainer.Add(newLabel);
                    }

                    description.Add(previewContainer);
                }
            }

            // Raw preview fallback for MCP-origin permissions
            if (permission.Input == null && !string.IsNullOrEmpty(permission.RawInput))
            {
                var trimmed = permission.RawInput.Length > 150 ? permission.RawInput.Substring(0, 150) + "..." : permission.RawInput;
                var rawLabel = new Label(trimmed);
                rawLabel.AddToClassList("sk-permission-preview-text");
                description.Add(rawLabel);
            }

            container.Add(description);

            // Show decision reason if available (from control_request)
            if (!string.IsNullOrEmpty(permission.DecisionReason))
            {
                var reasonLabel = new Label(permission.DecisionReason);
                reasonLabel.AddToClassList("sk-permission-preview-text");
                reasonLabel.AddToClassList("sk-permission-reason");
                description.Add(reasonLabel);
            }

            // Buttons row
            var buttonsRow = new VisualElement();
            buttonsRow.AddToClassList("sk-permission-buttons");

            var denyBtn = new Button(() => HandlePermissionResponse(permission.ToolUseId, allow: false, enableAutoAccept: false))
            {
                text = "Deny"
            };
            denyBtn.AddToClassList("sk-permission-btn");
            denyBtn.AddToClassList("sk-permission-btn-deny");
            denyBtn.name = $"deny-btn-{permission.ToolUseId}";
            buttonsRow.Add(denyBtn);

            var allowBtn = new Button(() => HandlePermissionResponse(permission.ToolUseId, allow: true, enableAutoAccept: false))
            {
                text = "Allow"
            };
            allowBtn.AddToClassList("sk-permission-btn");
            allowBtn.AddToClassList("sk-permission-btn-allow");
            allowBtn.name = $"allow-btn-{permission.ToolUseId}";
            buttonsRow.Add(allowBtn);

            // "Allow & Remember" button - auto-accepts future calls to this tool
            var allowRememberBtn = new Button(() => HandlePermissionResponse(permission.ToolUseId, allow: true, enableAutoAccept: true))
            {
                text = "Allow & Remember"
            };
            allowRememberBtn.AddToClassList("sk-permission-btn");
            allowRememberBtn.AddToClassList("sk-permission-btn-allow-remember");
            allowRememberBtn.name = $"allow-remember-btn-{permission.ToolUseId}";
            allowRememberBtn.tooltip = $"Allow and auto-accept all future {permission.ToolName} operations this session";
            buttonsRow.Add(allowRememberBtn);

            container.Add(buttonsRow);

            return container;
        }

        private void HandlePermissionResponse(string toolUseId, bool allow, bool enableAutoAccept)
        {
            if (_service == null) return;

            if (!_service.TryRespond(toolUseId, allow, enableAutoAccept, out _))
            {
                if (SidekickSettings.instance.VerboseLogging)
                {
                    Debug.LogWarning($"[Ryx Sidekick] Permission not found or already resolved: {toolUseId}");
                }
                return;
            }

            UpdatePermissionBannerStatus(toolUseId, allow, enableAutoAccept);
        }

        private void UpdatePermissionBannerStatus(string toolUseId, bool allowed, bool autoAcceptEnabled)
        {
            var banner = _bannerHost?.FindBanner($"permission-{toolUseId}");
            if (banner == null) return;

            // Disable all buttons
            var allowBtn = banner.Q<Button>($"allow-btn-{toolUseId}");
            var denyBtn = banner.Q<Button>($"deny-btn-{toolUseId}");
            var allowRememberBtn = banner.Q<Button>($"allow-remember-btn-{toolUseId}");

            allowBtn?.SetEnabled(false);
            denyBtn?.SetEnabled(false);
            allowRememberBtn?.SetEnabled(false);

            // Update banner style based on response
            banner.RemoveFromClassList("sk-permission-banner");
            banner.AddToClassList(allowed ? "sk-permission-banner-allowed" : "sk-permission-banner-denied");

            // Add status indicator with auto-accept note if applicable
            var statusText = allowed
                ? (autoAcceptEnabled ? "✓ Allowed (Remembered)" : "✓ Allowed")
                : "✕ Denied";
            var statusLabel = new Label(statusText);
            statusLabel.AddToClassList("sk-permission-status");
            statusLabel.AddToClassList(allowed ? "sk-permission-status-allowed" : "sk-permission-status-denied");

            var buttonsRow = banner.Q<VisualElement>(className: "sk-permission-buttons");
            buttonsRow?.Add(statusLabel);
        }
    }
}
