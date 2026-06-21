// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor.Presentation.ViewModels
{
    internal static class AttachmentMenuOptionIds
    {
        public const string AddSelection = "add-selection";
        public const string BrowseFiles = "browse-files";
        public const string ScreenshotSceneView = "screenshot-scene";
        public const string ScreenshotGameView = "screenshot-game";
    }

    internal readonly struct AttachmentMenuItemViewState : IEquatable<AttachmentMenuItemViewState>
    {
        public string Id { get; }
        public string Label { get; }
        public string IconName { get; }
        public bool IsEnabled { get; }

        public AttachmentMenuItemViewState(string id, string label, string iconName, bool isEnabled)
        {
            Id = id ?? string.Empty;
            Label = label ?? string.Empty;
            IconName = iconName ?? string.Empty;
            IsEnabled = isEnabled;
        }

        public bool Equals(AttachmentMenuItemViewState other)
        {
            return string.Equals(Id, other.Id, StringComparison.Ordinal)
                && string.Equals(Label, other.Label, StringComparison.Ordinal)
                && string.Equals(IconName, other.IconName, StringComparison.Ordinal)
                && IsEnabled == other.IsEnabled;
        }

        public override bool Equals(object obj)
        {
            return obj is AttachmentMenuItemViewState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Id != null ? StringComparer.Ordinal.GetHashCode(Id) : 0;
                hash = (hash * 397) ^ (Label != null ? StringComparer.Ordinal.GetHashCode(Label) : 0);
                hash = (hash * 397) ^ (IconName != null ? StringComparer.Ordinal.GetHashCode(IconName) : 0);
                hash = (hash * 397) ^ IsEnabled.GetHashCode();
                return hash;
            }
        }
    }
}
