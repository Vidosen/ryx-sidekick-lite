// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Auth;
using Ryx.Sidekick.Editor.Infrastructure;
using Ryx.Sidekick.Editor.Infrastructure.Dialogs;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using UnityEditor;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Presentation.Controllers
{
    internal sealed class AuthController : IDisposable
    {
        private readonly IAuthService _authService;
        private readonly IEditorDialogService _dialogService;
        private readonly IEditorScheduler _scheduler;
        private readonly IClipboardService _clipboardService;
        private readonly ISettingsStore _settingsStore;
        private readonly IProviderCatalog _providerCatalog;
        private ILoginOverlayView _loginOverlayView;
        private OAuthUrls _pendingOAuthUrls;
        private bool _disposed;

        public event Action ProviderSetupRequested;

        public AuthController(
            IAuthService authService,
            IEditorDialogService dialogService = null,
            IEditorScheduler scheduler = null,
            IClipboardService clipboardService = null,
            ISettingsStore settingsStore = null,
            IProviderCatalog providerCatalog = null)
        {
            _authService = authService;
            _dialogService = dialogService ?? new UnityEditorDialogService();
            _scheduler = scheduler ?? new UnityEditorScheduler();
            _clipboardService = clipboardService ?? new UnityClipboardService();
            _settingsStore = settingsStore;
            _providerCatalog = providerCatalog;
            if (_authService != null)
            {
                _authService.OnAuthStatusChanged += HandleAuthStatusChanged;
            }
        }

        public void BindView(ILoginOverlayView loginOverlayView)
        {
            if (_disposed && loginOverlayView != null)
            {
                return;
            }

            if (_loginOverlayView != null)
            {
                _loginOverlayView.ClaudeAiLoginRequested -= HandleClaudeAiLoginRequested;
                _loginOverlayView.ConsoleLoginRequested -= HandleConsoleLoginRequested;
                _loginOverlayView.ThirdPartyDocsRequested -= OpenThirdPartyAuthDocs;
                _loginOverlayView.CopyOAuthUrlRequested -= CopyOAuthUrlToClipboard;
                _loginOverlayView.ContinueOAuthRequested -= SubmitManualAuthCode;
                _loginOverlayView.BackRequested -= HandleOAuthBackRequested;
                _loginOverlayView.LoginAgainRequested -= ShowLoginSelectionScreen;
                _loginOverlayView.ProviderSetupRequested -= HandleProviderSetupRequested;
            }

            _loginOverlayView = loginOverlayView;

            if (_loginOverlayView != null)
            {
                _loginOverlayView.ClaudeAiLoginRequested += HandleClaudeAiLoginRequested;
                _loginOverlayView.ConsoleLoginRequested += HandleConsoleLoginRequested;
                _loginOverlayView.ThirdPartyDocsRequested += OpenThirdPartyAuthDocs;
                _loginOverlayView.CopyOAuthUrlRequested += CopyOAuthUrlToClipboard;
                _loginOverlayView.ContinueOAuthRequested += SubmitManualAuthCode;
                _loginOverlayView.BackRequested += HandleOAuthBackRequested;
                _loginOverlayView.LoginAgainRequested += ShowLoginSelectionScreen;
                _loginOverlayView.ProviderSetupRequested += HandleProviderSetupRequested;
            }

            if (!_disposed)
            {
                UpdateAuthUI();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_authService != null)
            {
                _authService.OnAuthStatusChanged -= HandleAuthStatusChanged;
            }

            _pendingOAuthUrls = null;
            BindView(null);
        }

        public bool RequiresAuth()
        {
            return IsActiveProviderOAuthBuiltIn() && _authService?.RequiresAuth() == true;
        }

        private void HandleAuthStatusChanged(AuthStatus status)
        {
            if (_disposed)
            {
                return;
            }

            _scheduler.Schedule(() =>
            {
                if (!_disposed)
                {
                    UpdateAuthUI();
                }
            });
        }

        public void UpdateAuthUI()
        {
            if (_disposed)
            {
                return;
            }

            var status = _authService?.GetAuthStatus() ?? AuthStatus.NotAuthenticated();
            UpdateLoginOverlayVisibility(status);
        }

        private void UpdateLoginOverlayVisibility(AuthStatus status)
        {
            if (_disposed)
            {
                return;
            }

            // Show login overlay when not authenticated (and not currently authenticating via overlay)
            var shouldShowOverlay = IsActiveProviderOAuthBuiltIn()
                                    && !status.IsAuthenticated
                                    && status.State != AuthState.Authenticating;
            var screen = _pendingOAuthUrls == null
                ? LoginOverlayScreen.AuthLost
                : LoginOverlayScreen.OAuth;

            _loginOverlayView?.Render(new LoginOverlayViewState(
                isVisible: shouldShowOverlay,
                screen: screen,
                manualRedirectUrl: _pendingOAuthUrls?.ManualRedirectUrl ?? string.Empty,
                oauthCode: string.Empty));
        }

        public async void HandleAuthButtonClick()
        {
            try
            {
                if (_disposed)
                {
                    return;
                }

                var status = _authService?.GetAuthStatus();

                if (status?.Method == AuthMethod.ThirdParty)
                {
                    OpenThirdPartyAuthDocs();
                    return;
                }

                if (status?.IsAuthenticated == true)
                {
                    await _authService.LogoutAsync();
                }
                else
                {
                    ShowLoginOptions();
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                //ignore
            }
        }

        public void ShowLoginOptions()
        {
            if (_disposed)
            {
                return;
            }

            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Login with Claude.ai"), false, () => StartLogin(AuthMethod.ClaudeAi));
            menu.AddItem(new GUIContent("Login with Anthropic Console"), false, () => StartLogin(AuthMethod.Console));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Enter API Key..."), false, ShowApiKeyDialog);

            menu.ShowAsContext();
        }

        private async void StartLogin(AuthMethod method)
        {
            try
            {
                var result = await _authService.LoginAsync(method, urls =>
                {
                    if (_disposed)
                    {
                        return;
                    }

                    Application.OpenURL(urls.AutomaticRedirectUrl);

                    _scheduler.Schedule(() =>
                    {
                        if (_disposed)
                        {
                            return;
                        }

                        if (!_dialogService.DisplayDialog(
                                "Complete Login",
                                "A browser window has opened for authentication.\n\n" +
                                "If the browser doesn't open, copy this URL:\n" +
                                urls.ManualRedirectUrl + "\n\n" +
                                "After completing login, the window will automatically close.",
                                "Done",
                                "Enter Code Manually"))
                        {
                            var code = EditorInputDialog.Show("Enter Auth Code", "Paste the authentication code:", "");
                            if (!string.IsNullOrEmpty(code))
                            {
                                var manualResult = _authService.HandleManualAuthCode(code);
                                if (!manualResult.Success && !_disposed)
                                {
                                    _dialogService.DisplayDialog("Invalid Authorization Code", manualResult.ErrorMessage, "OK");
                                }
                            }
                        }
                    });
                });

                if (!_disposed && !result.Success)
                {
                    HandleLoginFailure(result, returnToSelection: false);
                }
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    Debug.LogError($"[Ryx Sidekick] Login error: {ex.Message}");
                    _dialogService.DisplayDialog("Login Error", ex.Message, "OK");
                }
            }
        }

        private void ShowApiKeyDialog()
        {
            var apiKey = EditorInputDialog.Show("Enter API Key", "Paste your Anthropic API key:", "");
            if (!string.IsNullOrEmpty(apiKey))
            {
                if (_authService.SaveApiKey(apiKey))
                {
                    Debug.Log("[Ryx Sidekick] API key saved successfully");
                }
                else
                {
                    _dialogService.DisplayDialog("Error", "Failed to save API key", "OK");
                }
            }
        }

        public void ShowLoginSelectionScreen()
        {
            if (_disposed)
            {
                return;
            }

            _pendingOAuthUrls = null;
            _loginOverlayView?.Render(new LoginOverlayViewState(
                isVisible: true,
                screen: LoginOverlayScreen.Selection,
                manualRedirectUrl: string.Empty,
                oauthCode: string.Empty));
        }

        private void HandleProviderSetupRequested()
        {
            if (_disposed)
            {
                return;
            }

            _pendingOAuthUrls = null;
            _loginOverlayView?.Render(new LoginOverlayViewState(
                isVisible: false,
                screen: LoginOverlayScreen.AuthLost,
                manualRedirectUrl: string.Empty,
                oauthCode: string.Empty));
            ProviderSetupRequested?.Invoke();
        }

        private void ShowOAuthScreen(OAuthUrls urls)
        {
            if (_disposed || urls == null)
            {
                return;
            }

            _pendingOAuthUrls = urls;

            _loginOverlayView?.Render(new LoginOverlayViewState(
                isVisible: true,
                screen: LoginOverlayScreen.OAuth,
                manualRedirectUrl: urls.ManualRedirectUrl,
                oauthCode: string.Empty));
        }

        public async void StartLoginFromOverlay(AuthMethod method)
        {
            try
            {
                var result = await _authService.LoginAsync(method, urls =>
                {
                    if (_disposed)
                    {
                        return;
                    }

                    Application.OpenURL(urls.AutomaticRedirectUrl);
                    _scheduler.Schedule(() =>
                    {
                        if (!_disposed)
                        {
                            ShowOAuthScreen(urls);
                        }
                    });
                });

                if (_disposed)
                {
                    return;
                }

                if (result.Success)
                {
                    _pendingOAuthUrls = null;
                }
                else
                {
                    HandleLoginFailure(result, returnToSelection: true);
                }
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    Debug.LogError($"[Ryx Sidekick] Login error: {ex.Message}");
                    _dialogService.DisplayDialog("Login Error", ex.Message, "OK");
                    ShowLoginSelectionScreen();
                }
            }
        }

        public void OpenThirdPartyAuthDocs()
        {
            if (_disposed)
            {
                return;
            }

            Application.OpenURL("https://code.claude.com/docs/en/third-party-integrations#cloud-providers");
        }

        public void CopyOAuthUrlToClipboard()
        {
            if (!_disposed && _pendingOAuthUrls != null)
            {
                _clipboardService.Text = _pendingOAuthUrls.ManualRedirectUrl;
                Debug.Log("[Ryx Sidekick] OAuth URL copied to clipboard");
            }
        }

        public void SubmitManualAuthCode()
        {
            if (_disposed)
            {
                return;
            }

            var code = _loginOverlayView?.OAuthCode?.Trim();
            if (!string.IsNullOrEmpty(code))
            {
                var result = _authService?.HandleManualAuthCode(code);
                if (result?.Success == false)
                {
                    _dialogService.DisplayDialog("Invalid Authorization Code", result.ErrorMessage, "OK");
                }
            }
        }

        private void HandleLoginFailure(AuthResult result, bool returnToSelection)
        {
            if (_disposed || result == null)
            {
                return;
            }

            if (result.FailureKind == AuthFailureKind.Cancelled)
            {
                if (returnToSelection)
                {
                    ShowLoginSelectionScreen();
                }
                return;
            }

            Debug.LogWarning($"[Ryx Sidekick] Login failed: {result.ErrorMessage}");
            _dialogService.DisplayDialog("Login Failed", result.ErrorMessage, "OK");
            if (returnToSelection)
            {
                ShowLoginSelectionScreen();
            }
        }

        private void HandleClaudeAiLoginRequested()
        {
            StartLoginFromOverlay(AuthMethod.ClaudeAi);
        }

        private void HandleConsoleLoginRequested()
        {
            StartLoginFromOverlay(AuthMethod.Console);
        }

        private void HandleOAuthBackRequested()
        {
            if (_disposed)
            {
                return;
            }

            _authService?.CancelLogin();
            ShowLoginSelectionScreen();
        }

        private bool IsActiveProviderOAuthBuiltIn()
        {
            if (_settingsStore == null || _providerCatalog == null)
            {
                return true;
            }

            try
            {
                var provider = _providerCatalog.GetProvider(_settingsStore.ProviderId);
                return provider?.Metadata?.AuthKind == AuthOnboardingKind.OAuthBuiltIn;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Ryx Sidekick] Failed to resolve active provider auth kind: {ex.Message}");
                return true;
            }
        }
    }
}
