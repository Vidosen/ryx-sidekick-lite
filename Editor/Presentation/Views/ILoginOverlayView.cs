// SPDX-License-Identifier: GPL-3.0-only
using System;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    internal enum LoginOverlayScreen
    {
        Selection,
        OAuth,
        AuthLost
    }

    internal readonly struct LoginOverlayViewState
    {
        public LoginOverlayViewState(bool isVisible, LoginOverlayScreen screen, string manualRedirectUrl, string oauthCode)
        {
            IsVisible = isVisible;
            Screen = screen;
            ManualRedirectUrl = manualRedirectUrl;
            OAuthCode = oauthCode;
        }

        public bool IsVisible { get; }

        public LoginOverlayScreen Screen { get; }

        public string ManualRedirectUrl { get; }

        public string OAuthCode { get; }
    }

    internal interface ILoginOverlayView
    {
        event Action ClaudeAiLoginRequested;

        event Action ConsoleLoginRequested;

        event Action ThirdPartyDocsRequested;

        event Action CopyOAuthUrlRequested;

        event Action ContinueOAuthRequested;

        event Action BackRequested;

        event Action LoginAgainRequested;

        event Action ProviderSetupRequested;

        string OAuthCode { get; }

        void Render(LoginOverlayViewState state);
    }

    internal sealed class LoginOverlayView : ILoginOverlayView, IDisposable
    {
        private readonly VisualElement _container;
        private readonly VisualElement _overlay;
        private readonly VisualElement _selectionScreen;
        private readonly VisualElement _oauthScreen;
        private readonly VisualElement _authLostScreen;
        private readonly Button _claudeAiButton;
        private readonly Button _consoleButton;
        private readonly Button _thirdPartyButton;
        private readonly TextField _oauthUrlField;
        private readonly TextField _oauthCodeInput;
        private readonly Button _copyButton;
        private readonly Button _continueButton;
        private readonly Button _backButton;
        private readonly Button _loginAgainButton;
        private readonly Button _providerSetupButton;
        private bool _disposed;

        public LoginOverlayView(
            VisualElement container,
            VisualElement overlay,
            VisualElement selectionScreen,
            VisualElement oauthScreen,
            VisualElement authLostScreen,
            Button claudeAiButton,
            Button consoleButton,
            Button thirdPartyButton,
            TextField oauthUrlField,
            TextField oauthCodeInput,
            Button copyButton,
            Button continueButton,
            Button backButton,
            Button loginAgainButton,
            Button providerSetupButton)
        {
            _container = container;
            _overlay = overlay;
            _selectionScreen = selectionScreen;
            _oauthScreen = oauthScreen;
            _authLostScreen = authLostScreen;
            _claudeAiButton = claudeAiButton;
            _consoleButton = consoleButton;
            _thirdPartyButton = thirdPartyButton;
            _oauthUrlField = oauthUrlField;
            _oauthCodeInput = oauthCodeInput;
            _copyButton = copyButton;
            _continueButton = continueButton;
            _backButton = backButton;
            _loginAgainButton = loginAgainButton;
            _providerSetupButton = providerSetupButton;

            _claudeAiButton?.RegisterCallback<ClickEvent>(HandleClaudeAiButtonClicked);
            _consoleButton?.RegisterCallback<ClickEvent>(HandleConsoleButtonClicked);
            _thirdPartyButton?.RegisterCallback<ClickEvent>(HandleThirdPartyButtonClicked);
            _copyButton?.RegisterCallback<ClickEvent>(HandleCopyButtonClicked);
            _continueButton?.RegisterCallback<ClickEvent>(HandleContinueButtonClicked);
            _backButton?.RegisterCallback<ClickEvent>(HandleBackButtonClicked);
            _loginAgainButton?.RegisterCallback<ClickEvent>(HandleLoginAgainButtonClicked);
            _providerSetupButton?.RegisterCallback<ClickEvent>(HandleProviderSetupButtonClicked);
        }

        public event Action ClaudeAiLoginRequested;

        public event Action ConsoleLoginRequested;

        public event Action ThirdPartyDocsRequested;

        public event Action CopyOAuthUrlRequested;

        public event Action ContinueOAuthRequested;

        public event Action BackRequested;

        public event Action LoginAgainRequested;

        public event Action ProviderSetupRequested;

        public string OAuthCode => _oauthCodeInput?.value ?? string.Empty;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _claudeAiButton?.UnregisterCallback<ClickEvent>(HandleClaudeAiButtonClicked);
            _consoleButton?.UnregisterCallback<ClickEvent>(HandleConsoleButtonClicked);
            _thirdPartyButton?.UnregisterCallback<ClickEvent>(HandleThirdPartyButtonClicked);
            _copyButton?.UnregisterCallback<ClickEvent>(HandleCopyButtonClicked);
            _continueButton?.UnregisterCallback<ClickEvent>(HandleContinueButtonClicked);
            _backButton?.UnregisterCallback<ClickEvent>(HandleBackButtonClicked);
            _loginAgainButton?.UnregisterCallback<ClickEvent>(HandleLoginAgainButtonClicked);
            _providerSetupButton?.UnregisterCallback<ClickEvent>(HandleProviderSetupButtonClicked);
        }

        public void Render(LoginOverlayViewState state)
        {
            if (_container != null)
            {
                _container.style.display = state.IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_overlay != null)
            {
                _overlay.style.display = state.IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_selectionScreen != null)
            {
                _selectionScreen.style.display = state.Screen == LoginOverlayScreen.Selection
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_oauthScreen != null)
            {
                _oauthScreen.style.display = state.Screen == LoginOverlayScreen.OAuth
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_authLostScreen != null)
            {
                _authLostScreen.style.display = state.Screen == LoginOverlayScreen.AuthLost
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_oauthUrlField != null && _oauthUrlField.value != state.ManualRedirectUrl)
            {
                _oauthUrlField.SetValueWithoutNotify(state.ManualRedirectUrl ?? string.Empty);
            }

            if (_oauthCodeInput != null && _oauthCodeInput.value != state.OAuthCode)
            {
                _oauthCodeInput.SetValueWithoutNotify(state.OAuthCode ?? string.Empty);
            }
        }

        private void HandleClaudeAiButtonClicked(ClickEvent evt)
        {
            ClaudeAiLoginRequested?.Invoke();
        }

        private void HandleConsoleButtonClicked(ClickEvent evt)
        {
            ConsoleLoginRequested?.Invoke();
        }

        private void HandleThirdPartyButtonClicked(ClickEvent evt)
        {
            ThirdPartyDocsRequested?.Invoke();
        }

        private void HandleCopyButtonClicked(ClickEvent evt)
        {
            CopyOAuthUrlRequested?.Invoke();
        }

        private void HandleContinueButtonClicked(ClickEvent evt)
        {
            ContinueOAuthRequested?.Invoke();
        }

        private void HandleBackButtonClicked(ClickEvent evt)
        {
            BackRequested?.Invoke();
        }

        private void HandleLoginAgainButtonClicked(ClickEvent evt)
        {
            LoginAgainRequested?.Invoke();
        }

        private void HandleProviderSetupButtonClicked(ClickEvent evt)
        {
            ProviderSetupRequested?.Invoke();
        }
    }
}
