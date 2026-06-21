// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.UseCases.Contracts;
using UnityEditor;

namespace Ryx.Sidekick.Editor.Infrastructure.Updates
{
    /// <summary>
    /// Persists update dismissals using <see cref="EditorPrefs"/> so they
    /// survive domain reloads and editor restarts without requiring a
    /// serialized asset.
    /// </summary>
    internal sealed class EditorPrefsDismissStore : IDismissStore
    {
        private static string Key(string packageId, string latestVersion) =>
            $"Sidekick_UpdateDismissed_{packageId}_{latestVersion}";

        public bool IsDismissed(string packageId, string latestVersion)
        {
            if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(latestVersion))
                return false;
            return EditorPrefs.GetBool(Key(packageId, latestVersion), false);
        }

        public void Dismiss(string packageId, string latestVersion)
        {
            if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(latestVersion))
                return;
            EditorPrefs.SetBool(Key(packageId, latestVersion), true);
        }
    }
}
