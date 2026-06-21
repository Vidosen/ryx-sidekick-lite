// SPDX-License-Identifier: GPL-3.0-only
using System;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    internal enum SidekickAccountScreen
    {
        SignedOut,
        SigningIn,
        SignedIn
    }

    internal readonly struct SidekickAccountLoginViewState
    {
        public SidekickAccountLoginViewState(
            bool isVisible,
            SidekickAccountScreen screen,
            string email,
            string plan,
            int seats,
            string manualCode,
            string errorMessage)
        {
            IsVisible = isVisible;
            Screen = screen;
            Email = email;
            Plan = plan;
            Seats = seats;
            ManualCode = manualCode;
            ErrorMessage = errorMessage;
        }

        public bool IsVisible { get; }
        public SidekickAccountScreen Screen { get; }
        public string Email { get; }
        public string Plan { get; }
        public int Seats { get; }
        public string ManualCode { get; }
        public string ErrorMessage { get; }
    }

    internal interface ISidekickAccountLoginView
    {
        event Action SignInRequested;

        event Action SignOutRequested;

        event Action SubmitCodeRequested;

        event Action CancelRequested;

        string ManualCode { get; }

        void Render(SidekickAccountLoginViewState state);
    }

    internal sealed class SidekickAccountLoginView : ISidekickAccountLoginView, IDisposable
    {
        private readonly VisualElement _overlay;
        private readonly VisualElement _signedOutScreen;
        private readonly VisualElement _signingInScreen;
        private readonly VisualElement _signedInScreen;
        private readonly Button _signInButton;
        private readonly TextField _codeInput;
        private readonly Button _codeSubmitButton;
        private readonly Button _cancelButton;
        private readonly TextField _manualCodeInput;
        private readonly Button _signingInSubmitButton;
        private readonly Button _signingInCancelButton;
        private readonly Label _emailLabel;
        private readonly Label _planLabel;
        private readonly Label _seatsLabel;
        private readonly Button _signOutButton;
        private bool _disposed;

        public SidekickAccountLoginView(
            VisualElement overlay,
            VisualElement signedOutScreen,
            VisualElement signingInScreen,
            VisualElement signedInScreen,
            Button signInButton,
            TextField codeInput,
            Button codeSubmitButton,
            Button cancelButton,
            TextField manualCodeInput,
            Button signingInSubmitButton,
            Button signingInCancelButton,
            Label emailLabel,
            Label planLabel,
            Label seatsLabel,
            Button signOutButton)
        {
            _overlay = overlay;
            _signedOutScreen = signedOutScreen;
            _signingInScreen = signingInScreen;
            _signedInScreen = signedInScreen;
            _signInButton = signInButton;
            _codeInput = codeInput;
            _codeSubmitButton = codeSubmitButton;
            _cancelButton = cancelButton;
            _manualCodeInput = manualCodeInput;
            _signingInSubmitButton = signingInSubmitButton;
            _signingInCancelButton = signingInCancelButton;
            _emailLabel = emailLabel;
            _planLabel = planLabel;
            _seatsLabel = seatsLabel;
            _signOutButton = signOutButton;

            _signInButton?.RegisterCallback<ClickEvent>(HandleSignInClicked);
            _codeSubmitButton?.RegisterCallback<ClickEvent>(HandleSubmitCodeClicked);
            _cancelButton?.RegisterCallback<ClickEvent>(HandleCancelClicked);
            _signingInSubmitButton?.RegisterCallback<ClickEvent>(HandleSubmitCodeClicked);
            _signingInCancelButton?.RegisterCallback<ClickEvent>(HandleCancelClicked);
            _signOutButton?.RegisterCallback<ClickEvent>(HandleSignOutClicked);
        }

        public event Action SignInRequested;

        public event Action SignOutRequested;

        public event Action SubmitCodeRequested;

        public event Action CancelRequested;

        public string ManualCode
        {
            get
            {
                // Return whichever code input is active (SignedOut screen uses _codeInput,
                // SigningIn screen uses _manualCodeInput).
                var fromSigning = _manualCodeInput?.value;
                if (!string.IsNullOrWhiteSpace(fromSigning))
                {
                    return fromSigning;
                }

                return _codeInput?.value ?? string.Empty;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _signInButton?.UnregisterCallback<ClickEvent>(HandleSignInClicked);
            _codeSubmitButton?.UnregisterCallback<ClickEvent>(HandleSubmitCodeClicked);
            _cancelButton?.UnregisterCallback<ClickEvent>(HandleCancelClicked);
            _signingInSubmitButton?.UnregisterCallback<ClickEvent>(HandleSubmitCodeClicked);
            _signingInCancelButton?.UnregisterCallback<ClickEvent>(HandleCancelClicked);
            _signOutButton?.UnregisterCallback<ClickEvent>(HandleSignOutClicked);
        }

        public void Render(SidekickAccountLoginViewState state)
        {
            if (_overlay != null)
            {
                _overlay.style.display = state.IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_signedOutScreen != null)
            {
                _signedOutScreen.style.display = state.Screen == SidekickAccountScreen.SignedOut
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_signingInScreen != null)
            {
                _signingInScreen.style.display = state.Screen == SidekickAccountScreen.SigningIn
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_signedInScreen != null)
            {
                _signedInScreen.style.display = state.Screen == SidekickAccountScreen.SignedIn
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_emailLabel != null && _emailLabel.text != (state.Email ?? string.Empty))
            {
                _emailLabel.text = !string.IsNullOrEmpty(state.Email)
                    ? $"Email: {state.Email}"
                    : string.Empty;
            }

            if (_planLabel != null)
            {
                var planText = !string.IsNullOrEmpty(state.Plan) ? $"Plan: {state.Plan}" : string.Empty;
                if (_planLabel.text != planText)
                {
                    _planLabel.text = planText;
                }
            }

            if (_seatsLabel != null)
            {
                if (state.Seats > 0)
                {
                    _seatsLabel.text = $"Seats: {state.Seats}";
                    _seatsLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _seatsLabel.style.display = DisplayStyle.None;
                }
            }
        }

        private void HandleSignInClicked(ClickEvent evt)
        {
            SignInRequested?.Invoke();
        }

        private void HandleSignOutClicked(ClickEvent evt)
        {
            SignOutRequested?.Invoke();
        }

        private void HandleSubmitCodeClicked(ClickEvent evt)
        {
            SubmitCodeRequested?.Invoke();
        }

        private void HandleCancelClicked(ClickEvent evt)
        {
            CancelRequested?.Invoke();
        }
    }
}
