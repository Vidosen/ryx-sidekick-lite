// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

using Ryx.Sidekick.Editor.Domain.Auth;

namespace Ryx.Sidekick.Editor.Infrastructure.Auth
{
    /// <summary>
    /// OAuth service implementing PKCE flow with local loopback server.
    /// </summary>
    internal class OAuthService : IDisposable
    {
        private HttpListener _httpListener;
        private CancellationTokenSource _cts;
        private TaskCompletionSource<AuthCodeResult> _authCodeTcs;
        private string _codeVerifier;
        private string _state;
        private int _activePort;
        private bool _disposed;
        private bool _cancelledByDispose;

        /// <summary>
        /// Result from the authorization code callback.
        /// </summary>
        private class AuthCodeResult
        {
            public string Code;
            public bool IsManualFlow;
        }

        /// <summary>
        /// Starts the OAuth flow and returns tokens on success.
        /// </summary>
        /// <param name="isClaudeAi">True for Claude.ai OAuth, false for Console OAuth.</param>
        /// <param name="openBrowser">Callback to open the browser with the auth URL.</param>
        /// <returns>OAuth credentials on success.</returns>
        public virtual async Task<OAuthCredentials> StartOAuthFlowAsync(bool isClaudeAi, Action<OAuthUrls> openBrowser)
        {
            _cts = new CancellationTokenSource(OAuthConfig.OAuthTimeoutMs);
            _authCodeTcs = new TaskCompletionSource<AuthCodeResult>();

            try
            {
                // Start loopback server
                _activePort = StartCallbackServerAsync();
                
                // Generate PKCE parameters
                _codeVerifier = GenerateCodeVerifier();
                var codeChallenge = GenerateCodeChallenge(_codeVerifier);
                _state = GenerateState();

                // Build auth URLs
                var urls = BuildAuthUrls(codeChallenge, _state, _activePort, isClaudeAi);

                // Small delay to ensure server is fully ready
                await Task.Delay(100);
                
                Debug.Log($"[ClaudeAuth] Opening browser for OAuth, callback URL: http://localhost:{_activePort}/callback");

                try
                {
                    openBrowser?.Invoke(urls);
                }
                catch (Exception ex)
                {
                    throw new AuthFailureException(
                        AuthFailureKind.Unknown,
                        $"Failed to open OAuth browser: {ex.Message}",
                        ex);
                }

                // Wait for callback
                var result = await WaitForAuthorizationCodeAsync(_cts.Token);
                
                Debug.Log($"[ClaudeAuth] Received authorization code, isManualFlow: {result.IsManualFlow}");

                // Exchange code for tokens
                var tokens = await ExchangeCodeForTokensAsync(
                    result.Code, 
                    _state, 
                    _codeVerifier, 
                    result.IsManualFlow ? null : (int?)_activePort,
                    isClaudeAi
                );

                return tokens;
            }
            finally
            {
                Cleanup();
            }
        }

        /// <summary>
        /// Handles manual auth code input when automatic callback fails.
        /// </summary>
        public virtual AuthResult HandleManualAuthCode(string code)
        {
            if (_authCodeTcs == null || _authCodeTcs.Task.IsCompleted)
            {
                return AuthResult.Failed("No active OAuth login is waiting for a manual code", AuthFailureKind.InvalidManualCode);
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return AuthResult.Failed("Authorization code is empty", AuthFailureKind.InvalidManualCode);
            }

            // Parse the code - format is "code#state"
            var parts = code.Trim().Split('#');
            if (parts.Length == 2)
            {
                if (string.IsNullOrWhiteSpace(parts[0]))
                {
                    return AuthResult.Failed("Authorization code is empty", AuthFailureKind.InvalidManualCode);
                }

                if (parts[1] != _state)
                {
                    return AuthResult.Failed("Authorization code state does not match this login attempt", AuthFailureKind.InvalidManualCode);
                }

                if (!_authCodeTcs.TrySetResult(new AuthCodeResult
                {
                    Code = parts[0],
                    IsManualFlow = true
                }))
                {
                    return AuthResult.Failed("OAuth login is no longer waiting for a manual code", AuthFailureKind.InvalidManualCode);
                }

                return AuthResult.Succeeded(AuthStatus.Authenticating());
            }
            else if (parts.Length == 1)
            {
                if (string.IsNullOrWhiteSpace(parts[0]))
                {
                    return AuthResult.Failed("Authorization code is empty", AuthFailureKind.InvalidManualCode);
                }

                // Just the code without state
                if (!_authCodeTcs.TrySetResult(new AuthCodeResult
                {
                    Code = parts[0],
                    IsManualFlow = true
                }))
                {
                    return AuthResult.Failed("OAuth login is no longer waiting for a manual code", AuthFailureKind.InvalidManualCode);
                }

                return AuthResult.Succeeded(AuthStatus.Authenticating());
            }
            else
            {
                Debug.LogWarning("[ClaudeAuth] Invalid manual auth code format");
                return AuthResult.Failed("Invalid authorization code format", AuthFailureKind.InvalidManualCode);
            }
        }

