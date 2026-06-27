// SPDX-License-Identifier: GPL-3.0-only
using UnityEditor;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// MCP settings page (Project/Sidekick/MCP). A thin host: renders a page-level applicability
    /// HelpBox, then composes the registered IMcpSettingsSection list. Plain UI Toolkit (no App UI).
    /// </summary>
    internal sealed class SidekickMcpSettingsProvider : SettingsProvider
    {
        public SidekickMcpSettingsProvider(string path, SettingsScope scope) : base(path, scope) { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SidekickMcpSettingsProvider("Project/Sidekick/MCP", SettingsScope.Project)
            {
                keywords = new[] { "Sidekick", "MCP", "server", "transport", "http", "stdio", "unity" }
            };
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            // Fill the settings pane so the ScrollView below has a bounded height to scroll within
            // (the page now stacks several sections and overflows shorter windows).
            rootElement.style.flexGrow = 1;

            var root = SidekickSettingsSectionBuilder.CreateRoot();
            root.AddToClassList("sk-mcpset-page"); // scopes the Sidekick chat-style theme to this page only
            root.style.flexGrow = 1;
            LoadMcpStyleSheet(root);
            rootElement.Add(root);

            var scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                style =
                {
                    flexGrow = 1
                }
            };
            root.Add(scroll);

            var content = new VisualElement(); // a child we own and can rebuild without re-adding the page root
            scroll.Add(content);

            Render(content);

            // Notify the Presentation bridge LAST so it can create a local modal layer for the
            // in-place Paywall. Done after the page root is added so the page stays the first
            // child of rootElement (the modal layer's overlay root is absolute-positioned and
            // brought to front on Show, so its sibling order does not affect paint order).
            SidekickSettingsModalHost.NotifyActivated(rootElement);

            rootElement.RegisterCallback<DetachFromPanelEvent>(_ => SidekickSettingsModalHost.NotifyDeactivated(rootElement));
        }

        private static void Render(VisualElement content)
        {
            content.Clear();
            var settings = SidekickSettings.instance;

            if (!settings.ActiveProvider?.SupportsMcpConfig ?? false)
            {
                content.Add(SidekickSettingsSectionBuilder.Help(
                    $"The active provider ({settings.ActiveProvider?.DisplayName}) does not consume MCP config from Sidekick. " +
                    "These settings still apply to providers that do."));
            }

            var ctx = new McpSettingsSectionContext(settings, () => Render(content));
            foreach (var section in McpSettingsSectionRegistry.Sections)
                content.Add(section.Build(ctx));
        }

        private static void LoadMcpStyleSheet(VisualElement root)
        {
            const string path = "Packages/com.ryxinteractive.sidekick/Editor/Infrastructure/Settings/SidekickSettings.Mcp.uss";
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (uss != null) root.styleSheets.Add(uss);
        }
    }
}
