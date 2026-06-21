// SPDX-License-Identifier: GPL-3.0-only
#if !UNITY_6000_3_OR_NEWER
using System.Reflection;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Presentation.Constants;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Infrastructure
{
    [InitializeOnLoad]
    internal static class ToolbarService
    {
        private const string IconPath = SidekickUiConstants.ToolbarIconPath;
        private const string TooltipText = SidekickAppConstants.Toolbar.TooltipOpenSidekick;
        private const double CheckIntervalSeconds = 1.0;
        private static double _nextCheckTime;

        static ToolbarService()
        {
            EditorApplication.update += EnsureToolbarButton;
            EditorApplication.delayCall += EnsureToolbarButton;
        }

        private static void EnsureToolbarButton()
        {
            if (EditorApplication.timeSinceStartup < _nextCheckTime)
            {
                return;
            }

            _nextCheckTime = EditorApplication.timeSinceStartup + CheckIntervalSeconds;
            TryAddToolbarButton();
        }

        private static bool TryAddToolbarButton()
        {
            if (!TryGetToolbarRoot(out var root))
            {
                return false;
            }

            var rightZone = root.Q("ToolbarZoneRightAlign");
            if (rightZone == null)
            {
                return false;
            }

            if (rightZone.Q(SidekickUiConstants.ToolbarButtonName) != null)
            {
                return true;
            }

            var button = CreateToolbarButton();
            rightZone.Add(button);
            return true;
        }

        private static bool TryGetToolbarRoot(out VisualElement root)
        {
            root = null;
            var toolbarType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
            if (toolbarType == null)
            {
                return false;
            }

            var toolbars = Resources.FindObjectsOfTypeAll(toolbarType);
            if (toolbars == null || toolbars.Length == 0)
            {
                return false;
            }

            var toolbar = toolbars[0];
            var rootField = toolbarType.GetField("m_Root", BindingFlags.NonPublic | BindingFlags.Instance);
            root = rootField?.GetValue(toolbar) as VisualElement;
            if (root != null)
            {
                return true;
            }

            var rootProperty = toolbarType.GetProperty("rootVisualElement", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            root = rootProperty?.GetValue(toolbar) as VisualElement;
            return root != null;
        }

        private static Button CreateToolbarButton()
        {
            var button = new Button(SidekickWindow.ShowWindow)
            {
                name = SidekickUiConstants.ToolbarButtonName,
                tooltip = TooltipText
            };

            button.AddToClassList("unity-toolbar-button");

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon != null)
            {
                button.style.height = 22;
                button.style.marginLeft = 4;
                button.style.marginRight = 4;
                button.style.paddingLeft = 4;
                button.style.paddingRight = 6;
                button.style.alignItems = Align.Center;
                button.style.justifyContent = Justify.Center;
                button.style.flexDirection = FlexDirection.Row;

                var image = new Image
                {
                    image = icon,
                    scaleMode = ScaleMode.ScaleToFit
                };
                image.style.width = 16;
                image.style.height = 16;
                image.style.alignSelf = Align.Center;
                button.Add(image);

                var label = new Label(SidekickAppConstants.Toolbar.ActionOpenChat);
                label.style.marginLeft = 4;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.fontSize = 11;
                button.Add(label);
            }
            else
            {
                button.text = SidekickAppConstants.Toolbar.ActionOpenChat;
            }

            return button;
        }
    }
}
#endif
