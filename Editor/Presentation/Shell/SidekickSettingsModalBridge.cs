// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Infrastructure;
using Ryx.Sidekick.Editor.Infrastructure.Auth;
using Ryx.Sidekick.Editor.Infrastructure.Pro;
using Ryx.Sidekick.Editor.Infrastructure.UnityEditor;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using Ryx.Sidekick.Editor.Presentation.Shell.Modals;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.UseCases.Pro;
using UnityEditor;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    /// <summary>
    /// Presentation-layer <c>[InitializeOnLoad]</c> bridge that listens to
    /// <see cref="SidekickSettingsModalHost"/> events from Infrastructure Settings pages
    /// and creates a local <see cref="SidekickModalLayer"/> + modal views over the
    /// active Settings root element — so Paywall/Account modals open in-place inside
    /// Project Settings rather than stealing focus to the chat window.
    /// </summary>
    [InitializeOnLoad]
    internal static class SidekickSettingsModalBridge
    {
        // The Settings page root the current state belongs to. Lets OnDeactivated ignore a stale
        // deactivation from a page we've already superseded (e.g. General after navigating to MCP).
        private static VisualElement _trackedRoot;

        // Per-activation state (torn down in TeardownCurrent).
        private static SidekickModalLayer _layer;
        private static PaywallModalView _paywallView;
        private static PaywallViewModel _paywallVm;
        private static SidekickAccountController _accountController;
        private static AccountModalView _accountView;
        private static IDisposable _inPlaceToken;
        private static IDisposable _accountToken;

        static SidekickSettingsModalBridge()
        {
            SidekickSettingsModalHost.Activated += OnActivated;
            SidekickSettingsModalHost.Deactivated += OnDeactivated;
            // DetachFromPanelEvent does NOT fire across a domain reload, so tear down here too —
            // otherwise the account controller stays subscribed to the long-lived (process-singleton)
            // SidekickAccountManager and dead controllers accumulate across reloads.
            AssemblyReloadEvents.beforeAssemblyReload += TeardownCurrent;
        }

        private static void OnActivated(VisualElement root)
        {
            // Supersede any previous page cleanly before building for the new one.
            TeardownCurrent();
            if (root == null) return;

            _trackedRoot = root;
            _layer = new SidekickModalLayer(root);

            // Paywall (Buy upsell). The factory wires the baked offline fallback; no installer is
            // passed — the "you own Pro, install it" nudge is window-only (needs the full scope graph).
            var offerQuery = new GetProOfferQuery(RemoteConfigSourceFactory.Create());
            _paywallView = new PaywallModalView(_layer);
            _paywallVm = new PaywallViewModel(offerQuery, new UnityExternalUrlOpener());
            _paywallVm.BindView(_paywallView);

            // Route ProPaywallLauncher.Request() into the in-place modal instead of opening the chat window.
            _inPlaceToken = ProPaywallLauncher.RegisterInPlaceHandler(() =>
            {
                _paywallVm?.Open(ProPaywallLauncher.ConsumePending());
                return true;
            });

            // Account, backed by the process-singleton SidekickAccountManager.
            _accountController = new SidekickAccountController(SidekickAccountManager.Instance, new UnityEditorScheduler());
            _accountView = new AccountModalView(_layer);
            _accountController.BindView(_accountView);
            _accountToken = SidekickSettingsModalHost.RegisterAccountOpener(() => _accountController?.ShowSignIn());
        }

        // Only tear down if the deactivating page is the one we're tracking — a late DetachFromPanel
        // from a superseded page must not destroy the layer we just built for the new page.
        private static void OnDeactivated(VisualElement root)
        {
            if (root == _trackedRoot)
                TeardownCurrent();
        }

        private static void TeardownCurrent()
        {
            _accountToken?.Dispose();
            _accountToken = null;

            _inPlaceToken?.Dispose();
            _inPlaceToken = null;

            // Dispose (not BindView(null)) so the controller unsubscribes from SidekickAccountManager;
            // BindView(null) alone only drops view-event handlers and would leak the status subscription.
            _accountController?.Dispose();
            _accountView?.Dispose();
            _accountController = null;
            _accountView = null;

            _paywallVm?.BindView(null);
            _paywallView?.Dispose();
            _paywallVm = null;
            _paywallView = null;

            _layer?.Dispose();
            _layer = null;

            _trackedRoot = null;
        }
    }
}
