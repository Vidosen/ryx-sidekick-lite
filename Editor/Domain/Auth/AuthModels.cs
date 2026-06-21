// SPDX-License-Identifier: GPL-3.0-only
using System;
using Newtonsoft.Json;

namespace Ryx.Sidekick.Editor.Domain.Auth
{
    /// <summary>
    /// Represents the overall authentication status.
    /// </summary>
    internal enum AuthState
    {
        /// <summary>Not authenticated.</summary>
        NotAuthenticated,
        /// <summary>Authentication in progress.</summary>
        Authenticating,
        /// <summary>Successfully authenticated.</summary>
        Authenticated,
        /// <summary>Authentication failed.</summary>
        Failed
    }

    /// <summary>
    /// Authentication method used.
    /// </summary>
    internal enum AuthMethod
    {
        /// <summary>Claude.ai OAuth (user:inference scope).</summary>
        ClaudeAi,
        /// <summary>Console OAuth (creates API key).</summary>
        Console,
        /// <summary>Direct API key authentication.</summary>
        ApiKey,
        /// <summary>Third-party provider (Bedrock, Vertex, etc.).</summary>
        ThirdParty
    }

    internal enum AuthFailureKind
    {
        None,
        Cancelled,
        Timeout,
        InvalidCallback,
        InvalidManualCode,
        TokenExchange,
        CredentialStore,
        Network,
        Unknown
    }

    /// <summary>
    /// Concrete third-party provider when <see cref="AuthMethod.ThirdParty"/> is used.
    /// </summary>
    internal enum ThirdPartyProvider
    {
        Unknown,
        Bedrock,
        Vertex,
        Foundry
    }

    /// <summary>
    /// Subscription type for Claude.ai OAuth users.
    /// </summary>
    internal enum SubscriptionType
    {
        Unknown,
        Free,
        Pro,
        Max,
        Team,
        Enterprise
    }

    /// <summary>
    /// Current authentication status with details.
    /// </summary>
    [Serializable]
    internal class AuthStatus
    {
        public AuthState State;
        public AuthMethod? Method;
        public ThirdPartyProvider? ThirdPartyProvider;
        public string Email;
        public SubscriptionType? Subscription;
        public string ErrorMessage;
        public AuthFailureKind FailureKind;

        public bool IsAuthenticated => State == AuthState.Authenticated;

        public static AuthStatus NotAuthenticated() => new AuthStatus
        {
            State = AuthState.NotAuthenticated,
            Method = null,
            ThirdPartyProvider = null,
            Email = null,
            Subscription = null,
            ErrorMessage = null,
            FailureKind = AuthFailureKind.None
        };

        public static AuthStatus Authenticating() => new AuthStatus
        {
            State = AuthState.Authenticating,
            Method = null,
            ThirdPartyProvider = null,
            Email = null,
            Subscription = null,
            ErrorMessage = null,
            FailureKind = AuthFailureKind.None
        };

        public static AuthStatus Authenticated(AuthMethod method, string email = null, SubscriptionType? subscription = null) => new AuthStatus
        {
            State = AuthState.Authenticated,
            Method = method,
            ThirdPartyProvider = null,
            Email = email,
            Subscription = subscription,
            ErrorMessage = null,
            FailureKind = AuthFailureKind.None
        };

        public static AuthStatus Failed(string errorMessage, AuthFailureKind failureKind = AuthFailureKind.Unknown) => new AuthStatus
        {
            State = AuthState.Failed,
            Method = null,
            ThirdPartyProvider = null,
            Email = null,
            Subscription = null,
            ErrorMessage = errorMessage,
            FailureKind = failureKind
        };
    }

    /// <summary>
    /// OAuth credentials stored securely.
    /// Uses camelCase JSON property names to match CLI format.
    /// </summary>
    [Serializable]
    internal class OAuthCredentials
    {
        [JsonProperty("accessToken")]
        public string AccessToken;
        
        [JsonProperty("refreshToken")]
        public string RefreshToken;
        
        [JsonProperty("expiresAt")]
        public long? ExpiresAt;
        
        [JsonProperty("scopes")]
        public string[] Scopes;
        
        [JsonProperty("subscriptionType")]
        public SubscriptionType? SubscriptionType;
        
        [JsonProperty("rateLimitTier")]
        public string RateLimitTier;

        [JsonIgnore]
        public bool IsExpired => ExpiresAt.HasValue && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > ExpiresAt.Value;

        public bool HasScope(string scope)
        {
            if (Scopes == null) return false;
            foreach (var s in Scopes)
            {
                if (s == scope) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Result of an authentication attempt.
    /// </summary>
    [Serializable]
    internal class AuthResult
    {
        public bool Success;
        public AuthStatus Status;
        public string ErrorMessage;
        public AuthFailureKind FailureKind;

        public static AuthResult Succeeded(AuthStatus status) => new AuthResult
        {
            Success = true,
            Status = status,
            ErrorMessage = null,
            FailureKind = AuthFailureKind.None
        };

        public static AuthResult Failed(string errorMessage, AuthFailureKind failureKind = AuthFailureKind.Unknown) => new AuthResult
        {
            Success = false,
            Status = AuthStatus.Failed(errorMessage, failureKind),
            ErrorMessage = errorMessage,
            FailureKind = failureKind
        };
    }

    internal sealed class AuthFailureException : Exception
    {
        public AuthFailureException(AuthFailureKind failureKind, string message, Exception innerException = null)
            : base(message, innerException)
        {
            FailureKind = failureKind;
        }

        public AuthFailureKind FailureKind { get; }
    }

    /// <summary>
    /// OAuth configuration endpoints.
    /// </summary>
    internal static class OAuthConfig
    {
        // Production endpoints
        public const string BaseApiUrl = "https://api.anthropic.com";
        public const string ConsoleAuthorizeUrl = "https://console.anthropic.com/oauth/authorize";
        public const string ClaudeAiAuthorizeUrl = "https://claude.ai/oauth/authorize";
        public const string TokenUrl = "https://console.anthropic.com/v1/oauth/token";
        public const string ApiKeyUrl = "https://api.anthropic.com/api/oauth/claude_cli/create_api_key";
        public const string ProfileUrl = "https://api.anthropic.com/api/claude_cli_profile";
        public const string ConsoleSuccessUrl = "https://console.anthropic.com/oauth/code/success?app=claude-code";
        public const string ClaudeAiSuccessUrl = "https://console.anthropic.com/oauth/code/success?app=claude-code";
        public const string ManualRedirectUrl = "https://console.anthropic.com/oauth/code/callback";
        public const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

        // Scopes
        public static readonly string[] ConsoleScopes = { "org:create_api_key", "user:profile" };
        public static readonly string[] ClaudeAiScopes = { "user:profile", "user:inference", "user:sessions:claude_code" };

        // Loopback ports to try
        public static readonly int[] LoopbackPorts = { 54545, 54546, 54547, 54548, 54549 };
        
        // Timeout for OAuth flow (5 minutes)
        public const int OAuthTimeoutMs = 300000;
    }

    /// <summary>
    /// File-based credential storage format (matches CLI's .credentials.json).
    /// </summary>
    [Serializable]
    internal class CredentialFile
    {
        public OAuthCredentials claudeAiOauth;
    }

    /// <summary>
    /// Config file format (matches CLI's config.json).
    /// </summary>
    [Serializable]
    internal class ConfigFile
    {
        public string primaryApiKey;
        public string licenseKey;
        public string sidekickAccountRefreshToken;
    }
}


