// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.UseCases.Pro;
using UnityEditor;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Presenters
{
    internal sealed class SidekickWindowPresenter : IDisposable
    {
        private static string UxmlPath => SidekickUiConstants.MainWindowUxml;
        private static string LoginOverlayUxmlPath => SidekickUiConstants.LoginOverlayUxmlPath;
        private static string AskUserQuestionOverlayUxmlPath => SidekickUiConstants.AskUserQuestionOverlayUxmlPath;
        private static string PermissionOverlayUxmlPath => SidekickUiConstants.PermissionOverlayUxmlPath;
        private static string OnboardingOverlayUxmlPath => SidekickUiConstants.OnboardingOverlayUxmlPath;
        private static string AssetsPath => SidekickUiConstants.AssetsPath;
        private static string LogoAssetPath => SidekickUiConstants.LogoAssetPath;

        private readonly VisualElement _rootVisualElement;
        private readonly SidekickEditorAppHost _appHost;

        private SidekickAppPanel _appPanel;
        private SidekickWindowView _view;
        private WindowViewBindingPresenter _viewBindingPresenter;
        private ConversationPopupPresenter _conversationPopupPresenter;
        private OnboardingWizardPresenter _onboardingPresenter;
        private ProviderSwitchPresenter _providerSwitchPresenter;
        private ComposerContextAttachmentPresenter _contextAttachmentPresenter;
        private ComposerInputPresenter _composerInputPresenter;
        private CommandPalettePresenter _commandPalettePresenter;
        private StatusBarActionsPresenter _statusBarActionsPresenter;
        private SidekickAccountController _accountController;
        private SidekickAccountLoginView _accountLoginView;
        private EventCallback<ClickEvent> _conversationDropdownClickCallback;
        private EventCallback<ClickEvent> _rootClickCallback;
        private bool _disposed;

        public SidekickWindowPresenter(
            VisualElement rootVisualElement,
            SidekickEditorAppHost appHost)
        {
            _rootVisualElement = rootVisualElement;
            _appHost = appHost;
            _appHost.ProviderScopeChanged += RebindProviderScopePresenters;
            // Paywall requested from outside the window while it is already open (e.g. MCP settings upsell).
            ProPaywallLauncher.OpenRequested += OnPaywallOpenRequested;
        }

        private void OnPaywallOpenRequested()
        {
            _appHost.WindowScopeGraph?.PaywallViewModel?.Open(ProPaywallLauncher.ConsumePending());
        }

        public void CreateGUI()
        {
            DisposeViewPresenters();

            if (!SidekickAppPanel.TryCreate(out _appPanel))
            {
                _rootVisualElement.Clear();
                _rootVisualElement.Add(new Label("Failed to bootstrap App UI panel"));
                return;
            }

            _rootVisualElement.Clear();
            _rootVisualElement.Add(_appPanel.Canvas);

            if (!SidekickWindowView.TryCreate(
                    _appPanel.ContentContainer,
                    UxmlPath,
                    LoginOverlayUxmlPath,
                    AskUserQuestionOverlayUxmlPath,
                    PermissionOverlayUxmlPath,
                    OnboardingOverlayUxmlPath,
                    LogoAssetPath,
                    AssetsPath,
                    out _view))
            {
                return;
            }

            _appPanel.ApplyThemeToStatusBar();

            _viewBindingPresenter = new WindowViewBindingPresenter();
            _viewBindingPresenter.BindView(_view);
            _appHost.BindView(_viewBindingPresenter);

            // Mount the Sidekick Account overlay adjacent to the main window content.
            // Mirror: SidekickWindowView.TryCreate clones LoginOverlay.uxml into login-overlay-container,
            // then WindowViewBindingPresenter.BindWindowScopeGraphToView calls AuthController.BindView.
            _accountController = _appHost.WindowScopeGraph?.AccountController;
            if (_accountController != null)
            {
                _accountLoginView = SidekickAccountOverlayMount.Mount(_appPanel.ContentContainer);
                if (_accountLoginView != null)
                {
                    _accountController.BindView(_accountLoginView);
                }
            }

            InitializeStaticIcons();
            CreateViewPresenters();
            RebindProviderScopePresenters();

            // Cold-open path: a paywall was requested (e.g. MCP settings upsell) while the window was closed.
            var pendingPaywall = ProPaywallLauncher.ConsumePending();
            if (pendingPaywall != null)
                _appHost.WindowScopeGraph?.PaywallViewModel?.Open(pendingPaywall);

            _onboardingPresenter?.InitializeIfNeeded();
            EditorApplication.delayCall -= TryRestoreInputFieldState;
            EditorApplication.delayCall += TryRestoreInputFieldState;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _appHost.ProviderScopeChanged -= RebindProviderScopePresenters;
            ProPaywallLauncher.OpenRequested -= OnPaywallOpenRequested;
            EditorApplication.delayCall -= TryRestoreInputFieldState;
            DisposeViewPresenters();
        }

        private void CreateViewPresenters()
        {
            _conversationPopupPresenter = new ConversationPopupPresenter(_view.DropdownTitle);

            _contextAttachmentPresenter = new ComposerContextAttachmentPresenter(
                _appHost.WindowScopeGraph?.AttachmentController,
                _view.InputField);

            _providerSwitchPresenter = new ProviderSwitchPresenter(
                _view.Root,
                _appHost,
                _appHost.ProviderSelectorViewModel);

            _onboardingPresenter = new OnboardingWizardPresenter(
                _view.Onboarding,
                _appHost.WindowScopeGraph?.AuthService,
                _appHost.WindowScopeGraph?.AuthController,
                _appHost.WindowScopeGraph?.McpController,
                _providerSwitchPresenter.SwitchProviderFromOnboarding);
            _providerSwitchPresenter.SetOnboardingPresenter(_onboardingPresenter);
            if (_appHost.WindowScopeGraph?.AuthController != null)
            {
                _appHost.WindowScopeGraph.AuthController.ProviderSetupRequested += ShowProviderSetupWizard;
            }

            _commandPalettePresenter = new CommandPalettePresenter(
                _contextAttachmentPresenter,
                _appHost.ProviderSelectorViewModel,
                _appHost.WindowScopeGraph?.AttachmentController,
                CreateNewConversation,
                _appHost.WindowScopeGraph?.ProPresence,
                () => _appHost.WindowScopeGraph?.PaywallViewModel?.Open("skills"));
            _commandPalettePresenter.BindView(_view);

            _composerInputPresenter = new ComposerInputPresenter(
                _view,
                _appHost.WindowScopeGraph?.AttachmentController,
                _appHost.ProviderSelectorViewModel,
                _contextAttachmentPresenter,
                () => _commandPalettePresenter?.IsCommandPaletteOpen == true,
                CreateNewConversation);

            _statusBarActionsPresenter = new StatusBarActionsPresenter(
                _view.StatusBar,
                _view.Notifications,
                _view.ContextUsage,
                _appHost.WindowScopeGraph?.AssetRefreshController);

            _conversationDropdownClickCallback = _ => _conversationPopupPresenter?.ToggleConversationPopup();
            _rootClickCallback = evt =>
            {
                if (_appHost.ConversationController?.IsPopupOpen == true
                    && _conversationPopupPresenter?.IsClickInsidePopup(evt) == false)
                {
                    _conversationPopupPresenter.CloseConversationPopup();
                }
            };

            _view.ConversationDropdownButton?.RegisterCallback(_conversationDropdownClickCallback);
            _view.Root?.RegisterCallback(_rootClickCallback);
        }

        private void RebindProviderScopePresenters()
        {
            _conversationPopupPresenter?.RebindProviderScope(
                _appHost.ConversationController,
                _appHost.ChatController);
            _contextAttachmentPresenter?.RebindProviderScope(
                _appHost.ChatController,
                _appHost.ComposerViewModel);
            _commandPalettePresenter?.RebindProviderScope(_appHost.ChatController, _appHost.ActiveSlashCommandSource);
            _composerInputPresenter?.RebindProviderScope(_appHost.ComposerViewModel);
            _statusBarActionsPresenter?.RebindProviderScope(_appHost.ComposerViewModel);
        }

        private void CreateNewConversation()
        {
            _conversationPopupPresenter?.CreateNewConversation();
            _conversationPopupPresenter?.CloseConversationPopup();
        }

        private void InitializeStaticIcons()
        {
            var root = _view?.Root;
            if (root == null)
            {
                return;
            }

            SidekickIconCatalog.ApplyToLabel(
                root.Q<Label>(className: "sk-drop-zone-icon"),
                "ui-attach",
                "+",
                18f);

            SidekickIconCatalog.ApplyToLabel(
                root.Q<Label>(className: "sk-login-external-icon"),
                "ui-external",
                "->",
                12f);

            SidekickIconCatalog.ApplyToLabel(
                root.Q<Button>("oauth-copy-btn")?.Q<Label>(),
                "ui-copy",
                "Copy",
                12f);
        }

        private void TryRestoreInputFieldState()
        {
            if (_disposed)
            {
                return;
            }

            var state = DomainReloadAutoResume.LoadAndClearInputFieldState();
            if (state != null)
            {
                _appHost.RestoreInputFieldState(state);
            }
        }

        private void DisposeViewPresenters()
        {
            // Account overlay: unbind first so the controller stops receiving events,
            // then dispose the view to unregister ClickEvent callbacks from UI Toolkit.
            if (_accountController != null)
            {
                _accountController.BindView(null);
                _accountController = null;
            }

            _accountLoginView?.Dispose();
            _accountLoginView = null;

            if (_view != null)
            {
                _view.LoginOverlayView?.Dispose();

                if (_conversationDropdownClickCallback != null)
                {
                    _view.ConversationDropdownButton?.UnregisterCallback(_conversationDropdownClickCallback);
                    _conversationDropdownClickCallback = null;
                }

                if (_rootClickCallback != null)
                {
                    _view.Root?.UnregisterCallback(_rootClickCallback);
                    _rootClickCallback = null;
                }
            }

            _statusBarActionsPresenter?.Dispose();
            _statusBarActionsPresenter = null;
            _composerInputPresenter?.Dispose();
            _composerInputPresenter = null;
            _commandPalettePresenter?.Dispose();
            _commandPalettePresenter = null;
            _contextAttachmentPresenter?.Dispose();
            _contextAttachmentPresenter = null;
            if (_appHost.WindowScopeGraph?.AuthController != null)
            {
                _appHost.WindowScopeGraph.AuthController.ProviderSetupRequested -= ShowProviderSetupWizard;
            }
            _providerSwitchPresenter?.Dispose();
            _providerSwitchPresenter = null;
            _onboardingPresenter?.Dispose();
            _onboardingPresenter = null;
            _conversationPopupPresenter?.Dispose();
            _conversationPopupPresenter = null;
            _appHost.BindView(null);
            _viewBindingPresenter?.Dispose();
            _viewBindingPresenter = null;
            _view = null;
            _appPanel = null;
        }

        private void ShowProviderSetupWizard()
        {
            _onboardingPresenter?.Show(startStep: 0);
        }
    }
}
