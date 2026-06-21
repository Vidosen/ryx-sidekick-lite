// SPDX-License-Identifier: GPL-3.0-only
#if UNITY_6000_3_OR_NEWER
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Presentation.Constants;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Infrastructure
{
    [InitializeOnLoad]
    internal static class ToolbarService
    {
        private const string IconPath = SidekickUiConstants.ToolbarIconPath;
        private const string TooltipText = SidekickAppConstants.Toolbar.TooltipOpenSidekick;
        private const string FirstRunPrefKey = SidekickAppConstants.EditorPrefsKeys.ToolbarInitializedV1;

        static ToolbarService()
        {
            // Only enable toolbar on first install (don't override user preferences afterwards)
            if (!EditorPrefs.GetBool(FirstRunPrefKey, false))
            {
                EditorApplication.delayCall += EnableToolbarOnFirstRun;
            }
        }

        private static void EnableToolbarOnFirstRun()
        {

            // Find the internal MainToolbarWindow via reflection
            var mainToolbarWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.MainToolbarWindow");
            if (mainToolbarWindowType == null)
            {
                return;
            }

            var windows = Resources.FindObjectsOfTypeAll(mainToolbarWindowType);
            if (windows.Length == 0)
            {
                return;
            }

            var toolbarWindow = windows[0] as EditorWindow;
            if (toolbarWindow == null)
            {
                return;
            }
            // Use TryGetOverlay to find our toolbar element and enable it
            if (toolbarWindow.TryGetOverlay(SidekickUiConstants.ToolbarOverlayElementId, out var overlay))
            {
                overlay.displayed = true;
                // Mark as initialized so we don't override user's visibility preference later
                EditorPrefs.SetBool(FirstRunPrefKey, true);
            }
        }

        [MainToolbarElement(SidekickUiConstants.ToolbarOverlayElementId, defaultDockPosition = MainToolbarDockPosition.Right)]
        public static MainToolbarElement CreateSidekickButton()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            var content = new MainToolbarContent(SidekickAppConstants.Toolbar.ActionOpenChat, icon, TooltipText);
            return new MainToolbarButton(content, SidekickWindow.ShowWindow);
        }
    }
}
#endif
