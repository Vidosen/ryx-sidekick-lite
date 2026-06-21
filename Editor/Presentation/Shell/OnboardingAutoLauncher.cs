// SPDX-License-Identifier: GPL-3.0-only
using System.Text.RegularExpressions;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Infrastructure;
using UnityEditor;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Auto-opens the Sidekick window on editor startup when onboarding has not been completed.
    /// Fires once per Unity editor session via SessionState guard.
    /// </summary>
    [InitializeOnLoad]
    internal static class OnboardingAutoLauncher
    {
        private const string SessionKey = "Sidekick_OnboardingAutoOpened";
        private static readonly IEditorScheduler Scheduler = new UnityEditorScheduler();

        static OnboardingAutoLauncher()
        {
            Scheduler.Schedule(OnEditorReady);
        }

        private static void OnEditorReady()
        {
            if (SessionState.GetBool(SessionKey, false))
                return;

            SessionState.SetBool(SessionKey, true);

            var completedVersion = EditorPrefs.GetString(
                SidekickAppConstants.EditorPrefsKeys.OnboardingCompletedVersion, "");
            var currentVersion = GetPackageVersion();

            if (!string.IsNullOrEmpty(completedVersion) && completedVersion == currentVersion)
                return;

            SidekickWindow.ShowWindow();
        }

        private static string GetPackageVersion()
        {
            try
            {
                var json = System.IO.File.ReadAllText(SidekickUiConstants.PackageJsonPath);
                var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                if (match.Success)
                    return match.Groups[1].Value;
            }
            catch
            {
                // Package file not found or unreadable — treat as unknown
            }

            return "unknown";
        }
    }
}