        /// <summary>
        /// Creates an API key using the access token (for Console OAuth).
        /// </summary>
        public virtual async Task<string> CreateApiKeyAsync(string accessToken)
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new AuthFailureException(AuthFailureKind.TokenExchange, "Cannot create API key without an access token");
            }

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

                HttpResponseMessage response;
                try
                {
                    response = await client.PostAsync(OAuthConfig.ApiKeyUrl, null);
                }
                catch (HttpRequestException ex)
                {
                    throw new AuthFailureException(AuthFailureKind.Network, $"API key creation request failed: {ex.Message}", ex);
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = ParseOAuthErrorMessage(responseBody);
                    throw new AuthFailureException(
                        AuthFailureKind.TokenExchange,
                        $"API key creation failed ({(int)response.StatusCode} {response.StatusCode}): {errorMessage}");
                }

                return ParseApiKeyResponse(responseBody);
            }
        }

        /// <summary>
        /// Cleans up resources.
        /// </summary>
        public void Cleanup()
        {
            Debug.Log("[ClaudeAuth] Cleaning up OAuth service...");
            
            var listener = _httpListener;
            _httpListener = null;
            
            if (listener != null)
            {
                try
                {
                    if (listener.IsListening)
                    {
                        listener.Stop();
                    }
                    listener.Close();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ClaudeAuth] Error stopping HttpListener: {ex.Message}");
                }
            }

            _cts?.Cancel();
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            
            Debug.Log("[ClaudeAuth] OAuth service cleanup complete");
        }

        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cancelledByDispose = true;
            Cleanup();
        }

        private int StartCallbackServerAsync()
        {
            foreach (var port in OAuthConfig.LoopbackPorts)
            {
                try
                {
                    Debug.Log($"[ClaudeAuth] Attempting to start server on port {port}...");
                    
                    _httpListener = new HttpListener();
                    // Use both localhost and 127.0.0.1 for better compatibility
                    _httpListener.Prefixes.Add($"http://localhost:{port}/");
                    _httpListener.Prefixes.Add($"http://127.0.0.1:{port}/");
                    _httpListener.Start();
                    
                    if (!_httpListener.IsListening)
                    {
                        throw new Exception("HttpListener started but not listening");
                    }
                    
                    Debug.Log($"[ClaudeAuth] HttpListener started, prefixes: {string.Join(", ", _httpListener.Prefixes)}");
                    
                    // Start listening for requests in background on a dedicated thread
                    var listenerThread = new Thread(() =>
                    {
                        try
                        {
                            ListenForCallbackSync();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[ClaudeAuth] Listener thread error: {ex}");
                        }
                    })
                    {
                        IsBackground = true,
                        Name = "OAuthCallbackListener"
                    };
                    listenerThread.Start();
                    
                    Debug.Log($"[ClaudeAuth] OAuth callback server started on port {port}, IsListening: {_httpListener.IsListening}");
                    return port;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ClaudeAuth] Failed to start server on port {port}: {ex.Message}");
                    try { _httpListener?.Stop(); } catch { }
                    try { _httpListener?.Close(); } catch { }
                    _httpListener = null;
                }
            }

            throw new Exception("Failed to start OAuth callback server on any available port");
        }
        
        /// <summary>
        /// Synchronous listener loop that runs on a dedicated background thread.
        /// </summary>
        private void ListenForCallbackSync()
        {
            Debug.Log("[ClaudeAuth] Callback listener thread started");
            try
            {
                while (true)
                {
                    try
                    {
                        var listener = _httpListener;
                        if (listener == null || !listener.IsListening)
                        {
                            break;
                        }

                        // GetContext is blocking, which is fine on a background thread
                        var context = listener.GetContext();
                        Debug.Log($"[ClaudeAuth] Received request: {context.Request.HttpMethod} {context.Request.Url}");
                        HandleCallbackRequest(context);
                    }
                    catch (HttpListenerException hlex) when (hlex.ErrorCode == 995 || hlex.ErrorCode == 500)
                    {
                        // 995 = Operation aborted, 500 = Listener closed
                        Debug.Log("[ClaudeAuth] Listener stopped");
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        Debug.Log("[ClaudeAuth] Listener disposed");
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        Debug.Log("[ClaudeAuth] Listener not started");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeAuth] Listener loop error: {ex}");
                _authCodeTcs?.TrySetException(new AuthFailureException(AuthFailureKind.Unknown, ex.Message, ex));
            }
            Debug.Log("[ClaudeAuth] Callback listener thread ended");
        }
        
        /// <summary>
        /// Handles a callback request synchronously.
        /// </summary>
        private void HandleCallbackRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                if (request.Url.AbsolutePath == "/callback")
                {
                    var code = request.QueryString["code"];
                    var state = request.QueryString["state"];
                    
                    Debug.Log($"[ClaudeAuth] Callback received - code present: {!string.IsNullOrEmpty(code)}, state match: {state == _state}");

                    if (state != _state)
                    {
                        SendResponse(response, 400, "Invalid state parameter");
                        _authCodeTcs?.TrySetException(new AuthFailureException(AuthFailureKind.InvalidCallback, "Invalid state parameter"));
                        return;
                    }

                    if (string.IsNullOrEmpty(code))
                    {
                        SendResponse(response, 400, "Missing authorization code");
                        _authCodeTcs?.TrySetException(new AuthFailureException(AuthFailureKind.InvalidCallback, "Missing authorization code"));
                        return;
                    }

                    // Redirect to success page
                    response.StatusCode = 302;
                    response.RedirectLocation = OAuthConfig.ClaudeAiSuccessUrl;
                    response.Close();
                    
                    Debug.Log("[ClaudeAuth] Authorization code received, setting result...");

                    _authCodeTcs?.TrySetResult(new AuthCodeResult
                    {
                        Code = code,
                        IsManualFlow = false
                    });
                }
                else
                {
                    Debug.Log($"[ClaudeAuth] Unknown path requested: {request.Url.AbsolutePath}");
                    SendResponse(response, 404, "Not found");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeAuth] Error handling callback: {ex.Message}");
                try
                {
                    SendResponse(response, 500, "Internal error");
                }
                catch { }
            }
        }
        
        private void SendResponse(HttpListenerResponse response, int statusCode, string message)
        {
            try
            {
                response.StatusCode = statusCode;
                var buffer = Encoding.UTF8.GetBytes(message);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClaudeAuth] Error sending response: {ex.Message}");
            }
        }

        private async Task<AuthCodeResult> WaitForAuthorizationCodeAsync(CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(() => _authCodeTcs?.TrySetCanceled()))
            {
                try
                {
                    return await _authCodeTcs.Task;
                }
                catch (OperationCanceledException ex)
                {
                    var kind = _cancelledByDispose ? AuthFailureKind.Cancelled : AuthFailureKind.Timeout;
                    var message = kind == AuthFailureKind.Cancelled
                        ? "Login was cancelled"
                        : "Login timed out";
                    throw new AuthFailureException(kind, message, ex);
                }
            }
        }

        private OAuthUrls BuildAuthUrls(string codeChallenge, string state, int port, bool isClaudeAi)
        {
            var baseUrl = isClaudeAi ? OAuthConfig.ClaudeAiAuthorizeUrl : OAuthConfig.ConsoleAuthorizeUrl;
            var scopes = isClaudeAi ? OAuthConfig.ClaudeAiScopes : OAuthConfig.ConsoleScopes;

            var autoParams = new Dictionary<string, string>
            {
                ["code"] = "true",
                ["client_id"] = OAuthConfig.ClientId,
                ["response_type"] = "code",
                ["scope"] = string.Join(" ", scopes),
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["state"] = state,
                ["redirect_uri"] = $"http://localhost:{port}/callback"
            };

            var manualParams = new Dictionary<string, string>(autoParams)
            {
                ["redirect_uri"] = OAuthConfig.ManualRedirectUrl
            };

            return new OAuthUrls
            {
                AutomaticRedirectUrl = BuildUrlWithParams(baseUrl, autoParams),
                ManualRedirectUrl = BuildUrlWithParams(baseUrl, manualParams)
            };
        }

        private static string BuildUrlWithParams(string baseUrl, Dictionary<string, string> parameters)
        {
            var sb = new StringBuilder(baseUrl);
            sb.Append('?');
            bool first = true;
            foreach (var kvp in parameters)
            {
                if (!first) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kvp.Key));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(kvp.Value));
                first = false;
            }
            return sb.ToString();
        }

        private async Task<OAuthCredentials> ExchangeCodeForTokensAsync(
            string code, string state, string codeVerifier, int? port, bool isClaudeAi)
        {
            var redirectUri = port.HasValue 
                ? $"http://localhost:{port}/callback" 
                : OAuthConfig.ManualRedirectUrl;

            var requestBody = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = OAuthConfig.ClientId,
                ["code_verifier"] = codeVerifier,
                ["state"] = state
            };

            using (var client = new HttpClient())
            {
                var content = new StringContent(
                    JsonConvert.SerializeObject(requestBody),
                    Encoding.UTF8,
                    "application/json"
                );

                HttpResponseMessage response;
                try
                {
                    response = await client.PostAsync(OAuthConfig.TokenUrl, content);
                }
                catch (HttpRequestException ex)
                {
                    throw new AuthFailureException(AuthFailureKind.Network, $"Token exchange request failed: {ex.Message}", ex);
                }

                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = ParseOAuthErrorMessage(responseBody);
                    throw new AuthFailureException(
                        AuthFailureKind.TokenExchange,
                        $"Token exchange failed ({(int)response.StatusCode} {response.StatusCode}): {errorMessage}");
                }

                var credentials = ParseTokenResponse(responseBody);

                // Fetch profile info if we have user:profile scope
                if (credentials.HasScope("user:profile"))
                {
                    await FetchProfileInfoAsync(credentials);
                }

                return credentials;
            }
        }

        private async Task FetchProfileInfoAsync(OAuthCredentials credentials)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {credentials.AccessToken}");
                    
                    var response = await client.GetAsync(OAuthConfig.ProfileUrl);
                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.LogWarning($"[ClaudeAuth] Failed to fetch profile: {response.StatusCode}");
                        return;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    var profile = JObject.Parse(json);

                    var orgType = profile["organization"]?["organization_type"]?.ToString();
                    credentials.SubscriptionType = orgType switch
                    {
                        "claude_max" => SubscriptionType.Max,
                        "claude_pro" => SubscriptionType.Pro,
                        "claude_enterprise" => SubscriptionType.Enterprise,
                        "claude_team" => SubscriptionType.Team,
                        _ => SubscriptionType.Unknown
                    };

                    credentials.RateLimitTier = profile["organization"]?["rate_limit_tier"]?.ToString();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClaudeAuth] Failed to fetch profile info: {ex.Message}");
            }
        }

        internal static OAuthCredentials ParseTokenResponseForTests(string responseBody) => ParseTokenResponse(responseBody);

        internal static string ParseApiKeyResponseForTests(string responseBody) => ParseApiKeyResponse(responseBody);

        private static OAuthCredentials ParseTokenResponse(string responseBody)
        {
            JObject tokenResponse;
            try
            {
                tokenResponse = JObject.Parse(responseBody ?? string.Empty);
            }
            catch (JsonException ex)
            {
                throw new AuthFailureException(AuthFailureKind.TokenExchange, "Token exchange returned invalid JSON", ex);
            }

            var accessToken = tokenResponse["access_token"]?.ToString();
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new AuthFailureException(AuthFailureKind.TokenExchange, "Token exchange response did not include an access token");
            }

            var refreshToken = tokenResponse["refresh_token"]?.ToString();
            var expiresIn = tokenResponse["expires_in"]?.Value<int>();
            var scope = tokenResponse["scope"]?.ToString();

            return new OAuthCredentials
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = expiresIn.HasValue
                    ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (expiresIn.Value * 1000)
                    : null,
                Scopes = string.IsNullOrWhiteSpace(scope)
                    ? Array.Empty<string>()
                    : scope.Split(' ')
            };
        }

        private static string ParseApiKeyResponse(string responseBody)
        {
            JObject result;
            try
            {
                result = JObject.Parse(responseBody ?? string.Empty);
            }
            catch (JsonException ex)
            {
                throw new AuthFailureException(AuthFailureKind.TokenExchange, "API key creation returned invalid JSON", ex);
            }

            var apiKey = result["raw_key"]?.ToString();
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new AuthFailureException(AuthFailureKind.TokenExchange, "API key creation response did not include an API key");
            }

            return apiKey;
        }

        private static string ParseOAuthErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return "Empty response";
            }

            try
            {
                var errorJson = JObject.Parse(responseBody);
                return errorJson["error"]?.ToString()
                    ?? errorJson["message"]?.ToString()
                    ?? responseBody;
            }
            catch (JsonException)
            {
                return responseBody;
            }
        }

        #region PKCE Helpers

        private static string GenerateCodeVerifier()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Base64UrlEncode(bytes);
        }

        private static string GenerateCodeChallenge(string codeVerifier)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
                return Base64UrlEncode(hash);
            }
        }

        private static string GenerateState()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Base64UrlEncode(bytes);
        }

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        #endregion
    }
}
