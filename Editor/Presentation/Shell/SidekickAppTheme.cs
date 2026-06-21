// SPDX-License-Identifier: GPL-3.0-only
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    /// <summary>
    /// Loads the App UI theme stylesheet and applies editor-context classes to a
    /// root <see cref="VisualElement"/>. First step of the App UI foundation
    /// (APPUI-T01-02).
    /// </summary>
    internal static class SidekickAppTheme
    {
        internal const string AppUiThemePath =
            "Packages/com.unity.dt.app-ui/PackageResources/Styles/Themes/App UI.tss";

        internal const string UnityEditorClass = "unity-editor";

        /// <summary>
        /// Applies the App UI theme to <paramref name="root"/>.
        /// Returns <c>true</c> on success. On failure a warning is logged and
        /// the caller should continue &mdash; existing Sidekick USS still works.
        /// </summary>
        internal static bool Apply(VisualElement root)
        {
            if (root == null)
            {
                Debug.LogWarning("[SidekickAppTheme] Cannot apply theme: root is null.");
                return false;
            }

            var themeAsset = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(AppUiThemePath);
            if (themeAsset == null)
            {
                Debug.LogWarning(
                    $"[SidekickAppTheme] Failed to load App UI theme from '{AppUiThemePath}'. " +
                    "Ensure com.unity.dt.app-ui is installed.");
                return false;
            }

            root.styleSheets.Add(themeAsset);
            root.AddToClassList(UnityEditorClass);
            return true;
        }
    }
}
