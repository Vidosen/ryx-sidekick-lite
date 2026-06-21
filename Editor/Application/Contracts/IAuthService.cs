// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Auth;

namespace Ryx.Sidekick.Editor
{
    internal interface IAuthService
    {
        event Action<AuthStatus> OnAuthStatusChanged;

        AuthStatus GetAuthStatus();

        Task<AuthResult> LoginAsync(AuthMethod method, Action<OAuthUrls> openBrowser);

        AuthResult CancelLogin();

        AuthResult HandleManualAuthCode(string code);

        Task<bool> LogoutAsync();

        bool SaveApiKey(string apiKey);

        bool RequiresAuth();
    }
}
