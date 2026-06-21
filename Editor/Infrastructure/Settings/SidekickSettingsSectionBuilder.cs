// SPDX-License-Identifier: GPL-3.0-only
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Shared UI Toolkit helpers for the Sidekick Project Settings pages (General / Providers / MCP).
    /// Plain UI Toolkit only — no App UI components — so it can live in Infrastructure alongside the
    /// SettingsProviders. Field rendering is defined here once and reused across all three pages.
    /// </summary>
    internal static class SidekickSettingsSectionBuilder
    {
        private const string SettingsUss =
            "Packages/com.ryxinteractive.sidekick/Editor/Infrastructure/Settings/SidekickSettings.uss";

        /// <summary>Creates the page root, applies the shared stylesheet, and returns it.</summary>
        public static VisualElement CreateRoot()
        {
            var root = new VisualElement();
            root.AddToClassList("sk-settings-root");
            root.AddToClassList("sk-settings-page");
            root.style.flexGrow = 1;

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(SettingsUss);
            if (uss != null)
            {
                root.styleSheets.Add(uss);
            }

            return root;
        }

        public static VisualElement CreateScrollableRoot(VisualElement rootElement)
        {
            rootElement.style.flexGrow = 1;

            var root = CreateRoot();
            rootElement.Add(root);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            root.Add(scroll);

            var content = new VisualElement();
            content.AddToClassList("sk-settings-content");
            scroll.Add(content);
            return content;
        }

        // Structural layout is applied inline so it does not depend on the stylesheet being
        // resolved in the Project Settings panel context; the USS only carries cosmetics.

        public static Label SectionHeader(string text)
        {
            var label = new Label(text);
            label.AddToClassList("sk-settings-header");
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 0;
            label.style.marginBottom = 10;
            return label;
        }

        public static VisualElement Section(string title)
        {
            var section = new VisualElement();
            section.AddToClassList("sk-settings-section");
            section.Add(SectionHeader(title));
            return section;
        }

        /// <summary>A label-on-the-left, control-on-the-right row.</summary>
        public static VisualElement FieldRow(string label, VisualElement control, string tooltip = null)
        {
            var row = new VisualElement();
            row.AddToClassList("sk-settings-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 6;
            row.style.minHeight = 34;

            var labelElement = new Label(label);
            labelElement.AddToClassList("sk-settings-label");
            labelElement.style.width = 200;
            labelElement.style.flexShrink = 0;

            control.AddToClassList("sk-settings-control");
            control.style.flexGrow = 1;
            control.style.minHeight = 30;

            if (!string.IsNullOrEmpty(tooltip))
            {
                labelElement.tooltip = tooltip;
                control.tooltip = tooltip;
            }

            row.Add(labelElement);
            row.Add(control);
            return row;
        }

        /// <summary>A text field plus a "Browse" button. <paramref name="onBrowse"/> returns the chosen path (or null to keep).</summary>
        public static VisualElement BrowseRow(string label, TextField field, Func<string> onBrowse, string tooltip = null)
        {
            var control = new VisualElement();
            control.AddToClassList("sk-settings-browse-control");
            control.style.flexDirection = FlexDirection.Row;
            control.style.alignItems = Align.Center;
            control.style.minHeight = 30;

            field.AddToClassList("sk-settings-grow");
            field.style.flexGrow = 1;
            field.style.minHeight = 30;

            var button = new Button(() =>
            {
                var picked = onBrowse?.Invoke();
                if (!string.IsNullOrEmpty(picked))
                {
                    field.value = picked;
                }
            })
            {
                text = "Browse"
            };
            button.AddToClassList("sk-settings-browse-button");
            button.style.width = 64;
            button.style.height = 30;
            button.style.marginLeft = 6;

            control.Add(field);
            control.Add(button);
            return FieldRow(label, control, tooltip);
        }

        /// <summary>A horizontal container (button row, tab bar, key/value row).</summary>
        public static VisualElement HorizontalRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        public static HelpBox Help(string message, HelpBoxMessageType type = HelpBoxMessageType.Info)
        {
            var box = new HelpBox(message, type);
            box.AddToClassList("sk-settings-help");
            return box;
        }
    }
}
