// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

using Ryx.Sidekick.Editor.Domain.Account;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Auth
{
    /// <summary>
    /// Per-login OAuth worker for the Sidekick account (ryx-sidekick.pro) flow.
    /// Implements PKCE S256 with a local loopback callback server.
    /// </summary>
    internal class SidekickAccountOAuthService : IDisposable
    {
        private readonly IHttpClient _http;
        private readonly IMachineIdProvider _machine;
        private readonly string _editorVersion;
        private readonly string _os;

        private HttpListener _httpListener;
        private CancellationTokenSource _cts;
        private TaskCompletionSource<AccountCodeResult> _authCodeTcs;
        private string _codeVerifier;
        private string _state;
        private int _activePort;
        private bool _disposed;
        private bool _cancelledByDispose;

        /// <summary>Internal result of the authorization code callback.</summary>
        private class AccountCodeResult
        {
            public string Code;
            public bool IsManualFlow;
        }

        public SidekickAccountOAuthService(IHttpClient http, IMachineIdProvider machine,
            string editorVersion = null, string os = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            _editorVersion = editorVersion ?? UnityEngine.Application.unityVersion;
            _os = os ?? UnityEngine.Application.platform.ToString();
        }

        /// <summary>
        /// Starts the loopback OAuth flow. Opens the bridge URL in the browser, waits for the
        /// callback, then exchanges the code for a session.
        /// </summary>
        /// <param name="openBrowser">Callback that receives the bridge URL to open.</param>
        public virtual async Task<SidekickAccountSession> StartFlowAsync(Action<string> openBrowser)
        {
            _cts = new CancellationTokenSource(SidekickAccountOAuthConfig.OAuthTimeoutMs);
            _authCodeTcs = new TaskCompletionSource<AccountCodeResult>();

            try
            {
                _activePort = StartCallbackServer();

                _codeVerifier = GenerateCodeVerifier();
                var codeChallenge = GenerateCodeChallenge(_codeVerifier);
                _state = GenerateState();

                var redirectUri = $"http://127.0.0.1:{_activePort}/callback";
                var bridgeUrl = BuildBridgeUrl(codeChallenge, _state, redirectUri);

                // Small delay to ensure server is fully ready
                await Task.Delay(100);

                Debug.Log($"[SidekickAccount] Opening browser for OAuth, callback URL: {redirectUri}");

                try
                {
                    openBrowser?.Invoke(bridgeUrl);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to open OAuth browser: {ex.Message}", ex);
                }

                var result = await WaitForCodeAsync(_cts.Token);

                Debug.Log($"[SidekickAccount] Received authorization code, isManualFlow: {result.IsManualFlow}");

                return await ExchangeAsync(result.Code, _codeVerifier, redirectUri);
            }
            finally
            {
                Cleanup();
            }
        }

        /// <summary>
        /// Handles a manually entered authorization code in "code" or "code#state" format.
        /// </summary>
        public virtual SidekickAccountResult HandleManualCode(string code)
        {
            if (_authCodeTcs == null || _authCodeTcs.Task.IsCompleted)
            {
                return SidekickAccountResult.Failed("No active OAuth login is waiting for a manual code");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return SidekickAccountResult.Failed("Authorization code is empty");
            }

            var parts = code.Trim().Split('#');
            if (parts.Length == 2)
            {
                if (string.IsNullOrWhiteSpace(parts[0]))
                    return SidekickAccountResult.Failed("Authorization code is empty");

                if (parts[1] != _state)
                    return SidekickAccountResult.Failed("Authorization code state does not match this login attempt");

                if (!_authCodeTcs.TrySetResult(new AccountCodeResult { Code = parts[0], IsManualFlow = true }))
                    return SidekickAccountResult.Failed("OAuth login is no longer waiting for a manual code");

                return SidekickAccountResult.Succeeded(SidekickAccountStatus.SigningIn());
            }
            else if (parts.Length == 1)
            {
                if (string.IsNullOrWhiteSpace(parts[0]))
                    return SidekickAccountResult.Failed("Authorization code is empty");

                if (!_authCodeTcs.TrySetResult(new AccountCodeResult { Code = parts[0], IsManualFlow = true }))
                    return SidekickAccountResult.Failed("OAuth login is no longer waiting for a manual code");

                return SidekickAccountResult.Succeeded(SidekickAccountStatus.SigningIn());
            }
            else
            {
                return SidekickAccountResult.Failed("Invalid authorization code format");
            }
        }

        /// <summary>
        /// Exchanges an authorization code for a Sidekick account session.
        /// POST {ExchangeUrl} {code, codeVerifier, redirectUri, machineId, editorVersion, os}
        /// </summary>
        public virtual async Task<SidekickAccountSession> ExchangeAsync(
            string code, string codeVerifier, string redirectUri)
        {
            var body = JsonConvert.SerializeObject(new
            {
                code,
                codeVerifier,
                redirectUri,
                machineId = _machine.GetMachineId(),
                editorVersion = _editorVersion,
                os = _os
            });

            var response = await _http.PostJsonAsync(SidekickAccountOAuthConfig.ExchangeUrl, body, 30);

            if (!response.Ok)
            {
                var errorMsg = ParseErrorMessage(response.Body);
                throw new InvalidOperationException($"Token exchange failed ({response.Status}): {errorMsg}");
            }

            return ParseSessionResponse(response.Body, keepOldRefreshToken: null);
        }

        /// <summary>
        /// Refreshes the session using the stored refresh token.
        /// POST {RefreshUrl} {refreshToken}
        /// </summary>
        public virtual async Task<SidekickAccountSession> RefreshAsync(string refreshToken)
        {
            var body = JsonConvert.SerializeObject(new { refreshToken });

            var response = await _http.PostJsonAsync(SidekickAccountOAuthConfig.RefreshUrl, body, 30);

            if (!response.Ok)
            {
                var errorMsg = ParseErrorMessage(response.Body);
                throw new InvalidOperationException($"Session refresh failed ({response.Status}): {errorMsg}");
            }

            return ParseSessionResponse(response.Body, keepOldRefreshToken: refreshToken);
        }

        public void Cleanup()
        {
            Debug.Log("[SidekickAccount] Cleaning up OAuth service...");

            var listener = _httpListener;
            _httpListener = null;

            if (listener != null)
            {
                try
                {
                    if (listener.IsListening)
                        listener.Stop();
                    listener.Close();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SidekickAccount] Error stopping HttpListener: {ex.Message}");
                }
            }

            _cts?.Cancel();
            try { _cts?.Dispose(); } catch { }
            _cts = null;

            Debug.Log("[SidekickAccount] OAuth service cleanup complete");
        }

        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cancelledByDispose = true;
            Cleanup();
        }

        // ---------------------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------------------

        private int StartCallbackServer()
        {
            foreach (var port in SidekickAccountOAuthConfig.LoopbackPorts)
            {
                try
                {
                    Debug.Log($"[SidekickAccount] Attempting to start server on port {port}...");

                    _httpListener = new HttpListener();
                    _httpListener.Prefixes.Add($"http://localhost:{port}/");
                    _httpListener.Prefixes.Add($"http://127.0.0.1:{port}/");
                    _httpListener.Start();

                    if (!_httpListener.IsListening)
                        throw new Exception("HttpListener started but not listening");

                    var listenerThread = new Thread(() =>
                    {
                        try { ListenForCallbackSync(); }
                        catch (Exception ex) { Debug.LogError($"[SidekickAccount] Listener thread error: {ex}"); }
                    })
                    {
                        IsBackground = true,
                        Name = "SidekickAccountOAuthCallbackListener"
                    };
                    listenerThread.Start();

                    Debug.Log($"[SidekickAccount] OAuth callback server started on port {port}");
                    return port;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SidekickAccount] Failed to start server on port {port}: {ex.Message}");
                    try { _httpListener?.Stop(); } catch { }
                    try { _httpListener?.Close(); } catch { }
                    _httpListener = null;
                }
            }

            throw new Exception("Failed to start OAuth callback server on any available port");
        }

        private void ListenForCallbackSync()
        {
            Debug.Log("[SidekickAccount] Callback listener thread started");
            try
            {
                while (true)
                {
                    try
                    {
                        var listener = _httpListener;
                        if (listener == null || !listener.IsListening)
                            break;

                        var context = listener.GetContext();
                        Debug.Log($"[SidekickAccount] Received request: {context.Request.HttpMethod} {context.Request.Url}");
                        HandleCallbackRequest(context);
                    }
                    catch (HttpListenerException hlex) when (hlex.ErrorCode == 995 || hlex.ErrorCode == 500)
                    {
                        Debug.Log("[SidekickAccount] Listener stopped");
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        Debug.Log("[SidekickAccount] Listener disposed");
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        Debug.Log("[SidekickAccount] Listener not started");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SidekickAccount] Listener loop error: {ex}");
                _authCodeTcs?.TrySetException(new InvalidOperationException(ex.Message, ex));
            }
            Debug.Log("[SidekickAccount] Callback listener thread ended");
        }

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

                    Debug.Log($"[SidekickAccount] Callback received - code present: {!string.IsNullOrEmpty(code)}, state match: {state == _state}");

                    if (state != _state)
                    {
                        SendResponse(response, 400, "Invalid state parameter");
                        _authCodeTcs?.TrySetException(new InvalidOperationException("Invalid state parameter"));
                        return;
                    }

                    if (string.IsNullOrEmpty(code))
                    {
                        SendResponse(response, 400, "Missing authorization code");
                        _authCodeTcs?.TrySetException(new InvalidOperationException("Missing authorization code"));
                        return;
                    }

                    SendResponse(response, 200, "Login successful. You may close this tab.");

                    Debug.Log("[SidekickAccount] Authorization code received, setting result...");

                    _authCodeTcs?.TrySetResult(new AccountCodeResult { Code = code, IsManualFlow = false });
                }
                else
                {
                    Debug.Log($"[SidekickAccount] Unknown path requested: {request.Url.AbsolutePath}");
                    SendResponse(response, 404, "Not found");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SidekickAccount] Error handling callback: {ex.Message}");
                try { SendResponse(response, 500, "Internal error"); } catch { }
            }
        }

        private static void SendResponse(HttpListenerResponse response, int statusCode, string message)
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
                Debug.LogWarning($"[SidekickAccount] Error sending response: {ex.Message}");
            }
        }

        private async Task<AccountCodeResult> WaitForCodeAsync(CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(() => _authCodeTcs?.TrySetCanceled()))
            {
                try
                {
                    return await _authCodeTcs.Task;
                }
                catch (OperationCanceledException ex)
                {
                    var message = _cancelledByDispose ? "Login was cancelled" : "Login timed out";
                    throw new OperationCanceledException(message, ex);
                }
            }
        }

        private static string BuildBridgeUrl(string codeChallenge, string state, string redirectUri)
        {
            var sb = new StringBuilder(SidekickAccountOAuthConfig.AccountWebBase);
            sb.Append(SidekickAccountOAuthConfig.EditorAuthPath);
            sb.Append('?');
            sb.Append("redirect_uri=");
            sb.Append(Uri.EscapeDataString(redirectUri));
            sb.Append("&code_challenge=");
            sb.Append(Uri.EscapeDataString(codeChallenge));
            sb.Append("&code_challenge_method=S256");
            sb.Append("&state=");
            sb.Append(Uri.EscapeDataString(state));
            return sb.ToString();
        }

        private static SidekickAccountSession ParseSessionResponse(string body, string keepOldRefreshToken)
        {
            JObject json;
            try
            {
                json = JObject.Parse(body ?? string.Empty);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Server returned invalid JSON", ex);
            }

            var ok = json["ok"]?.Value<bool>() ?? false;
            if (!ok)
            {
                var err = json["error"]?.ToString() ?? "Unknown error";
                throw new InvalidOperationException($"Server returned error: {err}");
            }

            var accessToken = json["accessToken"]?.ToString();
            if (string.IsNullOrEmpty(accessToken))
                throw new InvalidOperationException("Server response did not include an access token");

            var refreshToken = json["refreshToken"]?.ToString() ?? keepOldRefreshToken;
            var expiresIn = json["expiresIn"]?.Value<long>() ?? 3600L;
            var expiresAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresIn;
            var entitlementToken = json["entitlementToken"]?.ToString();

            var profileObj = json["profile"] as JObject;
            SidekickAccountProfile profile = null;
            if (profileObj != null)
            {
                profile = new SidekickAccountProfile
                {
                    DisplayName = profileObj["displayName"]?.ToString(),
                    Email = profileObj["email"]?.ToString(),
                    Plan = profileObj["plan"]?.ToString()
                };
            }

            return new SidekickAccountSession
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAtUnix = expiresAtUnix,
                EntitlementToken = string.IsNullOrEmpty(entitlementToken) ? null : entitlementToken,
                Profile = profile
            };
        }

        private static string ParseErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "Empty response";
            try
            {
                var json = JObject.Parse(body);
                return json["error"]?.ToString() ?? json["message"]?.ToString() ?? body;
            }
            catch (JsonException)
            {
                return body;
            }
        }

        #region PKCE Helpers

        internal static string GenerateCodeVerifier()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Base64UrlEncode(bytes);
        }

        internal static string GenerateCodeChallenge(string codeVerifier)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
                return Base64UrlEncode(hash);
            }
        }

        internal static string GenerateState()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Base64UrlEncode(bytes);
        }

        internal static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        #endregion
    }
}
