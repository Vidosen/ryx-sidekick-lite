// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.Infrastructure;
using Ryx.Sidekick.Editor.Presentation.Shell;
using UnityEditor;

namespace Ryx.Sidekick.Editor.Presentation.Controllers
{
    /// <summary>
    /// Coordinates AssetDatabase.Refresh() based on <see cref="AssetRefreshMode"/> and observed tool activity.
    /// </summary>
    internal sealed class AssetRefreshController
    {
        private readonly IEditorScheduler _scheduler;
        private INotificationPresenter _notificationPresenter;

        private bool _refreshPending;
        private bool _refreshScheduled;
        private readonly System.Collections.Generic.HashSet<string> _refreshTrackedTools = new();

        public bool RefreshPending => _refreshPending;

        public AssetRefreshController(IEditorScheduler scheduler = null)
        {
            _scheduler = scheduler ?? new UnityEditorScheduler();
        }

        public void BindNotificationPresenter(INotificationPresenter notificationPresenter)
        {
            _notificationPresenter = notificationPresenter;
        }

        private static AssetRefreshMode CurrentRefreshMode => SidekickSettings.instance.AssetRefreshMode;

        private static bool IsEditOrWriteTool(ToolUse toolUse)
        {
            var toolKind = ToolPresentationCatalog.GetEffectiveKind(toolUse);
            return toolKind is ToolKind.Edit or ToolKind.Write;
        }

        private static bool IsRefreshTrackingEnabled() => CurrentRefreshMode != AssetRefreshMode.Off;
        private static bool IsManualRefreshMode() => CurrentRefreshMode == AssetRefreshMode.Manual;
        private static bool IsAfterAssistantRefreshMode() => CurrentRefreshMode == AssetRefreshMode.AfterAssistantCompletes;
        private static bool IsAfterToolRefreshMode() => CurrentRefreshMode == AssetRefreshMode.AfterEditAndWriteTools;

        private void PushRefreshHintToView()
        {
            var shouldShow = IsManualRefreshMode() && _refreshPending;
            _notificationPresenter?.ShowRefreshHint(shouldShow);
        }

        private void MarkRefreshPending()
        {
            if (!IsRefreshTrackingEnabled()) return;
            _refreshPending = true;
            PushRefreshHintToView();
        }

        private void ClearRefreshPending()
        {
            _refreshPending = false;
            PushRefreshHintToView();
        }

        public void OnToolUse(ToolUse toolUse)
        {
            // Note: We intentionally do NOT mark refresh pending here.
            // OnToolUse fires when permission is requested, not when the tool actually runs.
            // We only mark pending in OnToolResult after the edit/write completes.
            if (!IsRefreshTrackingEnabled()) return;
            if (!IsEditOrWriteTool(toolUse)) return;

            _refreshTrackedTools.Remove(toolUse.Id);
        }

        public void OnToolResult(ToolUse tool)
        {
            if (!IsRefreshTrackingEnabled() || tool == null) return;
            if (!IsEditOrWriteTool(tool)) return;

            if (!_refreshTrackedTools.Add(tool.Id)) return;

            // Mark pending now that the tool actually completed (edit/write was performed)
            MarkRefreshPending();

            if (IsAfterToolRefreshMode())
            {
                ScheduleAssetRefresh();
            }
        }

        public void OnStreamComplete()
        {
            if (!IsRefreshTrackingEnabled())
            {
                _refreshTrackedTools.Clear();
                return;
            }

            if (IsAfterAssistantRefreshMode() && _refreshPending)
            {
                ScheduleAssetRefresh();
            }

            if (IsManualRefreshMode())
            {
                PushRefreshHintToView();
            }

            _refreshTrackedTools.Clear();
        }

        public void TriggerManualRefresh()
        {
            if (!IsManualRefreshMode() || !_refreshPending) return;
            ScheduleAssetRefresh();
        }

        private void ScheduleAssetRefresh()
        {
            if (!IsRefreshTrackingEnabled()) return;
            if (_refreshScheduled) return;

            _refreshScheduled = true;
            _scheduler.Schedule(() =>
            {
                _refreshScheduled = false;
                try
                {
                    AssetDatabase.Refresh();
                }
                catch
                {
                    // ignore (Unity can throw if called during import/compile)
                }
                ClearRefreshPending();
            });
        }
    }
}
