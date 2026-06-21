// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.Presentation.State
{
    [Serializable]
    internal sealed class PermissionState : IEquatable<PermissionState>
    {
        internal static readonly PermissionState Default = new(0, string.Empty, string.Empty, ToolKind.Unknown, string.Empty, false);

        internal PermissionState(
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

        public bool Equals(PermissionState other)
        {
            return other != null
                && PendingCount == other.PendingCount
                && string.Equals(CurrentToolUseId, other.CurrentToolUseId, StringComparison.Ordinal)
                && string.Equals(CurrentToolName, other.CurrentToolName, StringComparison.Ordinal)
                && CurrentToolKind == other.CurrentToolKind
                && string.Equals(CurrentPathOrCommand, other.CurrentPathOrCommand, StringComparison.Ordinal)
                && HasActiveRequest == other.HasActiveRequest;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as PermissionState);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = PendingCount;
                hashCode = (hashCode * 397) ^ (CurrentToolUseId != null ? StringComparer.Ordinal.GetHashCode(CurrentToolUseId) : 0);
                hashCode = (hashCode * 397) ^ (CurrentToolName != null ? StringComparer.Ordinal.GetHashCode(CurrentToolName) : 0);
                hashCode = (hashCode * 397) ^ (int)CurrentToolKind;
                hashCode = (hashCode * 397) ^ (CurrentPathOrCommand != null ? StringComparer.Ordinal.GetHashCode(CurrentPathOrCommand) : 0);
                hashCode = (hashCode * 397) ^ HasActiveRequest.GetHashCode();
                return hashCode;
            }
        }
    }
}
