// SPDX-License-Identifier: GPL-3.0-only
using UnityEditor;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor
{
    /// <summary>The "MCP Config" block: enable toggle, custom-config toggle, custom path, prompt tool,
    /// generated path. Extracted from SidekickMcpSettingsProvider.OnActivate. Custom-only rows render
    /// conditionally on UseCustomMcpConfig (was a display flip); net-identical UX.</summary>
    internal sealed class McpConfigSourceSection : IMcpSettingsSection
    {
        public string Id => "mcp-config-source";
        public int Order => 10;

        public VisualElement Build(McpSettingsSectionContext ctx)
        {
            var settings = ctx.Settings;
            var root = new VisualElement();
            root.AddToClassList("sk-mcpset-group");

            root.Add(SidekickSettingsSectionBuilder.SectionHeader("MCP Config"));

            var enableToggle = new Toggle { value = settings.EnableMcpConfig };
            enableToggle.RegisterValueChangedCallback(evt => settings.EnableMcpConfig = evt.newValue); // setter auto-saves
            root.Add(SidekickSettingsSectionBuilder.FieldRow("Enable MCP Config", enableToggle,
                "Generate/pass MCP config to the active CLI."));

            var useCustomToggle = new Toggle { value = settings.UseCustomMcpConfig };
            useCustomToggle.RegisterValueChangedCallback(evt =>
            {
                settings.UseCustomMcpConfig = evt.newValue; // setter auto-saves
                ctx.RequestRebuild();                       // structural: flips which rows show + disables servers section
            });
            root.Add(SidekickSettingsSectionBuilder.FieldRow("Use Custom Config File", useCustomToggle,
                "Use an external MCP config JSON instead of the generated server list below."));

            if (settings.UseCustomMcpConfig)
            {
                var customPath = new TextField { value = settings.McpConfigPath };
                customPath.RegisterValueChangedCallback(evt => settings.McpConfigPath = evt.newValue);
                root.Add(SidekickSettingsSectionBuilder.BrowseRow("Custom Config Path", customPath,
                    () => EditorUtility.OpenFilePanel("Select MCP config", "", "json"),
                    "Absolute or project-relative path to an MCP config JSON."));

                var promptTool = new TextField { value = settings.McpPermissionPromptTool };
                promptTool.RegisterValueChangedCallback(evt => settings.McpPermissionPromptTool = evt.newValue);
                root.Add(SidekickSettingsSectionBuilder.FieldRow("Permission Prompt Tool", promptTool,
                    "Optional tool id passed via --permission-prompt-tool."));
            }

            var generatedPath = new TextField { value = settings.GeneratedMcpConfigPath };
            generatedPath.RegisterValueChangedCallback(evt => settings.GeneratedMcpConfigPath = evt.newValue);
            root.Add(SidekickSettingsSectionBuilder.BrowseRow("Generated Config Path", generatedPath,
                () => EditorUtility.SaveFilePanel("Select output MCP config", "", "mcp-config.generated.json", "json"),
                "Where the generated MCP config is written."));

            return root;
        }
    }
}
