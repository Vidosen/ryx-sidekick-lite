// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Infrastructure.Auth;
using Ryx.Sidekick.Editor.Domain.Auth;

namespace Ryx.Sidekick.Editor.Infrastructure
{
    internal sealed class ClaudeAuthService : IAuthService, IDisposable
    {
        private readonly ClaudeAuthManager _authManager;

        public ClaudeAuthService()
        {
            _authManager = ClaudeAuthManager.Instance;
        }

        public event Action<AuthStatus> OnAuthStatusChanged
        {
            add => _authManager.OnAuthStatusChanged += value;
            remove => _authManager.OnAuthStatusChanged -= value;
        }

        public AuthStatus GetAuthStatus()
        {
            return _authManager.GetAuthStatus();
        }

        public Task<AuthResult> LoginAsync(AuthMethod method, Action<OAuthUrls> openBrowser)
        {
            return _authManager.LoginAsync(method, openBrowser);
        }

        public AuthResult CancelLogin()
        {
            return _authManager.CancelLogin();
        }

        public AuthResult HandleManualAuthCode(string code)
        {
            return _authManager.HandleManualAuthCode(code);
        }

        public Task<bool> LogoutAsync()
        {
            return _authManager.LogoutAsync();
        }

        public bool SaveApiKey(string apiKey)
        {
            return _authManager.SaveApiKey(apiKey);
        }

        public bool RequiresAuth()
        {
            return _authManager.RequiresAuth();
        }

        public void Dispose()
        {
            _authManager.CancelLogin();
        }
    }
}
