// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using Ryx.Sidekick.Editor.Presentation.Views;
using UnityEditor;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Presenters
{
    internal sealed class StatusBarActionsPresenter : IDisposable
    {
        private readonly IStatusBarView _statusBarView;
        private readonly INotificationPresenter _notificationPresenter;
        private readonly VisualElement _contextUsage;
        private readonly AssetRefreshController _assetRefreshController;
        private readonly Unity.AppUI.UI.Pressable _contextUsagePressable;
        private ComposerViewModel _composerViewModel;
        private bool _disposed;

        public StatusBarActionsPresenter(
            IStatusBarView statusBarView,
            INotificationPresenter notificationPresenter,
            VisualElement contextUsage,
            AssetRefreshController assetRefreshController)
        {
            _statusBarView = statusBarView;
            _notificationPresenter = notificationPresenter;
            _contextUsage = contextUsage;
            _assetRefreshController = assetRefreshController;

            if (_statusBarView != null)
            {
                _statusBarView.McpRequested += HandleMcpButtonClick;
            }

            if (_notificationPresenter != null)
            {
                _notificationPresenter.RefreshClicked += HandleRefreshClicked;
            }

            if (_contextUsage != null)
            {
                _contextUsagePressable = new Unity.AppUI.UI.Pressable();
                _contextUsagePressable.clicked += HandleContextUsageClicked;
                _contextUsage.AddManipulator(_contextUsagePressable);
            }
        }

        public void RebindProviderScope(ComposerViewModel composerViewModel)
        {
            _composerViewModel = composerViewModel;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_statusBarView != null)
            {
                _statusBarView.McpRequested -= HandleMcpButtonClick;
            }

            if (_notificationPresenter != null)
            {
                _notificationPresenter.RefreshClicked -= HandleRefreshClicked;
            }

            if (_contextUsage != null && _contextUsagePressable != null)
            {
                _contextUsagePressable.clicked -= HandleContextUsageClicked;
                _contextUsage.RemoveManipulator(_contextUsagePressable);
            }

            _composerViewModel = null;
        }

        private void HandleRefreshClicked()
        {
            _assetRefreshController?.TriggerManualRefresh();
        }

        private void HandleContextUsageClicked()
        {
            _composerViewModel?.RequestCompactCommand.Execute(null);
        }

        private void HandleMcpButtonClick()
        {
            SettingsService.OpenProjectSettings("Project/Sidekick/MCP");
        }
    }
}
