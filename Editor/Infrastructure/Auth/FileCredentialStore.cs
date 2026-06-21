// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

using Ryx.Sidekick.Editor.Domain.Auth;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Auth
{
    /// <summary>
    /// File-based credential storage. Cross-platform fallback when secure storage is unavailable.
    /// Stores credentials in ~/.claude/.credentials.json (OAuth) and ~/.claude/config.json (API key).
    /// </summary>
    internal class FileCredentialStore : ICredentialStore
    {
        public string Name => "plaintext";

        private string ConfigDir => GetConfigDir();
        private string CredentialsPath => Path.Combine(ConfigDir, ".credentials.json");
        private string ConfigPath => Path.Combine(ConfigDir, "config.json");

        /// <summary>
        /// Gets the Claude config directory, respecting CLAUDE_CONFIG_DIR environment variable.
        /// </summary>
        private static string GetConfigDir()
        {
            var envDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (!string.IsNullOrEmpty(envDir))
                return envDir;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".claude");
        }

        public OAuthCredentials ReadOAuthCredentials()
        {
            try
            {
                if (!File.Exists(CredentialsPath))
                    return null;

                var json = File.ReadAllText(CredentialsPath);
                var credFile = JsonConvert.DeserializeObject<CredentialFile>(json);
                return credFile?.claudeAiOauth;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClaudeAuth] Failed to read credentials file: {ex.Message}");
                return null;
            }
        }

        public CredentialStoreResult WriteOAuthCredentials(OAuthCredentials credentials)
        {
            try
            {
                EnsureConfigDir();

                var credFile = new CredentialFile { claudeAiOauth = credentials };
                var json = JsonConvert.SerializeObject(credFile, Formatting.Indented);
                
                File.WriteAllText(CredentialsPath, json);
                SetFilePermissions(CredentialsPath);

                return CredentialStoreResult.Succeeded("Warning: Storing credentials in plaintext.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeAuth] Failed to write credentials file: {ex.Message}");
                return CredentialStoreResult.Failed();
            }
        }

        public string ReadApiKey()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return null;

                var json = File.ReadAllText(ConfigPath);
                var configFile = JsonConvert.DeserializeObject<ConfigFile>(json);
                return configFile?.primaryApiKey;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClaudeAuth] Failed to read config file: {ex.Message}");
                return null;
            }
        }

        public CredentialStoreResult WriteApiKey(string apiKey)
        {
            try
            {
                EnsureConfigDir();

                ConfigFile configFile;
                if (File.Exists(ConfigPath))
                {
                    var existingJson = File.ReadAllText(ConfigPath);
                    configFile = JsonConvert.DeserializeObject<ConfigFile>(existingJson) ?? new ConfigFile();
                }
                else
                {
                    configFile = new ConfigFile();
                }

                configFile.primaryApiKey = apiKey;
                var json = JsonConvert.SerializeObject(configFile, Formatting.Indented);
                
                File.WriteAllText(ConfigPath, json);
                SetFilePermissions(ConfigPath);

                return CredentialStoreResult.Succeeded("Warning: Storing API key in plaintext.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeAuth] Failed to write config file: {ex.Message}");
                return CredentialStoreResult.Failed();
            }
        }

        public bool DeleteAll()
        {
            bool success = true;

            try
            {
                if (File.Exists(CredentialsPath))
                    File.Delete(CredentialsPath);
            }
            catch
            {
                success = false;
            }

            // Don't delete config.json entirely, just remove the API key
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var configFile = JsonConvert.DeserializeObject<ConfigFile>(json) ?? new ConfigFile();
                    configFile.primaryApiKey = null;
                    File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(configFile, Formatting.Indented));
                }
            }
            catch
            {
                success = false;
            }

            return success;
        }

        public string ReadLicenseKey()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return null;
                var config = JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(ConfigPath));
                return string.IsNullOrEmpty(config?.licenseKey) ? null : config.licenseKey;
            }
            catch { return null; }
        }

        public CredentialStoreResult WriteLicenseKey(string licenseKey)
        {
            try
            {
                ConfigFile config = null;
                if (File.Exists(ConfigPath))
                    config = JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(ConfigPath));
                config ??= new ConfigFile();
                config.licenseKey = licenseKey;
                EnsureConfigDir();
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(config, Formatting.Indented));
                SetFilePermissions(ConfigPath);
                return CredentialStoreResult.Succeeded();
            }
            catch { return CredentialStoreResult.Failed(); }
        }

        public string ReadAccountRefreshToken()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return null;
                var config = JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(ConfigPath));
                return string.IsNullOrEmpty(config?.sidekickAccountRefreshToken) ? null : config.sidekickAccountRefreshToken;
            }
            catch { return null; }
        }

        public CredentialStoreResult WriteAccountRefreshToken(string token)
        {
            try
            {
                ConfigFile config = null;
                if (File.Exists(ConfigPath))
                    config = JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(ConfigPath));
                config ??= new ConfigFile();
                config.sidekickAccountRefreshToken = token ?? string.Empty;
                EnsureConfigDir();
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(config, Formatting.Indented));
                SetFilePermissions(ConfigPath);
                return CredentialStoreResult.Succeeded();
            }
            catch { return CredentialStoreResult.Failed(); }
        }

        private void EnsureConfigDir()
        {
            if (!Directory.Exists(ConfigDir))
            {
                Directory.CreateDirectory(ConfigDir);
            }
        }

        /// <summary>
        /// Sets restrictive file permissions (owner read/write only) on Unix systems.
        /// </summary>
        private static void SetFilePermissions(string path)
        {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            try
            {
                // chmod 600 - owner read/write only
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"600 \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    process?.WaitForExit(1000);
                }
            }
            catch
            {
                // Ignore permission errors - not critical
            }
#endif
        }
    }
}

