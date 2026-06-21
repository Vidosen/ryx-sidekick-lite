// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Models;
using Unity.AppUI.Redux;

namespace Ryx.Sidekick.Editor.Presentation.State
{
    [Serializable]
    internal sealed class PermissionQueueSummaryPayload
    {
        internal PermissionQueueSummaryPayload(
            int pendingCount,
            string currentToolUseId,
            string currentToolName,
            ToolKind currentToolKind,
            string currentPathOrCommand,
            bool hasActiveRequest)
        {
            PendingCount = pendingCount;
            CurrentToolUseId = currentToolUseId ?? string.Empty;
            CurrentToolName = currentToolName ?? string.Empty;
            CurrentToolKind = currentToolKind;
            CurrentPathOrCommand = currentPathOrCommand ?? string.Empty;
            HasActiveRequest = hasActiveRequest;
        }

        public int PendingCount { get; }

        public string CurrentToolUseId { get; }

        public string CurrentToolName { get; }

        public ToolKind CurrentToolKind { get; }

        public string CurrentPathOrCommand { get; }

        public bool HasActiveRequest { get; }
    }

    internal static class PermissionStateActions
    {
        internal static readonly ActionCreator<PermissionQueueSummaryPayload> PermissionQueueUpdated =
            new("permission/PermissionQueueUpdated");

        internal static readonly ActionCreator PermissionQueueCleared =
            new("permission/PermissionQueueCleared");
    }
}
