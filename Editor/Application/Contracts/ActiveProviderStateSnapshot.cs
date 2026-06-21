// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor
{
    [Serializable]
    internal sealed class ActiveProviderStateSnapshot : IEquatable<ActiveProviderStateSnapshot>
    {
        internal ActiveProviderStateSnapshot(string providerId, string model, string collaborationMode, string permissionMode, string reasoningEffort = null)
        {
            ProviderId = providerId ?? string.Empty;
            Model = model ?? string.Empty;
            CollaborationMode = collaborationMode ?? string.Empty;
            PermissionMode = permissionMode ?? string.Empty;
            ReasoningEffort = reasoningEffort ?? string.Empty;
        }

        public string ProviderId { get; }

        public string Model { get; }

        public string CollaborationMode { get; }

        public string PermissionMode { get; }
        public string ReasoningEffort { get; }

        public bool Equals(ActiveProviderStateSnapshot other)
        {
            return other != null
                && string.Equals(ProviderId, other.ProviderId, StringComparison.Ordinal)
                && string.Equals(Model, other.Model, StringComparison.Ordinal)
                && string.Equals(CollaborationMode, other.CollaborationMode, StringComparison.Ordinal)
                && string.Equals(PermissionMode, other.PermissionMode, StringComparison.Ordinal)
                && string.Equals(ReasoningEffort, other.ReasoningEffort, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ActiveProviderStateSnapshot);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = ProviderId != null ? StringComparer.Ordinal.GetHashCode(ProviderId) : 0;
                hashCode = (hashCode * 397) ^ (Model != null ? StringComparer.Ordinal.GetHashCode(Model) : 0);
                hashCode = (hashCode * 397) ^ (CollaborationMode != null ? StringComparer.Ordinal.GetHashCode(CollaborationMode) : 0);
                hashCode = (hashCode * 397) ^ (PermissionMode != null ? StringComparer.Ordinal.GetHashCode(PermissionMode) : 0);
                hashCode = (hashCode * 397) ^ (ReasoningEffort != null ? StringComparer.Ordinal.GetHashCode(ReasoningEffort) : 0);
                return hashCode;
            }
        }
    }
}
