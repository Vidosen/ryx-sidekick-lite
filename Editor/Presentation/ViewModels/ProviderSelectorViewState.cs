// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor.Presentation.ViewModels
{
    internal readonly struct ProviderOptionViewState : IEquatable<ProviderOptionViewState>
    {
        public string Id { get; }
        public string DisplayName { get; }
        public bool IsActive { get; }

        /// <summary>True when this row represents a locked Pro-only provider.</summary>
        public bool IsLocked { get; }

        /// <summary>The Pro feature id for locked rows (empty/null for normal rows).</summary>
        public string FeatureId { get; }

        public ProviderOptionViewState(string id, string displayName, bool isActive)
            : this(id, displayName, isActive, isLocked: false, featureId: null)
        {
        }

        public ProviderOptionViewState(string id, string displayName, bool isActive, bool isLocked, string featureId)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            IsActive = isActive;
            IsLocked = isLocked;
            FeatureId = featureId ?? string.Empty;
        }

        public bool Equals(ProviderOptionViewState other)
        {
            return string.Equals(Id, other.Id, StringComparison.Ordinal)
                && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
                && IsActive == other.IsActive
                && IsLocked == other.IsLocked
                && string.Equals(FeatureId, other.FeatureId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ProviderOptionViewState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Id != null ? StringComparer.Ordinal.GetHashCode(Id) : 0;
                hash = (hash * 397) ^ (DisplayName != null ? StringComparer.Ordinal.GetHashCode(DisplayName) : 0);
                hash = (hash * 397) ^ IsActive.GetHashCode();
                hash = (hash * 397) ^ IsLocked.GetHashCode();
                hash = (hash * 397) ^ (FeatureId != null ? StringComparer.Ordinal.GetHashCode(FeatureId) : 0);
                return hash;
            }
        }
    }

    internal readonly struct ModelPresetViewState : IEquatable<ModelPresetViewState>
    {
        public string Name { get; }
        public bool IsActive { get; }
        public string DisplayName { get; }
        public string Description { get; }

        public ModelPresetViewState(string name, bool isActive, string displayName = null, string description = null)
        {
            Name = name ?? string.Empty;
            IsActive = isActive;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Name : displayName;
            Description = description ?? string.Empty;
        }

        public bool Equals(ModelPresetViewState other)
        {
            return string.Equals(Name, other.Name, StringComparison.Ordinal)
                && IsActive == other.IsActive
                && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
                && string.Equals(Description, other.Description, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ModelPresetViewState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Name != null ? StringComparer.Ordinal.GetHashCode(Name) : 0;
                hash = (hash * 397) ^ IsActive.GetHashCode();
                hash = (hash * 397) ^ (DisplayName != null ? StringComparer.Ordinal.GetHashCode(DisplayName) : 0);
                hash = (hash * 397) ^ (Description != null ? StringComparer.Ordinal.GetHashCode(Description) : 0);
                return hash;
            }
        }
    }

    internal readonly struct ReasoningEffortViewState
    {
        public string Value { get; }
        public string Description { get; }
        public bool IsActive { get; }

        public ReasoningEffortViewState(string value, string description, bool isActive)
        {
            Value = value ?? string.Empty;
            Description = description ?? string.Empty;
            IsActive = isActive;
        }
    }
}
