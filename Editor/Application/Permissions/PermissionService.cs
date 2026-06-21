// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using UnityEngine;

namespace Ryx.Sidekick.Editor.UseCases.Permissions
{

    /// <summary>
    /// Service for managing permission requests with auto-accept caching.
    /// Mirrors VS Code extension's permission management approach.
    /// </summary>
    internal class PermissionService
    {
        // Queue of pending permissions
        private readonly Dictionary<string, PendingPermission> _pendingPermissions = new();

        // Track resolved permissions to prevent duplicates
        private readonly HashSet<string> _resolvedPermissions = new();

        // Remembered decisions for session runtimes and legacy tool flows.
        private readonly Dictionary<string, SessionPermissionOptionKind> _rememberedDecisions = new();

        private Action<PendingPermission, bool, string, bool> _sendResponse;
        private readonly ISettingsStore _settingsStore;

        public PermissionService(
            Action<PendingPermission, bool, string, bool> sendResponse,
            ISettingsStore settingsStore = null)
        {
            _sendResponse = sendResponse;
            _settingsStore = settingsStore;
        }

        public void UpdateResponseCallback(Action<PendingPermission, bool, string, bool> sendResponse)
        {
            _sendResponse = sendResponse;
        }

        public bool IsDuplicateOrResolved(string toolUseId)
        {
            if (string.IsNullOrEmpty(toolUseId)) return true;
            return _pendingPermissions.ContainsKey(toolUseId) || _resolvedPermissions.Contains(toolUseId);
        }

        /// <summary>
        /// Checks if a tool is in the auto-accept list.
        /// </summary>
        public bool IsAutoAccepted(string toolName)
        {
            if (string.IsNullOrEmpty(toolName)) return false;
            var kind = ToolPresentationCatalog.InferKind(null, null, toolName, null);
            var cacheKey = ToolPresentationCatalog.BuildDecisionKey(null, kind, toolName);
            return _rememberedDecisions.TryGetValue(cacheKey, out var decision)
                && (decision == SessionPermissionOptionKind.AllowAlways || decision == SessionPermissionOptionKind.AllowOnce);
        }

        public bool IsAutoAccepted(PendingPermission permission)
        {
            var cacheKey = GetCacheKey(permission);
            if (string.IsNullOrEmpty(cacheKey))
            {
                return false;
            }

            return _rememberedDecisions.TryGetValue(cacheKey, out var decision)
                && (decision == SessionPermissionOptionKind.AllowAlways || decision == SessionPermissionOptionKind.AllowOnce);
        }

        public bool TryAutoAccept(PendingPermission permission, string message = null)
        {
            if (permission == null || string.IsNullOrEmpty(permission.ToolUseId)) return false;
            if (IsDuplicateOrResolved(permission.ToolUseId)) return false;

            var cacheKey = GetCacheKey(permission);
            if (!_rememberedDecisions.TryGetValue(cacheKey, out var decision))
            {
                return false;
            }

            _resolvedPermissions.Add(permission.ToolUseId);
            permission.SelectedOptionId = SelectOptionId(permission, decision, allowFallback: decision is SessionPermissionOptionKind.AllowAlways or SessionPermissionOptionKind.AllowOnce);
            _sendResponse?.Invoke(permission, decision is SessionPermissionOptionKind.AllowAlways or SessionPermissionOptionKind.AllowOnce, message, decision is SessionPermissionOptionKind.AllowAlways or SessionPermissionOptionKind.RejectAlways);
            return true;
        }

        public bool TryEnqueue(PendingPermission permission)
        {
            if (permission == null || string.IsNullOrEmpty(permission.ToolUseId)) return false;
            if (IsDuplicateOrResolved(permission.ToolUseId)) return false;

            _pendingPermissions[permission.ToolUseId] = permission;
            return true;
        }

        public bool TryRespond(string toolUseId, bool allow, bool enableAutoAccept, out PendingPermission permission, string message = null)
        {
            permission = null;

            if (string.IsNullOrEmpty(toolUseId)) return false;
            if (_resolvedPermissions.Contains(toolUseId)) return false;
            if (!_pendingPermissions.TryGetValue(toolUseId, out permission)) return false;

            _resolvedPermissions.Add(toolUseId);

            // Register auto-accept before sending response (so subsequent requests during the same turn can be auto-handled)
            if (enableAutoAccept)
            {
                RememberDecision(permission, allow);
            }

            if (permission.Options != null && permission.Options.Count > 0)
            {
                var preferredKind = enableAutoAccept
                    ? (allow ? SessionPermissionOptionKind.AllowAlways : SessionPermissionOptionKind.RejectAlways)
                    : (allow ? SessionPermissionOptionKind.AllowOnce : SessionPermissionOptionKind.RejectOnce);
                permission.SelectedOptionId = SelectOptionId(permission, preferredKind, allow);
            }

            _sendResponse?.Invoke(permission, allow, message, enableAutoAccept);
            _pendingPermissions.Remove(toolUseId);
            return true;
        }

