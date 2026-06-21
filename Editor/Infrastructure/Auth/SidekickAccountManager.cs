// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading.Tasks;
using UnityEngine;

using Ryx.Sidekick.Editor.Domain.Account;
using Ryx.Sidekick.Editor.Infrastructure.Licensing;
using Ryx.Sidekick.Editor.Infrastructure.Net;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Auth
{
    /// <summary>
    /// Process-singleton that orchestrates Sidekick account (ryx-sidekick.pro) sign-in,
    /// token persistence, refresh, and sign-out. Implements <see cref="ISidekickAccountService"/>.
    /// </summary>
    internal sealed class SidekickAccountManager : ISidekickAccountService, IDisposable
    {
        private static SidekickAccountManager _instance;

        /// <summary>
        /// Singleton instance, constructed with platform defaults on first access.
        /// </summary>
        public static SidekickAccountManager Instance
        {
            get
            {
                _instance ??= new SidekickAccountManager();
                return _instance;
            }
        }

        private readonly IHttpClient _http;
        private readonly IEntitlementCache _cache;
        private readonly ICredentialStore _creds;
        private readonly IMachineIdProvider _machine;
        private readonly Func<SidekickAccountOAuthService> _workerFactory;

        private SidekickAccountOAuthService _current;
        private bool _disposed;

        /// <inheritdoc/>
        public event Action<SidekickAccountStatus> OnStatusChanged;

        // -------------------------------------------------------------------------
        // Constructors
        // -------------------------------------------------------------------------

        /// <summary>
        /// Default constructor used by the singleton — resolves platform defaults.
        /// </summary>
        public SidekickAccountManager()
            : this(
                new UnityWebRequestHttpClient(),
                new SettingsEntitlementCache(),
                CredentialStoreFactory.Create(),
                new SystemInfoMachineIdProvider())
        {
        }

        /// <summary>
        /// Internal constructor for unit tests — all dependencies injected.
        /// </summary>
        internal SidekickAccountManager(
            IHttpClient http,
            IEntitlementCache cache,
            ICredentialStore creds,
            IMachineIdProvider machine,
            Func<SidekickAccountOAuthService> workerFactory = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _creds = creds ?? throw new ArgumentNullException(nameof(creds));
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            _workerFactory = workerFactory ?? (() => new SidekickAccountOAuthService(_http, _machine));
        }

        // -------------------------------------------------------------------------
        // ISidekickAccountService
        // -------------------------------------------------------------------------

        /// <inheritdoc/>
        public SidekickAccountStatus GetStatus()
        {
            try
            {
                var settings = SidekickSettings.instance;
                if (settings != null && settings.AccountSignedIn)
                {
                    var profile = new SidekickAccountProfile
                    {
                        Email = settings.AccountEmail,
                        Plan = settings.AccountPlan
                    };
                    return SidekickAccountStatus.SignedIn(profile);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SidekickAccount] Error reading account status from settings: {ex.Message}");
            }
            return SidekickAccountStatus.SignedOut();
        }

        /// <inheritdoc/>
        public async Task<SidekickAccountResult> StartLoginAsync(Action<string> openBrowser)
        {
            if (_disposed)
                return SidekickAccountResult.Failed("Account manager is disposed");

            NotifyStatusChange(SidekickAccountStatus.SigningIn());

            _current?.Dispose();
            var worker = _workerFactory();
            _current = worker;

            try
            {
                var session = await worker.StartFlowAsync(openBrowser);

                PersistSession(session);
                var status = SidekickAccountStatus.SignedIn(session.Profile);
                NotifyStatusChange(status);
                Debug.Log($"[SidekickAccount] Login successful, plan: {session.Profile?.Plan}");
                return SidekickAccountResult.Succeeded(status);
            }
            catch (OperationCanceledException ex)
            {
                var msg = ex.Message;
                var status = SidekickAccountStatus.Failed(msg);
                NotifyStatusChange(status);
                return SidekickAccountResult.Failed(msg);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SidekickAccount] Login failed: {ex.Message}");
                var status = SidekickAccountStatus.Failed(ex.Message);
                NotifyStatusChange(status);
                return SidekickAccountResult.Failed(ex.Message);
            }
            finally
            {
                if (ReferenceEquals(_current, worker))
                {
                    _current?.Dispose();
                    _current = null;
                }
            }
        }

        /// <inheritdoc/>
        public SidekickAccountResult CancelLogin()
        {
            var worker = _current;
            if (worker == null)
                return SidekickAccountResult.Failed("No active login to cancel");

            _current = null;
            worker.Dispose();

            var status = SidekickAccountStatus.SignedOut();
            NotifyStatusChange(status);
            return SidekickAccountResult.Succeeded(status);
        }

        /// <inheritdoc/>
        public SidekickAccountResult HandleManualCode(string code)
        {
            if (_current == null)
                return SidekickAccountResult.Failed("No active login is waiting for a manual code");

            return _current.HandleManualCode(code);
        }

        /// <inheritdoc/>
        public async Task<SidekickAccountResult> RefreshAsync()
        {
            var refreshToken = _creds.ReadAccountRefreshToken();
            if (string.IsNullOrEmpty(refreshToken))
            {
                var failStatus = SidekickAccountStatus.Failed("No refresh token stored — please sign in again");
                NotifyStatusChange(failStatus);
                return SidekickAccountResult.Failed(failStatus.ErrorMessage);
            }

            try
            {
                var session = await DoRefreshAsync(refreshToken);

                PersistSession(session);
                var status = SidekickAccountStatus.SignedIn(session.Profile);
                NotifyStatusChange(status);
                Debug.Log($"[SidekickAccount] Session refreshed, plan: {session.Profile?.Plan}");
                return SidekickAccountResult.Succeeded(status);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SidekickAccount] Refresh failed: {ex.Message}");
                var status = SidekickAccountStatus.Failed(ex.Message);
                NotifyStatusChange(status);
                return SidekickAccountResult.Failed(ex.Message);
            }
        }

        /// <summary>
        /// Returns a fresh access token, silently refreshing the session if a stored refresh
        /// token is available. Returns null if not signed in or if the refresh call fails.
        /// Does not raise <see cref="OnStatusChanged"/> — use <see cref="RefreshAsync"/> for
        /// full status-notification behaviour.
        /// </summary>
        internal async Task<string> GetFreshAccessTokenAsync()
        {
            try
            {
                var refreshToken = _creds.ReadAccountRefreshToken();
                if (string.IsNullOrEmpty(refreshToken))
                    return null;

                var session = await DoRefreshAsync(refreshToken);
                PersistSession(session);
                return session.AccessToken;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SidekickAccount] GetFreshAccessToken failed: {ex.Message}");
                return null;
            }
        }

        /// <inheritdoc/>
        public Task<bool> SignOutAsync()
        {
            try
            {
                _creds.WriteAccountRefreshToken(string.Empty);
                _cache.Clear();
                ClearSettingsAccountFields();
                NotifyStatusChange(SidekickAccountStatus.SignedOut());
                Debug.Log("[SidekickAccount] Signed out");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SidekickAccount] Sign-out failed: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        // -------------------------------------------------------------------------
        // IDisposable
        // -------------------------------------------------------------------------

        public void Dispose()
        {
            if (_disposed && _current == null) return;
            _disposed = true;
            _current?.Dispose();
            _current = null;
        }

        // -------------------------------------------------------------------------
        // Test seam
        // -------------------------------------------------------------------------

        /// <summary>
        /// Resets the singleton instance (for testing only — do not call in production code).
        /// </summary>
        internal static void ResetForTests()
        {
            _instance?.Dispose();
            _instance = null;
        }

        // -------------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------------

        private async Task<SidekickAccountSession> DoRefreshAsync(string refreshToken)
        {
            var worker = _workerFactory();
            return await worker.RefreshAsync(refreshToken);
        }

        private void PersistSession(SidekickAccountSession session)
        {
            if (session == null) return;

            // Write entitlement token to cache only when provided (free tier has none)
            if (!string.IsNullOrEmpty(session.EntitlementToken))
                _cache.Write(session.EntitlementToken);

            if (!string.IsNullOrEmpty(session.RefreshToken))
                _creds.WriteAccountRefreshToken(session.RefreshToken);

            try
            {
                var settings = SidekickSettings.instance;
                settings.AccountEmail = session.Profile?.Email ?? string.Empty;
                settings.AccountPlan = session.Profile?.Plan ?? string.Empty;
                settings.AccountExpiresAt = session.ExpiresAtUnix;
                settings.AccountSignedIn = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SidekickAccount] Failed to persist session to settings: {ex.Message}");
            }
        }

        private static void ClearSettingsAccountFields()
        {
            try
            {
                var settings = SidekickSettings.instance;
                settings.AccountSignedIn = false;
                settings.AccountEmail = string.Empty;
                settings.AccountPlan = string.Empty;
                settings.AccountExpiresAt = 0L;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SidekickAccount] Failed to clear account fields in settings: {ex.Message}");
            }
        }

        private void NotifyStatusChange(SidekickAccountStatus status)
        {
            OnStatusChanged?.Invoke(status);
        }
    }
}
