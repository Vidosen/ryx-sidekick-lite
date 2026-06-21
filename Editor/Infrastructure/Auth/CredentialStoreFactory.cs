// SPDX-License-Identifier: GPL-3.0-only
using UnityEngine;

using Ryx.Sidekick.Editor.Domain.Auth;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Auth
{
    /// <summary>
    /// Factory for creating platform-appropriate credential stores.
    /// </summary>
    internal static class CredentialStoreFactory
    {
        private static ICredentialStore _cachedStore;

        /// <summary>
        /// Creates the appropriate credential store for the current platform.
        /// </summary>
        /// <returns>A credential store instance suitable for the current platform.</returns>
        public static ICredentialStore Create()
        {
            if (_cachedStore != null)
                return _cachedStore;

#if UNITY_EDITOR_OSX
            _cachedStore = new MacOSCredentialStore();
            Debug.Log($"[ClaudeAuth] Using credential store: {_cachedStore.Name}");
#elif UNITY_EDITOR_WIN
            // Windows: Use file storage (could add Windows Credential Manager in future)
            _cachedStore = new FileCredentialStore();
            Debug.Log($"[ClaudeAuth] Using credential store: {_cachedStore.Name}");
#else
            // Linux and others: Use file storage
            _cachedStore = new FileCredentialStore();
            Debug.Log($"[ClaudeAuth] Using credential store: {_cachedStore.Name}");
#endif

            return _cachedStore;
        }

        /// <summary>
        /// Clears the cached store instance (for testing purposes).
        /// </summary>
        internal static void ClearCache()
        {
            _cachedStore = null;
        }
    }
}



