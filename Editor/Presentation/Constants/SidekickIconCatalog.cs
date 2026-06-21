// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Constants
{
    internal static class SidekickIconCatalog
    {
        private static readonly MethodInfo LoadIconMethod = typeof(EditorGUIUtility).GetMethod(
            "LoadIcon",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(string) },
            null);

        private static readonly Dictionary<string, string[]> IconCandidates = new()
        {
            ["mode-default"] = new[] { "d_SettingsIcon", "SettingsIcon" },
            ["mode-plan"] = new[] { "d_UnityEditor.ConsoleWindow", "UnityEditor.ConsoleWindow", "d_FilterByType" },
            ["permission-default"] = new[] { "d_console.infoicon.sml", "console.infoicon.sml" },
            ["permission-auto"] = new[] { "d_PlayButton", "PlayButton" },
            ["permission-trust"] = new[] { "d_LockIcon-On", "LockIcon-On", "d_LockIcon", "LockIcon" },
            ["permission-danger"] = new[] { "d_console.warnicon.sml", "console.warnicon.sml" },

            ["tool-read"] = new[] { "d_TextAsset Icon", "TextAsset Icon" },
            ["tool-edit"] = new[] { "d_editicon.sml", "editicon.sml" },
            ["tool-write"] = new[] { "d_SaveAs", "SaveAs" },
            ["tool-bash"] = new[] { "d_UnityEditor.ConsoleWindow", "UnityEditor.ConsoleWindow" },
            ["tool-search"] = new[] { "d_Search Icon", "Search Icon" },
            ["tool-folder"] = new[] { "d_Folder Icon", "Folder Icon" },
            ["tool-todo"] = new[] { "d_FilterSelectedOnly", "FilterSelectedOnly" },
            ["tool-web"] = new[] { "d_CloudConnect", "CloudConnect" },
            ["tool-default"] = new[] { "d_SettingsIcon", "SettingsIcon" },

            ["ui-attach"] = new[] { "d_TreeEditor.Duplicate", "TreeEditor.Duplicate" },
            ["ui-file"] = new[] { "d_TextAsset Icon", "TextAsset Icon" },
            ["ui-external"] = new[] { "d_SceneViewTools", "SceneViewTools" },
            ["ui-copy"] = new[] { "d_TreeEditor.Duplicate", "TreeEditor.Duplicate" },

            ["cmd-check"] = new[] { "d_FilterSelectedOnly", "FilterSelectedOnly" },
            ["cmd-settings"] = new[] { "d_SettingsIcon", "SettingsIcon" },
            ["cmd-help"] = new[] { "d__Help", "_Help" },
            ["cmd-chat"] = new[] { "d_UnityEditor.ConsoleWindow", "UnityEditor.ConsoleWindow" },
            ["cmd-plug"] = new[] { "d_Profiler.NetworkMessages", "Profiler.NetworkMessages" },
            ["cmd-screenshot"] = new[] { "d_SceneViewFx", "SceneViewFx" },
            ["cmd-selection"] = new[] { "d_RectTransformBlueprint", "RectTransformBlueprint" },
            ["cmd-gameview"] = new[] { "d_UnityEditor.GameView", "UnityEditor.GameView", "d_PlayButton", "PlayButton" },
        };

        public static Texture2D GetIcon(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            if (!IconCandidates.TryGetValue(key, out var candidates))
            {
                return null;
            }

            for (var i = 0; i < candidates.Length; i++)
            {
                var icon = LoadIcon(candidates[i]);
                if (icon != null)
                {
                    return icon;
                }
            }

            return null;
        }

        public static void ApplyToLabel(Label label, string key, string fallbackText, float sizePx = 14f)
        {
            if (label == null)
            {
                return;
            }

            var icon = GetIcon(key);
            if (icon == null)
            {
                label.style.backgroundImage = StyleKeyword.None;
                label.text = fallbackText;
                label.style.width = StyleKeyword.Auto;
                label.style.height = StyleKeyword.Auto;
                label.style.minWidth = StyleKeyword.Null;
                label.style.minHeight = StyleKeyword.Null;
                return;
            }

            label.text = string.Empty;
            label.style.backgroundImage = new StyleBackground(icon);
            label.style.width = sizePx;
            label.style.height = sizePx;
            label.style.minWidth = sizePx;
            label.style.minHeight = sizePx;
        }

        private static Texture2D LoadIcon(string iconName)
        {
            if (string.IsNullOrWhiteSpace(iconName))
            {
                return null;
            }

            if (LoadIconMethod != null)
            {
                try
                {
                    if (LoadIconMethod.Invoke(null, new object[] { iconName }) is Texture2D texture)
                    {
                        return texture;
                    }
                }
                catch
                {
                    // Fall back to the public lookup path below.
                }
            }

            return EditorGUIUtility.FindTexture(iconName);
        }
    }
}
