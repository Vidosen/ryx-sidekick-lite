// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor.Domain.Account
{
    /// <summary>
    /// OAuth configuration for the Sidekick account (ryx-sidekick.pro) login flow.
    /// </summary>
    internal static class SidekickAccountOAuthConfig
    {
        public const string AccountWebBase = "https://ryx-sidekick.pro";
        public const string EditorAuthPath = "/editor-auth";
        public const string ExchangeUrl = "https://europe-west1-ryx-sidekick.cloudfunctions.net/exchangeEditorCode";
        public const string RefreshUrl = "https://europe-west1-ryx-sidekick.cloudfunctions.net/refreshSession";

        /// <summary>
        /// Timeout for the OAuth flow in milliseconds (5 minutes).
        /// </summary>
        public const int OAuthTimeoutMs = 300000;

        /// <summary>
        /// Loopback ports to try for the OAuth callback server.
        /// Defined locally to avoid coupling to Claude's OAuthConfig.
        /// </summary>
        public static readonly int[] LoopbackPorts = { 54545, 54546, 54547, 54548, 54549 };
    }

    /// <summary>
    /// Profile information returned from the Sidekick account backend.
    /// </summary>
    [Serializable]
    internal class SidekickAccountProfile
    {
        public string DisplayName;
        public string Email;
        public string Plan;
    }

    /// <summary>
    /// A Sidekick account session containing tokens and profile information.
    /// </summary>
    [Serializable]
    internal class SidekickAccountSession
    {
        public string AccessToken;
        public string RefreshToken;
        /// <summary>Unix epoch seconds at which the access token expires.</summary>
        public long ExpiresAtUnix;
        /// <summary>Entitlement token, or null for free-tier users.</summary>
        public string EntitlementToken;
        public SidekickAccountProfile Profile;
    }

    /// <summary>
    /// Account sign-in state.
    /// </summary>
    internal enum SidekickAccountState
    {
        SignedOut,
        SigningIn,
        SignedIn,
        Failed
    }

    /// <summary>
    /// Current account status with details.
    /// </summary>
    [Serializable]
    internal class SidekickAccountStatus
    {
        public SidekickAccountState State;
        public SidekickAccountProfile Profile;
        public string ErrorMessage;

        public bool IsSignedIn => State == SidekickAccountState.SignedIn;

        public static SidekickAccountStatus SignedOut() => new SidekickAccountStatus
        {
            State = SidekickAccountState.SignedOut,
            Profile = null,
            ErrorMessage = null
        };

        public static SidekickAccountStatus SigningIn() => new SidekickAccountStatus
        {
            State = SidekickAccountState.SigningIn,
            Profile = null,
            ErrorMessage = null
        };

        public static SidekickAccountStatus SignedIn(SidekickAccountProfile profile) => new SidekickAccountStatus
        {
            State = SidekickAccountState.SignedIn,
            Profile = profile,
            ErrorMessage = null
        };

        public static SidekickAccountStatus Failed(string errorMessage) => new SidekickAccountStatus
        {
            State = SidekickAccountState.Failed,
            Profile = null,
            ErrorMessage = errorMessage
        };
    }

    /// <summary>
    /// Result of an account operation (login, refresh, sign-out).
    /// </summary>
    [Serializable]
    internal class SidekickAccountResult
    {
        public bool Success;
        public SidekickAccountStatus Status;
        public string ErrorMessage;

        public static SidekickAccountResult Succeeded(SidekickAccountStatus status) => new SidekickAccountResult
        {
            Success = true,
            Status = status,
            ErrorMessage = null
        };

        public static SidekickAccountResult Failed(string errorMessage) => new SidekickAccountResult
        {
            Success = false,
            Status = SidekickAccountStatus.Failed(errorMessage),
            ErrorMessage = errorMessage
        };
    }
}
