// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Debug = UnityEngine.Debug;

using Ryx.Sidekick.Editor.Domain.Auth;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Auth
{
    /// <summary>
    /// macOS Keychain-based credential storage with file fallback.
    /// Uses the 'security' CLI tool to interact with the macOS Keychain.
    /// </summary>
    internal class MacOSCredentialStore : ICredentialStore
    {
        public string Name => "keychain-with-plaintext-fallback";

        private readonly FileCredentialStore _fallback = new FileCredentialStore();
        private readonly string _serviceName = GetServiceName("-credentials");
        private readonly string _accountName = GetAccountName();

        /// <summary>
        /// Gets the keychain service name, including a hash suffix if CLAUDE_CONFIG_DIR is set.
        /// </summary>
        private static string GetServiceName(string suffix = "")
        {
            var configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            var hashSuffix = string.Empty;
            
            if (!string.IsNullOrEmpty(configDir))
            {
                using var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(configDir));
                hashSuffix = "-" + BitConverter.ToString(hash).Replace("-", "")[..8].ToLower();
            }

            return $"Claude Code{suffix}{hashSuffix}";
        }

        /// <summary>
        /// Gets the account name for keychain entries.
        /// </summary>
        private static string GetAccountName()
        {
            var user = Environment.GetEnvironmentVariable("USER");
            if (!string.IsNullOrEmpty(user))
                return user;

            return Environment.UserName ?? "claude-code-user";
        }

        public OAuthCredentials ReadOAuthCredentials()
        {
            try
            {
                var data = ReadFromKeychain();
                if (!string.IsNullOrEmpty(data))
                {
                    // The data from keychain should be the raw JSON (security -w decodes hex automatically)
                    var json = data.Trim();
                    
                    // Try to parse as our CredentialFile format using Newtonsoft.Json
                    try
                    {
                        var credFile = JsonConvert.DeserializeObject<CredentialFile>(json);
                        if (credFile?.claudeAiOauth != null)
                        {
                            Debug.Log("[ClaudeAuth] Successfully read OAuth credentials from keychain");
                            return credFile.claudeAiOauth;
                        }
                    }
                    catch (JsonException)
                    {
                        // Try parsing as direct OAuthCredentials (legacy or different format)
                        try
                        {
                            var directCreds = JsonConvert.DeserializeObject<OAuthCredentials>(json);
                            if (directCreds != null && !string.IsNullOrEmpty(directCreds.AccessToken))
                            {
                                Debug.Log("[ClaudeAuth] Read OAuth credentials from keychain (direct format)");
                                return directCreds;
                            }
                        }
                        catch
                        {
                            // Not direct credentials either, check if it's CLI format with different structure
                        }
                    }
                    
                    // If we get here, the keychain has data but not in our format
                    // This might be from Claude CLI - log but don't fail
                    if (json.Length > 0)
                    {
                        var preview = json.Length > 100 ? json[..100] + "..." : json;
                        Debug.LogWarning($"[ClaudeAuth] Keychain contains unrecognized data format. Preview: {preview}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClaudeAuth] Keychain read error: {ex.Message}");
            }

            // Fall back to file storage
            return _fallback.ReadOAuthCredentials();
        }

        public CredentialStoreResult WriteOAuthCredentials(OAuthCredentials credentials)
        {
            var credFile = new CredentialFile { claudeAiOauth = credentials };
            var json = JsonConvert.SerializeObject(credFile, Formatting.Indented);

            Debug.Log($"[ClaudeAuth] Writing OAuth credentials to keychain (token length: {credentials.AccessToken?.Length ?? 0})...");

            // Try keychain first
            var keychainResult = WriteToKeychain(json);
            if (keychainResult)
            {
                // Verify by reading back
                var readBack = ReadFromKeychain();
                if (!string.IsNullOrEmpty(readBack) && readBack.Contains("accessToken"))
                {
                    Debug.Log("[ClaudeAuth] OAuth credentials verified in keychain");
                    DeleteFallbackCredentials();
                    return CredentialStoreResult.Succeeded();
                }
                Debug.LogWarning($"[ClaudeAuth] Keychain verification failed, read back: {readBack?[..Math.Min(50, readBack?.Length ?? 0)]}...");
            }

            Debug.LogWarning("[ClaudeAuth] Keychain write failed, falling back to file storage");
            // Fall back to file storage
            var fileResult = _fallback.WriteOAuthCredentials(credentials);
            return fileResult.Success 
                ? CredentialStoreResult.Succeeded(fileResult.Warning) 
                : CredentialStoreResult.Failed();
        }

        public string ReadApiKey()
        {
            // API key uses a different service name
            try
            {
                var apiKeyService = GetServiceName();
                var key = ReadFromKeychain(apiKeyService);
                if (!string.IsNullOrEmpty(key))
                    return key;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClaudeAuth] Keychain API key read failed: {ex.Message}");
            }

            // Fall back to file storage
            return _fallback.ReadApiKey();
        }

        public CredentialStoreResult WriteApiKey(string apiKey)
        {
            var apiKeyService = GetServiceName();
            
            // Try keychain first
            if (WriteToKeychain(apiKey, apiKeyService))
            {
                return CredentialStoreResult.Succeeded();
            }

            // Fall back to file storage
            return _fallback.WriteApiKey(apiKey);
        }

        public bool DeleteAll()
        {
            bool success = true;

            // Delete from keychain
            try
            {
                DeleteFromKeychain(_serviceName);
            }
            catch
            {
                success = false;
            }

            try
            {
                DeleteFromKeychain(GetServiceName());
            }
            catch
            {
                success = false;
            }

            // Also delete from file fallback
            if (!_fallback.DeleteAll())
                success = false;

            return success;
        }

        public string ReadLicenseKey()
        {
            try
            {
                var value = ReadFromKeychain(GetServiceName("-license"));
                if (!string.IsNullOrEmpty(value)) return value;
            }
            catch { /* fall through to file */ }
            return _fallback.ReadLicenseKey();
        }

        public CredentialStoreResult WriteLicenseKey(string licenseKey)
        {
            try
            {
                if (WriteToKeychain(licenseKey, GetServiceName("-license")))
                    return CredentialStoreResult.Succeeded();
            }
            catch { /* fall through */ }
            return _fallback.WriteLicenseKey(licenseKey);
        }

        public string ReadAccountRefreshToken()
        {
            try
            {
                var value = ReadFromKeychain(GetServiceName("-sidekick-account"));
                if (!string.IsNullOrEmpty(value)) return value;
            }
            catch { /* fall through to file */ }
            return _fallback.ReadAccountRefreshToken();
        }

        public CredentialStoreResult WriteAccountRefreshToken(string token)
        {
            try
            {
                if (WriteToKeychain(token ?? string.Empty, GetServiceName("-sidekick-account")))
                    return CredentialStoreResult.Succeeded();
            }
            catch { /* fall through */ }
            return _fallback.WriteAccountRefreshToken(token);
        }

        private string ReadFromKeychain(string serviceName = null)
        {
            serviceName ??= _serviceName;

            var startInfo = new ProcessStartInfo
            {
                FileName = "security",
                Arguments = $"find-generic-password -a \"{_accountName}\" -w -s \"{serviceName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            if (process.ExitCode != 0)
                return null;

            var result = output?.Trim();
                
            // Check if the result looks like hex-encoded data (all hex chars, even length)
            // This can happen when data was stored with -X flag
            if (!string.IsNullOrEmpty(result) && IsHexString(result))
            {
                try
                {
                    result = DecodeHex(result);
                }
                catch
                {
                    // Not valid hex, use as-is
                }
            }
                
            return result;
        }
        
        private static bool IsHexString(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length % 2 != 0)
                return false;
            
            // Quick check: if it starts with { or [, it's probably already JSON
            if (s[0] == '{' || s[0] == '[')
                return false;
                
            foreach (var c in s)
            {
                if (c is (< '0' or > '9') and (< 'a' or > 'f') and (< 'A' or > 'F'))
                    return false;
            }
            return true;
        }
        
        private static string DecodeHex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return Encoding.UTF8.GetString(bytes);
        }

        private bool WriteToKeychain(string data, string serviceName = null)
        {
            serviceName ??= _serviceName;

            try
            {
                // First, try to delete any existing entry to avoid conflicts
                try
                {
                    DeleteFromKeychain(serviceName);
                }
                catch
                {
                    // ignored
                }

                // Convert data to hex for security CLI
                var hexData = BitConverter.ToString(Encoding.UTF8.GetBytes(data)).Replace("-", "");
                
                // Use security CLI with stdin input for safety
                // Using -U flag to update if exists, but we deleted first for clean state
                var input = $"add-generic-password -a \"{_accountName}\" -s \"{serviceName}\" -X \"{hexData}\"\n";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "security",
                    Arguments = "-i",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    return false;

                process.StandardInput.Write(input);
                process.StandardInput.Close();
                    
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    Debug.LogWarning($"[ClaudeAuth] Keychain write returned exit code {process.ExitCode}: {stderr}");
                    return false;
                }
                    
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClaudeAuth] Keychain write failed: {ex.Message}");
                return false;
            }
        }

        private void DeleteFromKeychain(string serviceName)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "security",
                    Arguments = $"delete-generic-password -a \"{_accountName}\" -s \"{serviceName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                process?.WaitForExit(5000);
            }
            catch
            {
                // Ignore - item may not exist
            }
        }

        private static void DeleteFallbackCredentials()
        {
            try
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var credPath = Path.Combine(home, ".claude", ".credentials.json");
                if (File.Exists(credPath))
                {
                    File.Delete(credPath);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
