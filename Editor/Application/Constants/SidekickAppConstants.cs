// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Constants
{
    /// <summary>
    /// Centralized non-UI app constants (menu paths, EditorPrefs keys, model IDs, and modes).
    /// </summary>
    internal static class SidekickAppConstants
    {
        public static class MenuItems
        {
            public const string WindowSidekick = "Window/Sidekick";
            public const string HelpDocumentation = "Help/Ryx Sidekick/Documentation";
            public const string HelpChangelog = "Help/Ryx Sidekick/Changelog";
        }

        public static class EditorPrefsKeys
        {
            public const string OnboardingCompletedVersion = "Sidekick_OnboardingCompletedVersion";
            public const string ToolbarInitializedV1 = "Sidekick_ToolbarInitialized_v1";

            public static string ProviderOnboardingCompleted(string providerId)
                => $"Sidekick_ProviderOnboarding_{providerId}";
        }

        public static class Models
        {
            public const string Sonnet = "sonnet";
            public const string Opus = "opus";
            public const string Haiku = "haiku";

            public static readonly string[] Presets = { Sonnet, Opus, Haiku };
        }

        public static class PermissionModes
        {
            public const string Default = "default";
            public const string Plan = "plan";
            public const string BypassPermissions = "bypassPermissions";

            public static readonly string[] All = { Default, Plan, BypassPermissions };
        }

        public static class CollaborationModes
        {
            public const string Default = "default";
            public const string Plan = "plan";

            public static readonly string[] All = { Default, Plan };
        }

        public static class Toolbar
        {
            public const string TooltipOpenSidekick = "Open Sidekick";
            public const string ActionOpenChat = "Open Chat";
        }

        public static class Files
        {
            public const string DocsTempFileName = "sidekick-docs.html";
        }

        public static class Notifications
        {
            public const string RefreshAssetsMessage = "Refresh project assets to load assistant edits";
        }
    }
}
