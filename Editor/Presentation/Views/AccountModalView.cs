// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Presentation.Shell.Modals;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    /// <summary>
    /// Modal-layer implementation of <see cref="ISidekickAccountLoginView"/>.
    /// Renders the three account screens (SignedOut / SigningIn / SignedIn) as a
    /// centered popup over the host surface via <see cref="SidekickModalLayer"/>.
    /// No App UI Panel required — works in both the chat window and Project Settings.
    /// </summary>
    internal sealed class AccountModalView : ISidekickAccountLoginView, IDisposable
    {
        private readonly SidekickModalLayer _layer;
        private SidekickModalHandle _handle;
        private SidekickAccountScreen _currentScreen;
        private bool _suppressDismissEvent;
        private bool _disposed;

        // Element refs for the active screen (refreshed on each Render).
        private TextField _manualCodeInput;
        private TextField _codeInput;

        public event Action SignInRequested;
        public event Action SignOutRequested;
        public event Action SubmitCodeRequested;
        public event Action CancelRequested;

        public AccountModalView(SidekickModalLayer layer)
        {
            _layer = layer;
        }

        public string ManualCode
        {
            get
            {
                var fromSigning = _manualCodeInput?.value;
                if (!string.IsNullOrWhiteSpace(fromSigning))
                    return fromSigning;
                return _codeInput?.value ?? string.Empty;
            }
        }

        public void Render(SidekickAccountLoginViewState state)
        {
            if (_disposed) return;

            if (!state.IsVisible)
            {
                DismissHandle();
                return;
            }

            // If already open for the same screen, just refresh data.
            if (_handle != null && _handle.IsOpen && _currentScreen == state.Screen)
            {
                RefreshScreenData(state);
                return;
            }

            // Dismiss any existing handle before rebuilding.
            DismissHandle();

            _currentScreen = state.Screen;
            _manualCodeInput = null;
            _codeInput = null;

            VisualElement content;
            bool outsideClick;
            bool keyboard;

            switch (state.Screen)
            {
                case SidekickAccountScreen.SigningIn:
                    content = BuildSigningInScreen(state);
                    outsideClick = false;
                    keyboard = false;
                    break;
                case SidekickAccountScreen.SignedIn:
                    content = BuildSignedInScreen(state);
                    outsideClick = true;
                    keyboard = true;
                    break;
                default: // SignedOut
                    content = BuildSignedOutScreen(state);
                    outsideClick = true;
                    keyboard = true;
                    break;
            }

            _handle = _layer.Show(content, new SidekickModalOptions(outsideClick, keyboard, "sk-modal-account-content"));
            _handle.Dismissed += OnHandleDismissed;
        }

        private void RefreshScreenData(SidekickAccountLoginViewState state)
        {
            // No mutable data in the current screens that needs live refresh within the same screen.
            // Screen transitions are handled by a full rebuild.
        }

        private VisualElement BuildSignedOutScreen(SidekickAccountLoginViewState state)
        {
            var card = NewCard();
            card.Add(NewCloseButton());

            var brand = new VisualElement();
            brand.AddToClassList("sk-login-brand");
            brand.Add(new Label("Sidekick Account") { name = "account-title" }.WithClass("sk-login-title"));
            card.Add(brand);

            var divider = new VisualElement();
            divider.AddToClassList("sk-login-divider");
            card.Add(divider);

            var desc = new Label("Sign in to your Ryx Sidekick account to manage your subscription and unlock Pro features.");
            desc.AddToClassList("sk-login-description");
            card.Add(desc);

            var buttons = new VisualElement();
            buttons.AddToClassList("sk-login-buttons");

            var signInBtn = new Button(() => SignInRequested?.Invoke());
            signInBtn.AddToClassList("sk-login-btn");
            signInBtn.AddToClassList("sk-login-btn-primary");
            signInBtn.Add(new Label("Sign in with browser"));
            buttons.Add(signInBtn);
            card.Add(buttons);

            var codeLabel = new Label("Or paste an authorization code manually:");
            codeLabel.AddToClassList("sk-oauth-code-label");
            card.Add(codeLabel);

            _codeInput = new TextField();
            _codeInput.name = "account-code-input";
            _codeInput.AddToClassList("sk-oauth-code-input");
            card.Add(_codeInput);

            var codeButtons = new VisualElement();
            codeButtons.AddToClassList("sk-login-buttons");

            var submitBtn = new Button(() => SubmitCodeRequested?.Invoke());
            submitBtn.AddToClassList("sk-login-btn");
            submitBtn.AddToClassList("sk-login-btn-secondary");
            submitBtn.Add(new Label("Submit code"));
            codeButtons.Add(submitBtn);

            var cancelBtn = new Button(() => CancelRequested?.Invoke());
            cancelBtn.AddToClassList("sk-login-btn");
            cancelBtn.AddToClassList("sk-login-btn-secondary");
            cancelBtn.Add(new Label("Cancel"));
            codeButtons.Add(cancelBtn);
            card.Add(codeButtons);

            return card;
        }

        private VisualElement BuildSigningInScreen(SidekickAccountLoginViewState state)
        {
            var card = NewCard();
            // No close button on SigningIn — deliberate close via Cancel only.

            var brand = new VisualElement();
            brand.AddToClassList("sk-login-brand");
            brand.Add(new Label("Sidekick Account").WithClass("sk-login-title"));
            card.Add(brand);

            var divider = new VisualElement();
            divider.AddToClassList("sk-login-divider");
            card.Add(divider);

            var title = new Label("Continue in your browser…");
            title.AddToClassList("sk-oauth-title");
            card.Add(title);

            var subtitle = new Label("A browser window has opened. Complete the sign-in there, or paste an authorization code below.");
            subtitle.AddToClassList("sk-oauth-subtitle");
            card.Add(subtitle);

            var codeLabel = new Label("Or paste your authorization code manually:");
            codeLabel.AddToClassList("sk-oauth-code-label");
            card.Add(codeLabel);

            _manualCodeInput = new TextField();
            _manualCodeInput.name = "account-manual-code-input";
            _manualCodeInput.AddToClassList("sk-oauth-code-input");
            card.Add(_manualCodeInput);

            var buttons = new VisualElement();
            buttons.AddToClassList("sk-login-buttons");

            var submitBtn = new Button(() => SubmitCodeRequested?.Invoke());
            submitBtn.AddToClassList("sk-login-btn");
            submitBtn.AddToClassList("sk-login-btn-primary");
            submitBtn.Add(new Label("Submit code"));
            buttons.Add(submitBtn);

            var cancelBtn = new Button(() => CancelRequested?.Invoke());
            cancelBtn.AddToClassList("sk-login-btn");
            cancelBtn.AddToClassList("sk-login-btn-secondary");
            cancelBtn.Add(new Label("Cancel"));
            buttons.Add(cancelBtn);
            card.Add(buttons);

            return card;
        }

        private VisualElement BuildSignedInScreen(SidekickAccountLoginViewState state)
        {
            var card = NewCard();
            card.Add(NewCloseButton());

            var brand = new VisualElement();
            brand.AddToClassList("sk-login-brand");
            brand.Add(new Label("Sidekick Account").WithClass("sk-login-title"));
            card.Add(brand);

            var divider = new VisualElement();
            divider.AddToClassList("sk-login-divider");
            card.Add(divider);

            if (!string.IsNullOrEmpty(state.Email))
            {
                var emailLabel = new Label($"Email: {state.Email}");
                emailLabel.AddToClassList("sk-login-description");
                card.Add(emailLabel);
            }

            if (!string.IsNullOrEmpty(state.Plan))
            {
                var planLabel = new Label($"Plan: {state.Plan}");
                planLabel.AddToClassList("sk-login-description");
                card.Add(planLabel);
            }

            if (state.Seats > 0)
            {
                var seatsLabel = new Label($"Seats: {state.Seats}");
                seatsLabel.AddToClassList("sk-login-description");
                card.Add(seatsLabel);
            }

            var buttons = new VisualElement();
            buttons.AddToClassList("sk-login-buttons");
            var signOutBtn = new Button(() => SignOutRequested?.Invoke());
            signOutBtn.AddToClassList("sk-login-btn");
            signOutBtn.AddToClassList("sk-login-btn-secondary");
            signOutBtn.Add(new Label("Sign out"));
            buttons.Add(signOutBtn);
            card.Add(buttons);

            return card;
        }

        private Button NewCloseButton()
        {
            var btn = new Button(() => CancelRequested?.Invoke()) { text = "✕" };
            btn.AddToClassList("sk-paywall-close");
            return btn;
        }

        private static VisualElement NewCard()
        {
            var card = new VisualElement();
            card.AddToClassList("sk-login-screen");
            return card;
        }

        private void DismissHandle()
        {
            if (_handle == null || !_handle.IsOpen) return;
            _suppressDismissEvent = true;
            _handle.Dismiss(SidekickModalDismissType.Manual);
        }

        private void OnHandleDismissed(SidekickModalDismissType type)
        {
            _handle = null;
            if (_suppressDismissEvent)
            {
                _suppressDismissEvent = false;
                return;
            }
            // User-driven dismiss (outside-click / ESC / close button) — cancel the flow.
            CancelRequested?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DismissHandle();
        }
    }
}
