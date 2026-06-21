// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Presentation.Views;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    internal interface IPermissionBannerHost
    {
        void AddBanner(VisualElement banner);

        VisualElement FindBanner(string name);

        void RefreshToolAutoAccepted(string toolUseId);

        void ScrollToBottom(int delayMs = 50);
    }

    internal sealed class PermissionBannerHost : IPermissionBannerHost
    {
        private readonly IChatTimelineView _chatTimelineView;

        public PermissionBannerHost(IChatTimelineView chatTimelineView)
        {
            _chatTimelineView = chatTimelineView;
        }

        public void AddBanner(VisualElement banner)
        {
            _chatTimelineView?.AddOverlayBanner(banner);
        }

        public VisualElement FindBanner(string name)
        {
            return _chatTimelineView?.FindOverlayBanner(name);
        }

        public void RefreshToolAutoAccepted(string toolUseId)
        {
            // ListView path: rebind the row so the factory re-applies the chip (id is already
            // marked auto-accepted via PermissionController.IsToolUseAutoAccepted).
            _chatTimelineView?.RefreshToolById(toolUseId);
        }

        public void ScrollToBottom(int delayMs = 50)
        {
            _chatTimelineView?.RequestScrollToBottom(delayMs);
        }
    }
}
