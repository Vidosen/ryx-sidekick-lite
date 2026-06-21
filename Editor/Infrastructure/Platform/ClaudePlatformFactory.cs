// SPDX-License-Identifier: GPL-3.0-only
using UnityEngine;

namespace Ryx.Sidekick.Editor.Infrastructure.Platform
{
    /// <summary>
    /// Factory for creating platform-specific IClaudePlatform implementations.
    /// Returns the appropriate implementation based on the current Unity editor platform.
    /// </summary>
    internal static class ClaudePlatformFactory
    {
        private static ICliPlatform _cachedPlatform;

        /// <summary>
        /// Gets the platform implementation for the current operating system.
        /// The result is cached for performance.
        /// </summary>
        public static ICliPlatform GetPlatform()
        {
            if (_cachedPlatform != null)
                return _cachedPlatform;

            _cachedPlatform = Application.platform switch
            {
                RuntimePlatform.WindowsEditor => new WindowsICliPlatform(),
                RuntimePlatform.OSXEditor => new MacOSCliPlatform(),
                RuntimePlatform.LinuxEditor => new LinuxCliPlatform(),
                _ => new LinuxCliPlatform() // Fallback to Linux for unknown platforms
            };

            return _cachedPlatform;
        }

        /// <summary>
        /// Clears the cached platform instance. Useful for testing.
        /// </summary>
        public static void ClearCache()
        {
            _cachedPlatform = null;
        }
    }
}
