// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Auth;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>
    /// Result of a credential store operation.
    /// </summary>
    internal class CredentialStoreResult
    {
        public bool Success;
        public string Warning;

        public static CredentialStoreResult Succeeded(string warning = null) => new CredentialStoreResult
        {
            Success = true,
            Warning = warning
        };

        public static CredentialStoreResult Failed() => new CredentialStoreResult
        {
            Success = false,
            Warning = null
        };
    }

    /// <summary>
    /// Interface for platform-specific credential storage.
    /// Implementations handle secure storage of OAuth tokens and API keys.
    /// </summary>
    internal interface ICredentialStore
    {
        /// <summary>
        /// Store name for logging/debugging.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Read OAuth credentials from secure storage.
        /// </summary>
        /// <returns>OAuth credentials or null if not found.</returns>
        OAuthCredentials ReadOAuthCredentials();

        /// <summary>
        /// Write OAuth credentials to secure storage.
        /// </summary>
        /// <param name="credentials">The credentials to store.</param>
        /// <returns>Result indicating success or failure.</returns>
        CredentialStoreResult WriteOAuthCredentials(OAuthCredentials credentials);

        /// <summary>
        /// Read API key from secure storage.
        /// </summary>
        /// <returns>API key string or null if not found.</returns>
        string ReadApiKey();

        /// <summary>
        /// Write API key to secure storage.
        /// </summary>
        /// <param name="apiKey">The API key to store.</param>
        /// <returns>Result indicating success or failure.</returns>
        CredentialStoreResult WriteApiKey(string apiKey);

        /// <summary>
        /// Delete all stored credentials.
        /// </summary>
        /// <returns>True if deletion succeeded.</returns>
        bool DeleteAll();

        /// <summary>Read the Sidekick license key (offline-fallback), or null.</summary>
        string ReadLicenseKey();

        /// <summary>Store the Sidekick license key.</summary>
        CredentialStoreResult WriteLicenseKey(string licenseKey);

        /// <summary>Read the Sidekick account OAuth refresh token, or null if not signed in.</summary>
        string ReadAccountRefreshToken();

        /// <summary>Store the Sidekick account OAuth refresh token. Pass empty string to clear.</summary>
        CredentialStoreResult WriteAccountRefreshToken(string token);
    }
}





