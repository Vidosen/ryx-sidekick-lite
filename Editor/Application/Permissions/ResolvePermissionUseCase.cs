// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Permissions;

namespace Ryx.Sidekick.Editor.UseCases.Permissions
{
    internal enum PermissionResolutionAction
    {
        Ignored,
        AskUserQuestion,
        AutoAccepted,
        Queued
    }

    internal sealed class ResolvePermissionUseCase
    {
        private readonly PermissionService _permissionService;
        private readonly AutoAcceptPermissionPolicy _autoAcceptPermissionPolicy;
        private readonly ISettingsStore _settingsStore;
        private readonly Func<IProviderUiMetadata> _getActiveProviderMetadata;

        public ResolvePermissionUseCase(PermissionService permissionService)
            : this(permissionService, null, null, null)
        {
        }

        public ResolvePermissionUseCase(
            PermissionService permissionService,
            AutoAcceptPermissionPolicy autoAcceptPermissionPolicy,
            ISettingsStore settingsStore,
            Func<IProviderUiMetadata> getActiveProviderMetadata)
        {
            _permissionService = permissionService;
            _autoAcceptPermissionPolicy = autoAcceptPermissionPolicy ?? new AutoAcceptPermissionPolicy();
            _settingsStore = settingsStore;
            _getActiveProviderMetadata = getActiveProviderMetadata;
        }

        public PermissionResolutionAction Resolve(PendingPermission permission)
        {
            if (permission == null || string.IsNullOrEmpty(permission.ToolUseId) || _permissionService == null)
            {
                return PermissionResolutionAction.Ignored;
            }

            if (_permissionService.IsDuplicateOrResolved(permission.ToolUseId))
            {
                return PermissionResolutionAction.Ignored;
            }

            var toolKind = ToolPresentationCatalog.GetEffectiveKind(permission);
            if (toolKind is ToolKind.AskUserQuestion or ToolKind.ExitPlanMode or ToolKind.ImplementPlan)
            {
                return PermissionResolutionAction.AskUserQuestion;
            }

            if (_autoAcceptPermissionPolicy.ShouldAutoAccept(_settingsStore, _getActiveProviderMetadata?.Invoke()))
            {
                _permissionService.AddAutoAccept(permission);

                if (_permissionService.TryAutoAccept(permission))
                {
                    return PermissionResolutionAction.AutoAccepted;
                }
            }

            if (_permissionService.TryAutoAccept(permission))
            {
                return PermissionResolutionAction.AutoAccepted;
            }

            return _permissionService.TryEnqueue(permission)
                ? PermissionResolutionAction.Queued
                : PermissionResolutionAction.Ignored;
        }

        public bool TryResolveDecision(
            PendingPermission permission,
            bool allow,
            bool remember,
            string message = null)
        {
            if (permission == null || string.IsNullOrEmpty(permission.ToolUseId) || _permissionService == null)
            {
                return false;
            }

            return _permissionService.TryRespond(permission.ToolUseId, allow, remember, out _, message);
        }
    }
}