        /// <summary>
        /// Adds a tool to the auto-accept list.
        /// Future permission requests for this tool will be auto-accepted.
        /// </summary>
        public void AddAutoAccept(string toolName)
        {
            if (!string.IsNullOrEmpty(toolName))
            {
                var kind = ToolPresentationCatalog.InferKind(null, null, toolName, null);
                var cacheKey = ToolPresentationCatalog.BuildDecisionKey(null, kind, toolName);
                _rememberedDecisions[cacheKey] = SessionPermissionOptionKind.AllowAlways;
                if (_settingsStore?.VerboseLogging == true)
                {
                    Debug.Log($"[PermissionService] Added {toolName} to auto-accept list");
                }
            }
        }

        public void AddAutoAccept(PendingPermission permission)
        {
            var cacheKey = GetCacheKey(permission);
            if (string.IsNullOrEmpty(cacheKey))
            {
                return;
            }

            _rememberedDecisions[cacheKey] = SessionPermissionOptionKind.AllowAlways;
            if (_settingsStore?.VerboseLogging == true)
            {
                Debug.Log($"[PermissionService] Added {cacheKey} to auto-accept list");
            }
        }

        /// <summary>
        /// Clears pending and resolved tracking state.
        /// Auto-accept is preserved across conversations for UX convenience.
        /// </summary>
        public void ResetPending()
        {
            _pendingPermissions.Clear();
            _resolvedPermissions.Clear();
        }

        private static string GetCacheKey(PendingPermission permission)
        {
            if (permission == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(permission.DecisionKey))
            {
                return permission.DecisionKey;
            }

            var inferredKind = ToolPresentationCatalog.GetEffectiveKind(permission);
            return ToolPresentationCatalog.BuildDecisionKey(
                permission.ProviderId,
                inferredKind,
                permission.RawToolTitle,
                permission.RawToolName,
                permission.CacheKey,
                permission.ToolName);
        }

        private void RememberDecision(PendingPermission permission, bool allow)
        {
            var cacheKey = GetCacheKey(permission);
            if (string.IsNullOrEmpty(cacheKey))
            {
                return;
            }

            var optionKind = allow ? SessionPermissionOptionKind.AllowAlways : SessionPermissionOptionKind.RejectAlways;
            if (!HasOption(permission, optionKind))
            {
                optionKind = allow ? SessionPermissionOptionKind.AllowOnce : SessionPermissionOptionKind.RejectOnce;
            }

            _rememberedDecisions[cacheKey] = optionKind;
        }

        private static bool HasOption(PendingPermission permission, SessionPermissionOptionKind optionKind)
        {
            if (permission?.Options == null)
            {
                return false;
            }

            foreach (var option in permission.Options)
            {
                if (option != null && option.Kind == optionKind)
                {
                    return true;
                }
            }

            return false;
        }

        private static string SelectOptionId(PendingPermission permission, SessionPermissionOptionKind preferredKind, bool allowFallback)
        {
            if (permission?.Options == null || permission.Options.Count == 0)
            {
                return null;
            }

            foreach (var option in permission.Options)
            {
                if (option != null && option.Kind == preferredKind && !string.IsNullOrEmpty(option.Id))
                {
                    return option.Id;
                }
            }

            if (allowFallback)
            {
                foreach (var option in permission.Options)
                {
                    if (option == null || string.IsNullOrEmpty(option.Id))
                    {
                        continue;
                    }

                    if (option.Kind == SessionPermissionOptionKind.AllowAlways || option.Kind == SessionPermissionOptionKind.AllowOnce)
                    {
                        return option.Id;
                    }
                }
            }
            else
            {
                foreach (var option in permission.Options)
                {
                    if (option == null || string.IsNullOrEmpty(option.Id))
                    {
                        continue;
                    }

                    if (option.Kind == SessionPermissionOptionKind.RejectAlways || option.Kind == SessionPermissionOptionKind.RejectOnce || option.Kind == SessionPermissionOptionKind.Cancelled)
                    {
                        return option.Id;
                    }
                }
            }

            return permission.Options[0]?.Id;
        }
    }
}
