// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.UseCases.Attachments;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Pro;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Presenters
{
    internal sealed class WindowViewBindingPresenter : IDisposable
    {
        private SidekickWindowView _view;
        private SidekickWindowScopeGraph _windowScopeGraph;
        private ConversationController _conversationController;
        private ChatController _chatController;
        private ComposerViewModel _composerViewModel;
        private IPermissionBannerHost _permissionBannerHost;
        private PaywallModalView _paywallModalView;
        private UpdateNotificationViewModel _updateNotificationViewModel;
        private bool _disposed;

        // Guards the auto-shown "you own Pro → install it" nudge to once per editor session. Static so
        // it survives window close/reopen within a session; resets on domain reload (e.g. after the
        // install completes, or a recompile), which is the desired "once per session" cadence.
        private static bool _installNudgeShownThisSession;

        public IComposerView ComposerView => _view?.Composer;

        /// <summary>
        /// The update-notification VM constructed once the App UI panel is live.
        /// Null until <see cref="BindWindowScopeGraphToView"/> has run with a valid view + scope graph.
        /// Used by <see cref="SidekickEditorAppHost"/> to trigger a post-fetch re-evaluation.
        /// </summary>
        public UpdateNotificationViewModel UpdateNotificationViewModel => _updateNotificationViewModel;

        public void BindView(SidekickWindowView view)
        {
            ThrowIfDisposed();

            _view = view;
            _permissionBannerHost = _view?.ChatTimeline != null
                ? new PermissionBannerHost(_view.ChatTimeline)
                : null;

            BindWindowScopeGraphToView();
            BindProviderScopeToView();
        }

        public void BindWindowScopeGraph(SidekickWindowScopeGraph windowScopeGraph)
        {
            ThrowIfDisposed();

            _windowScopeGraph = windowScopeGraph;
            BindWindowScopeGraphToView();
        }

        public void RebindProviderScope(
            ConversationController conversationController,
            ChatController chatController,
            ComposerViewModel composerViewModel)
        {
            ThrowIfDisposed();

            DetachScopeEventHandlers();

            _conversationController = conversationController;
            _chatController = chatController;
            _composerViewModel = composerViewModel;

            BindProviderScopeToView();
            AttachScopeEventHandlers();
        }

        public InputFieldState CaptureInputFieldState(AttachmentController attachmentController)
        {
            return new InputFieldState
            {
                InputText = _view?.InputField?.value ?? string.Empty,
                ContextAttachments = ContextAttachmentSerializer.Serialize(attachmentController?.PendingContextAttachments),
                ImageAttachments = attachmentController?.PendingAttachments?.ToList() ?? new List<ImageAttachment>()
            };
        }

        public void RestoreInputFieldState(InputFieldState state, AttachmentController attachmentController)
        {
            if (state == null)
            {
                return;
            }

            if (_view?.InputField != null && !string.IsNullOrEmpty(state.InputText))
            {
                _view.InputField.value = state.InputText;
            }

            var contextAttachments = ContextAttachmentSerializer.Deserialize(state.ContextAttachments);
            attachmentController?.RestorePendingAttachments(state.ImageAttachments, contextAttachments);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DetachScopeEventHandlers();
            UnbindPaywall();
            UnbindUpdates();
            _view = null;
            _windowScopeGraph = null;
            _conversationController = null;
            _chatController = null;
            _composerViewModel = null;
            _permissionBannerHost = null;
        }

        private void BindWindowScopeGraphToView()
        {
            if (_windowScopeGraph == null || _view == null)
            {
                return;
            }

            _windowScopeGraph.ProviderMenuDisplayBinder?.BindView(_view.ProviderMenu);
            _windowScopeGraph.AssetRefreshController?.BindNotificationPresenter(_view.Notifications);
            _windowScopeGraph.ProviderSelectorViewModel?.BindView(_view.ProviderMenu);
            _windowScopeGraph.AttachmentController?.BindViews(_view.Composer, _view.ImageOverlayView, _view.StatusBar);
            _windowScopeGraph.PermissionController?.BindBannerHost(_permissionBannerHost);
            _windowScopeGraph.PermissionOverlayController?.BindView(_view.PermissionOverlayView);
            _windowScopeGraph.AskUserQuestionController?.BindView(_view.AskUserQuestionView);
            _windowScopeGraph.AuthController?.BindView(_view.LoginOverlayView);
            // MCP rework (B1): the status bar no longer binds/polls McpForUnityController by default — that drove
            // the Coplay "MCP: Connected / Stop" live state and kept the bridge connected on startup. Show a static
            // "MCP settings" shortcut instead (the button opens Project Settings > Sidekick > MCP via McpRequested).
            // Pro live status is layered on later by B6 (MCP-T06-04). McpController stays in the graph for onboarding.
            _view.StatusBar?.SetMcpStatus(
                IndicatorState.Neutral,
                "MCP",
                "Settings",
                buttonVisible: true,
                buttonEnabled: true,
                tooltip: "Open MCP settings");
            _view.ChatTimeline?.SetMessageElementFactory(_windowScopeGraph.MessageElementFactory);
            _windowScopeGraph.ChatTimelineViewModel?.BindView(_view.ChatTimeline);
            _windowScopeGraph.AuthController?.UpdateAuthUI();

            // Bind paywall once — _paywallModalView is set on first successful bind.
            if (_paywallModalView == null)
            {
                BindPaywall(_view.Root, _windowScopeGraph);
            }

            // Bind update-notification once — constructs UpdateNotifier + UpdateNotificationViewModel
            // with the reference view, then fires an immediate Evaluate() off the baked config.
            if (_updateNotificationViewModel == null)
            {
                BindUpdates(_view.Root, _windowScopeGraph);
            }
        }

        private void BindPaywall(VisualElement referenceView, SidekickWindowScopeGraph scopeGraph)
        {
            var paywallVm = scopeGraph?.PaywallViewModel;
            if (paywallVm == null)
            {
                return;
            }

            _paywallModalView = new PaywallModalView(referenceView);
            paywallVm.BindView(_paywallModalView);

            // Entitlement-aware chip: hidden when Pro is installed; "Install Pro" when the user owns
            // Pro but hasn't installed it; "★ Upgrade to Pro" otherwise.
            var access = ResolveProAccess(scopeGraph);
            ApplyProChipState(access);

            // Wire provider-menu locked-feature click → paywall.
            if (_view?.ProviderMenu != null)
            {
                _view.ProviderMenu.LockedFeatureClicked += OnLockedFeatureClicked;
            }

            // Wire status-bar Pro chip click → paywall.
            if (_view?.StatusBar != null)
            {
                _view.StatusBar.ProUpgradeClicked += OnProUpgradeClicked;
            }

            // Auto-show the full-screen "you own Pro → install it" nudge once per session.
            // Deferred via schedule so the panel is live before Modal.Show() runs (bind time may
            // precede attachment to the App UI panel).
            if (access == ProAccessState.OwnedNotInstalled && !_installNudgeShownThisSession)
            {
                _installNudgeShownThisSession = true;
                referenceView?.schedule.Execute(() =>
                {
                    if (!_disposed)
                    {
                        _windowScopeGraph?.PaywallViewModel?.Open(null);
                    }
                });
            }
        }

        private static ProAccessState ResolveProAccess(SidekickWindowScopeGraph scopeGraph)
        {
            var query = scopeGraph?.ResolveProAccessState;
            if (query != null)
            {
                return query.Resolve();
            }

            // Fallback when the query wasn't wired (e.g. minimal test graphs): infer from presence only.
            var presence = scopeGraph?.ProPresence;
            return presence != null && presence.IsInstalled ? ProAccessState.Installed : ProAccessState.Locked;
        }

        private void ApplyProChipState(ProAccessState access)
        {
            var statusBar = _view?.StatusBar;
            if (statusBar == null)
            {
                return;
            }

            switch (access)
            {
                case ProAccessState.Installed:
                    statusBar.SetProChipVisible(false);
                    break;
                case ProAccessState.OwnedNotInstalled:
                    statusBar.SetProChipLabel("Install Pro");
                    statusBar.SetProChipVisible(true);
                    break;
                default:
                    statusBar.SetProChipLabel("★ Upgrade to Pro");
                    statusBar.SetProChipVisible(true);
                    break;
            }
        }

        private void UnbindPaywall()
        {
            if (_view?.ProviderMenu != null)
            {
                _view.ProviderMenu.LockedFeatureClicked -= OnLockedFeatureClicked;
            }

            if (_view?.StatusBar != null)
            {
                _view.StatusBar.ProUpgradeClicked -= OnProUpgradeClicked;
            }

            _paywallModalView?.Dispose();
            _paywallModalView = null;
        }

        private void BindUpdates(VisualElement referenceView, SidekickWindowScopeGraph scopeGraph)
        {
            if (referenceView == null || scopeGraph?.CheckForUpdatesQuery == null)
            {
                return;
            }

            var notifier = new UpdateNotifier(referenceView);
            _updateNotificationViewModel = new UpdateNotificationViewModel(
                scopeGraph.CheckForUpdatesQuery,
                scopeGraph.RemoteConfigSource,
                scopeGraph.ExternalUrlOpener,
                notifier,
                scopeGraph.DismissStore);

            // Evaluate immediately against the baked / already-cached config snapshot.
            // A post-fetch re-evaluation will be triggered by SidekickEditorAppHost once
            // the remote refresh completes (via UpdateNotificationViewModel.Evaluate()).
            try { _updateNotificationViewModel.Evaluate(); }
            catch { /* swallow — toast must not break window init */ }
        }

        private void UnbindUpdates()
        {
            _updateNotificationViewModel?.Dispose();
            _updateNotificationViewModel = null;
        }

        private void OnLockedFeatureClicked(string featureId)
        {
            _windowScopeGraph?.PaywallViewModel?.Open(featureId);
        }

        private void OnProUpgradeClicked()
        {
            _windowScopeGraph?.PaywallViewModel?.Open(null);
        }

        private void BindProviderScopeToView()
        {
            if (_view == null)
            {
                return;
            }

            _conversationController?.BindView(_view.ConversationMenu);
            _chatController?.BindView(_view.Composer);
            _composerViewModel?.BindView(_view.Composer);
            _composerViewModel?.BindAttachmentMenuView(_view.AttachmentMenu);
            _windowScopeGraph?.ProviderSelectorViewModel?.BindView(_view.ProviderMenu);
        }

        private void AttachScopeEventHandlers()
        {
            if (_windowScopeGraph is { PermissionController: not null, AskUserQuestionController: not null })
            {
                _windowScopeGraph.PermissionController.OnAskUserQuestionPermission -= _windowScopeGraph.AskUserQuestionController.HandlePermission;
                _windowScopeGraph.PermissionController.OnAskUserQuestionPermission += _windowScopeGraph.AskUserQuestionController.HandlePermission;
            }

            if (_windowScopeGraph is { PermissionController: not null, PermissionOverlayController: not null })
            {
                _windowScopeGraph.PermissionController.OnPermissionModalRequested -= _windowScopeGraph.PermissionOverlayController.Enqueue;
                _windowScopeGraph.PermissionController.OnPermissionModalRequested += _windowScopeGraph.PermissionOverlayController.Enqueue;
            }

            if (_chatController != null)
            {
                _chatController.OnContextUsageUpdated -= HandleContextUsageUpdated;
                _chatController.OnContextUsageUpdated += HandleContextUsageUpdated;
            }

            if (_conversationController != null)
            {
                _conversationController.OnConversationUsageLoaded -= HandleConversationUsageLoaded;
                _conversationController.OnConversationUsageLoaded += HandleConversationUsageLoaded;
            }
        }

        public void DetachScopeEventHandlers()
        {
            if (_windowScopeGraph is { PermissionController: not null, AskUserQuestionController: not null })
            {
                _windowScopeGraph.PermissionController.OnAskUserQuestionPermission -= _windowScopeGraph.AskUserQuestionController.HandlePermission;
            }

            if (_windowScopeGraph is { PermissionController: not null, PermissionOverlayController: not null })
            {
                _windowScopeGraph.PermissionController.OnPermissionModalRequested -= _windowScopeGraph.PermissionOverlayController.Enqueue;
            }

            if (_chatController != null)
            {
                _chatController.OnContextUsageUpdated -= HandleContextUsageUpdated;
            }

            if (_conversationController != null)
            {
                _conversationController.OnConversationUsageLoaded -= HandleConversationUsageLoaded;
            }
        }

        private void HandleContextUsageUpdated(int usedTokens, int contextWindow)
        {
            _view?.StatusBar?.UpdateContextUsage(usedTokens, contextWindow);
        }

        private void HandleConversationUsageLoaded(int usedTokens, int contextWindow)
        {
            _view?.StatusBar?.UpdateContextUsage(usedTokens, contextWindow);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WindowViewBindingPresenter));
            }
        }
    }
}
