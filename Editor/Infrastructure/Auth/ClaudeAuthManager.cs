// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Infrastructure;
using UnityEngine;

using Ryx.Sidekick.Editor.Domain.Auth;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Auth
{
    /// <summary>
    /// Main authentication manager for Sidekick.
    /// Orchestrates credential storage, OAuth flows, and authentication state.
    /// </summary>
    internal class ClaudeAuthManager : IDisposable, IAuthService
    {
        private static ClaudeAuthManager _instance;
        
        /// <summary>
        /// Singleton instance of the auth manager.
        /// </summary>
        public static ClaudeAuthManager Instance
        {
            get
            {
                _instance ??= new ClaudeAuthManager();
                return _instance;
            }
        }

        private readonly ICredentialStore _credentialStore;
        private readonly Func<OAuthService> _oauthServiceFactory;
        private OAuthService _currentOAuthService;
        private bool _disposed;

        /// <summary>
        /// Fired when authentication status changes.
        /// </summary>
        public event Action<AuthStatus> OnAuthStatusChanged;

        /// <summary>
        /// Creates a new ClaudeAuthManager with platform-appropriate credential storage.
        /// </summary>
        public ClaudeAuthManager() : this(new DefaultCredentialStoreProvider())
        {
        }

        internal ClaudeAuthManager(ICredentialStoreProvider credentialStoreProvider)
            : this(credentialStoreProvider?.Create() ?? CredentialStoreFactory.Create())
        {
        }

        /// <summary>
        /// Creates a new ClaudeAuthManager with a specific credential store (for testing).
        /// </summary>
        public ClaudeAuthManager(ICredentialStore credentialStore)
            : this(credentialStore, () => new OAuthService())
        {
        }

        internal ClaudeAuthManager(ICredentialStore credentialStore, Func<OAuthService> oauthServiceFactory)
        {
            _credentialStore = credentialStore;
            _oauthServiceFactory = oauthServiceFactory ?? (() => new OAuthService());
            Debug.Log($"[ClaudeAuth] AuthManager initialized with store: {credentialStore.Name}");
        }

        /// <summary>
        /// Gets the current authentication status.
        /// </summary>
        public AuthStatus GetAuthStatus()
        {
            // Check for third-party providers via environment variables
            if (IsThirdPartyAuthConfigured())
            {
                var status = AuthStatus.Authenticated(AuthMethod.ThirdParty);
                status.ThirdPartyProvider = GetThirdPartyProvider();
                return status;
            }

            // Check for direct API key in environment
            var envApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (!string.IsNullOrEmpty(envApiKey))
            {
                return AuthStatus.Authenticated(AuthMethod.ApiKey);
            }

            // Check for OAuth tokens
            var oauthCreds = _credentialStore.ReadOAuthCredentials();
            if (oauthCreds != null && !string.IsNullOrEmpty(oauthCreds.AccessToken))
            {
                // Determine if this is Claude.ai or Console OAuth based on scopes
                var method = oauthCreds.HasScope("user:inference") 
                    ? AuthMethod.ClaudeAi 
                    : AuthMethod.Console;
                
                return AuthStatus.Authenticated(method, null, oauthCreds.SubscriptionType);
            }

            // Check for stored API key
            var apiKey = _credentialStore.ReadApiKey();
            if (!string.IsNullOrEmpty(apiKey))
            {
                return AuthStatus.Authenticated(AuthMethod.ApiKey);
            }

            return AuthStatus.NotAuthenticated();
        }

        /// <summary>
        /// Starts the OAuth login flow.
        /// </summary>
        /// <param name="method">The authentication method to use.</param>
        /// <param name="openBrowser">Callback to open the browser with auth URL.</param>
        /// <returns>Authentication result.</returns>
        public async Task<AuthResult> LoginAsync(AuthMethod method, Action<OAuthUrls> openBrowser)
        {
            if (_disposed)
            {
                return AuthResult.Failed("Authentication manager is disposed", AuthFailureKind.Unknown);
            }

            if (method != AuthMethod.ClaudeAi && method != AuthMethod.Console)
            {
                return AuthResult.Failed("Invalid authentication method for OAuth login");
            }

            NotifyStatusChange(AuthStatus.Authenticating());

            _currentOAuthService?.Dispose();
            var oauthService = _oauthServiceFactory();
            _currentOAuthService = oauthService;

            try
            {
                var isClaudeAi = method == AuthMethod.ClaudeAi;
                var tokens = await oauthService.StartOAuthFlowAsync(isClaudeAi, openBrowser);

                if (isClaudeAi)
                {
                    // Save OAuth tokens directly
                    var result = _credentialStore.WriteOAuthCredentials(tokens);
                    if (!result.Success)
                    {
                        var status = AuthStatus.Failed("Failed to save OAuth credentials", AuthFailureKind.CredentialStore);
                        NotifyStatusChange(status);
                        return AuthResult.Failed("Failed to save OAuth credentials", AuthFailureKind.CredentialStore);
                    }
                }
                else
                {
                    // Console OAuth - create and save API key
                    var apiKey = await oauthService.CreateApiKeyAsync(tokens.AccessToken);
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        var status = AuthStatus.Failed("Failed to create API key", AuthFailureKind.TokenExchange);
                        NotifyStatusChange(status);
                        return AuthResult.Failed("Failed to create API key", AuthFailureKind.TokenExchange);
                    }

                    var result = _credentialStore.WriteApiKey(apiKey);
                    if (!result.Success)
                    {
                        var status = AuthStatus.Failed("Failed to save API key", AuthFailureKind.CredentialStore);
                        NotifyStatusChange(status);
                        return AuthResult.Failed("Failed to save API key", AuthFailureKind.CredentialStore);
                    }
                }

                var finalStatus = GetAuthStatus();
                NotifyStatusChange(finalStatus);
                Debug.Log($"[ClaudeAuth] Login successful: {finalStatus.Method}");
                
                return AuthResult.Succeeded(finalStatus);
            }
            catch (AuthFailureException ex)
            {
                if (ex.FailureKind != AuthFailureKind.Cancelled)
                {
                    Debug.LogWarning($"[ClaudeAuth] Login failed: {ex.Message}");
                }

                var status = AuthStatus.Failed(ex.Message, ex.FailureKind);
                NotifyStatusChange(status);
                return AuthResult.Failed(ex.Message, ex.FailureKind);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClaudeAuth] Login failed: {ex.Message}");
                var status = AuthStatus.Failed(ex.Message, AuthFailureKind.Unknown);
                NotifyStatusChange(status);
                return AuthResult.Failed(ex.Message, AuthFailureKind.Unknown);
            }
            finally
            {
                if (ReferenceEquals(_currentOAuthService, oauthService))
                {
                    _currentOAuthService?.Dispose();
                    _currentOAuthService = null;
                }
            }
        }

        public AuthResult CancelLogin()
        {
            var oauthService = _currentOAuthService;
            if (oauthService == null)
            {
                return AuthResult.Failed("No active OAuth login to cancel", AuthFailureKind.Cancelled);
            }

            _currentOAuthService = null;
            oauthService.Dispose();

            var status = AuthStatus.NotAuthenticated();
            NotifyStatusChange(status);
            return AuthResult.Succeeded(status);
        }

        /// <summary>
        /// Handles manual auth code input when automatic callback fails.
        /// </summary>
        public AuthResult HandleManualAuthCode(string code)
        {
            if (_currentOAuthService == null)
            {
                Debug.LogWarning("[ClaudeAuth] No active OAuth service to handle manual auth code");
                return AuthResult.Failed("No active OAuth login is waiting for a manual code", AuthFailureKind.InvalidManualCode);
            }

            return _currentOAuthService.HandleManualAuthCode(code);
        }

        /// <summary>
        /// Logs out and clears all stored credentials.
        /// </summary>
        public Task<bool> LogoutAsync()
        {
            try
            {
                var success = _credentialStore.DeleteAll();
                var status = success
                    ? AuthStatus.NotAuthenticated()
                    : AuthStatus.Failed("Failed to clear credentials", AuthFailureKind.CredentialStore);
                NotifyStatusChange(status);
                Debug.Log($"[ClaudeAuth] Logout {(success ? "successful" : "failed")}");
                return Task.FromResult(success);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeAuth] Logout failed: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Saves an API key directly (for manual API key entry).
        /// </summary>
        public bool SaveApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
                return false;

            var result = _credentialStore.WriteApiKey(apiKey);
            if (result.Success)
            {
                var status = GetAuthStatus();
                NotifyStatusChange(status);
            }
            return result.Success;
        }

        /// <summary>
        /// Checks if authentication is required before sending prompts.
        /// </summary>
        public bool RequiresAuth()
        {
            var status = GetAuthStatus();
            return !status.IsAuthenticated;
        }

        private void NotifyStatusChange(AuthStatus status)
        {
            OnAuthStatusChanged?.Invoke(status);
        }

        private bool IsThirdPartyAuthConfigured()
        {
            // Settings-driven third-party routing (Unity often doesn't inherit shell env vars when launched from GUI).
            // We treat this as "authenticated" because Claude CLI won't require Claude.ai / Console login in this mode.
            if (IsThirdPartyProviderEnabledInSettings())
                return true;

            // Check for Bedrock, Vertex, or other third-party providers
            if (IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_BEDROCK")))
                return true;
            if (IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_VERTEX")))
                return true;
            if (IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_FOUNDRY")))
                return true;
            if (IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_SKIP_AUTH_LOGIN")))
                return true;
            // LLM gateway scenarios where the gateway handles provider authentication
            if (IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_SKIP_BEDROCK_AUTH")))
                return true;
            if (IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_SKIP_VERTEX_AUTH")))
                return true;
            if (IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_SKIP_FOUNDRY_AUTH")))
                return true;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN")))
                return true;

            return false;
        }

        private static ThirdPartyProvider GetThirdPartyProvider()
        {
            // Prefer concrete provider flags first (matches Claude CLI docs for cloud providers / gateways)
            if (IsThirdPartyProviderEnabledInSettings() ||
                IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_BEDROCK")) ||
                IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_SKIP_BEDROCK_AUTH")))
            {
                return ThirdPartyProvider.Bedrock;
            }

            if (IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_VERTEX")) ||
                IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_SKIP_VERTEX_AUTH")))
            {
                return ThirdPartyProvider.Vertex;
            }

            if (IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_FOUNDRY")) ||
                IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_SKIP_FOUNDRY_AUTH")))
            {
                return ThirdPartyProvider.Foundry;
            }

            return ThirdPartyProvider.Unknown;
        }

        private static bool IsThirdPartyProviderEnabledInSettings()
        {
            try
            {
                // Currently Sidekick only exposes Bedrock as a first-class toggle.
                return SidekickSettings.instance && SidekickSettings.instance.UseBedrock;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTruthy(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            var lower = value.ToLowerInvariant().Trim();
            return lower is "1" or "true" or "yes" or "on";
        }

        public void Dispose()
        {
            if (_disposed && _currentOAuthService == null) return;
            _disposed = true;
            _currentOAuthService?.Dispose();
            _currentOAuthService = null;
        }

        /// <summary>
        /// Resets the singleton instance (for testing).
        /// </summary>
        internal static void ResetInstance()
        {
            _instance?.Dispose();
            _instance = null;
        }
    }
}
