// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Unity.AppUI.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    /// <summary>
    /// Scoped App UI shell: a <c>canvas → Panel</c> hierarchy that isolates the
    /// App UI theme from the existing Sidekick UXML tree (APPUI-T01-03).
    /// </summary>
    internal sealed class SidekickAppPanel
    {
        internal const string CanvasName = "appui-canvas";

        /// <summary>The wrapper element that carries the App UI TSS.</summary>
        internal VisualElement Canvas { get; }

        /// <summary>The App UI <see cref="Panel"/> with popup/notification/tooltip layers.</summary>
        internal Panel Panel { get; }

        internal VisualElement ContentContainer => Panel.contentContainer;
        internal VisualElement PopupContainer => Panel.popupContainer;
        internal VisualElement NotificationContainer => Panel.notificationContainer;
        internal VisualElement TooltipContainer => Panel.tooltipContainer;

        SidekickAppPanel(VisualElement canvas, Panel panel)
        {
            Canvas = canvas;
            Panel = panel;
        }

        /// <summary>
        /// Builds the scoped canvas/Panel hierarchy.  Returns <c>false</c> when the
        /// App UI theme cannot be loaded — the caller should continue without it.
        /// </summary>
        internal static bool TryCreate(out SidekickAppPanel result)
        {
            result = null;

            var canvas = new VisualElement
            {
                name = CanvasName,
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = new StyleLength(StyleKeyword.Auto)
                }
            };

            var panel = new Panel
            {
                theme = EditorGUIUtility.isProSkin ? "editor-dark" : "editor-light",
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = new StyleLength(StyleKeyword.Auto)
                }
            };

            canvas.Add(panel);

            // Scope App UI theme to popup/notification/tooltip layers only. If we attached
            // it to canvas, the imported unity-theme://default rules would cascade onto
            // main UXML in contentContainer and override our .sk-* button styling.
            if (!SidekickAppTheme.Apply(panel.popupContainer))
                return false;
            SidekickAppTheme.Apply(panel.notificationContainer);
            SidekickAppTheme.Apply(panel.tooltipContainer);

            // Layer SidekickWindow.uss on top of the App UI theme so our .sk-* rules win
            // same-specificity ties for content rendered into popovers (popover content is
            // a child of popupContainer, so it inherits these styleSheets).
            var sidekickStyles = AssetDatabase.LoadAssetAtPath<StyleSheet>(SidekickUiConstants.MainWindowUss);
            if (sidekickStyles != null)
            {
                panel.popupContainer.styleSheets.Add(sidekickStyles);
                panel.notificationContainer.styleSheets.Add(sidekickStyles);
                panel.tooltipContainer.styleSheets.Add(sidekickStyles);
            }

            // Cascade SidekickWindow.uxml partial stylesheets onto the App UI overlay layers.
            // Modal/Toast content is built off-tree and reparented under popupContainer /
            // notificationContainer by App UI, so it does NOT inherit the <Style src="..."/>
            // declarations at the root of SidekickWindow.uxml. Without this loop, .sk-perm-*,
            // .sk-ask-*, .sk-onboarding-* (and other partial rules) would not apply to Modal
            // content — regression introduced by APPUI-T08-04/05/06.
            // IMPORTANT: keep SidekickUiConstants.MainWindowPartialUssPaths in sync with the
            // <Style src="..."/> block at the top of SidekickWindow.uxml.
            foreach (var ussPath in SidekickUiConstants.MainWindowPartialUssPaths)
            {
                var partial = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
                if (partial == null)
                    continue;
                panel.popupContainer.styleSheets.Add(partial);
                panel.notificationContainer.styleSheets.Add(partial);
                panel.tooltipContainer.styleSheets.Add(partial);
            }

            result = new SidekickAppPanel(canvas, panel);
            return true;
        }

        /// <summary>
        /// Surgically extends the App UI theme onto the <c>status-bar</c> element inside
        /// <see cref="ContentContainer"/> so the <c>&lt;appui:Button&gt;</c>/<c>&lt;appui:Pressable&gt;</c> chips
        /// introduced in APPUI-T08-02 receive theme tokens. Must be called AFTER the
        /// SidekickWindow UXML has been mounted into <see cref="ContentContainer"/> by
        /// <c>SidekickWindowView.TryCreate</c>; calling it earlier is a no-op because the
        /// status-bar element does not exist yet.
        /// </summary>
        internal void ApplyThemeToStatusBar()
        {
            var statusBar = ContentContainer?.Q<VisualElement>("status-bar");
            if (statusBar != null)
                SidekickAppTheme.Apply(statusBar);
        }
    }
}
