// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Presentation.ViewModels
{
    internal struct PermissionRequestViewState
    {
        public string IconName { get; set; }
        public string Title { get; set; }
        public string CounterText { get; set; }
        public string ToolName { get; set; }
        public string PathText { get; set; }
        public string CommandText { get; set; }
        public string PreviewText { get; set; }
        public string ReasonText { get; set; }
        public bool IsVisible { get; set; }
        public bool IsPreviewExpanded { get; set; }
        public bool CanShowMore { get; set; }

        public static readonly PermissionRequestViewState Hidden = default;
    }
}
