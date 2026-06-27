// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Account;
using Ryx.Sidekick.Editor.Infrastructure;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Presentation.Controllers
{
    /// <summary>
    /// Mediates between <see cref="ISidekickAccountLoginView"/> and <see cref="ISidekickAccountService"/>.
    /// Mirrors the structural pattern of <see cref="AuthController"/>.
    /// </summary>
    internal sealed class SidekickAccountController : IDisposable
    {
        private readonly ISidekickAccountService _accountService;
        private readonly IEditorScheduler _scheduler;
        private ISidekickAccountLoginView _view;
        private bool _overlayVisible;
        private SidekickAccountState _lastNotifiedState = SidekickAccountState.SignedOut;
        private bool _disposed;

        public SidekickAccountController(
            ISidekickAccountService accountService,
            IEditorScheduler scheduler = null)
        {
            _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
            _scheduler = scheduler ?? new UnityEditorScheduler();

            _accountService.OnStatusChanged += HandleStatusChanged;
        }

        /// <summary>
        /// Binds (or unbinds) the overlay view. Mirrors AuthController.BindView.
        /// </summary>
        public void BindView(ISidekickAccountLoginView view)
        {
            if (_disposed && view != null)
            {
                return;
            }

            if (_view != null)
            {
                _view.SignInRequested -= HandleSignInRequested;
                _view.SignOutRequested -= HandleSignOutRequested;
                _view.SubmitCodeRequested -= HandleSubmitCodeRequested;
                _view.CancelRequested -= HandleCancelRequested;
            }

            _view = view;

            if (_view != null)
            {
                _view.SignInRequested += HandleSignInRequested;
                _view.SignOutRequested += HandleSignOutRequested;
                _view.SubmitCodeRequested += HandleSubmitCodeRequested;
                _view.CancelRequested += HandleCancelRequested;
            }

            if (!_disposed)
            {
                RenderFromStatus(_accountService.GetStatus());
            }
        }

        /// <summary>
        /// Shows the overlay, advancing to the SignedOut screen if not already signed in.
        /// </summary>
        public void ShowSignIn()
        {
            if (_disposed)
            {
                return;
            }

            _overlayVisible = true;
            RenderFromStatus(_accountService.GetStatus());
        }

        /// <summary>
        /// Hides the overlay.
        /// </summary>
        public void Hide()
        {
            if (_disposed)
            {
                return;
            }

            _overlayVisible = false;
            _view?.Render(BuildState(_accountService.GetStatus(), forceVisible: false));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _accountService.OnStatusChanged -= HandleStatusChanged;
            BindView(null);
        }

        // -------------------------------------------------------------------------
        // Private — status / render
        // -------------------------------------------------------------------------

        private void HandleStatusChanged(SidekickAccountStatus status)
        {
            if (_disposed)
            {
                return;
            }

            _scheduler.Schedule(() =>
            {
                if (_disposed)
                {
                    return;
                }

                var newState = status?.State ?? SidekickAccountState.SignedOut;

                // When a sign-in flow just completed (SigningIn → SignedIn), close the overlay
                // automatically. A returning user otherwise tends to reflexively click the only
                // prominent button — "Sign out" — mistaking it for "Continue"/"Done". Opening the
                // overlay while already signed in (to inspect the plan) does not pass through
                // SigningIn, so that case stays open.
                if (_lastNotifiedState == SidekickAccountState.SigningIn &&
                    newState == SidekickAccountState.SignedIn)
                {
                    _overlayVisible = false;
                }

                _lastNotifiedState = newState;
                RenderFromStatus(status);
            });
        }

        private void RenderFromStatus(SidekickAccountStatus status)
        {
            if (_disposed || _view == null)
            {
                return;
            }

            _view.Render(BuildState(status, forceVisible: _overlayVisible));
        }

        private SidekickAccountLoginViewState BuildState(SidekickAccountStatus status, bool forceVisible)
        {
            var screen = MapScreen(status);

            // Visibility follows the per-controller overlay flag only. A sign-in flow is global
            // (one OAuth session in the process-singleton manager), so multiple bound views share
            // the same status. Showing the SigningIn screen wherever the status is SigningIn would
            // pop the modal in windows the user never opened it in (e.g. the chat window when sign-in
            // was started from Project Settings). Only the controller whose overlay was opened via
            // ShowSignIn() (which sets _overlayVisible) shows the in-progress UI.
            var visible = forceVisible;

            return new SidekickAccountLoginViewState(
                isVisible: visible,
                screen: screen,
                email: status?.Profile?.Email ?? string.Empty,
                plan: status?.Profile?.Plan ?? string.Empty,
                seats: 0,          // seat-count endpoint is Phase 2d; default 0 until then
                manualCode: string.Empty,
                errorMessage: status?.ErrorMessage ?? string.Empty);
        }

        private static SidekickAccountScreen MapScreen(SidekickAccountStatus status)
        {
            if (status == null)
            {
                return SidekickAccountScreen.SignedOut;
            }

            return status.State switch
            {
                SidekickAccountState.SignedIn => SidekickAccountScreen.SignedIn,
                SidekickAccountState.SigningIn => SidekickAccountScreen.SigningIn,
                _ => SidekickAccountScreen.SignedOut
            };
        }

        // -------------------------------------------------------------------------
        // Private — view event handlers
        // -------------------------------------------------------------------------

        private async void HandleSignInRequested()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var result = await _accountService.StartLoginAsync(url =>
                {
                    if (!_disposed)
                    {
                        Application.OpenURL(url);
                    }
                });

                if (_disposed)
                {
                    return;
                }

                if (!result.Success)
                {
                    Debug.LogWarning($"[SidekickAccount] Login failed: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    Debug.LogError($"[SidekickAccount] Login error: {ex.Message}");
                }
            }
        }

        private async void HandleSignOutRequested()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                await _accountService.SignOutAsync();
                _overlayVisible = false;
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    Debug.LogError($"[SidekickAccount] Sign-out error: {ex.Message}");
                }
            }
        }

        private void HandleSubmitCodeRequested()
        {
            if (_disposed)
            {
                return;
            }

            var code = _view?.ManualCode?.Trim();
            if (!string.IsNullOrEmpty(code))
            {
                var result = _accountService.HandleManualCode(code);
                if (!result.Success)
                {
                    Debug.LogWarning($"[SidekickAccount] Manual code rejected: {result.ErrorMessage}");
                }
            }
        }

        private void HandleCancelRequested()
        {
            if (_disposed)
            {
                return;
            }

            _accountService.CancelLogin();
            _overlayVisible = false;
            _view?.Render(BuildState(_accountService.GetStatus(), forceVisible: false));
        }
    }
}
