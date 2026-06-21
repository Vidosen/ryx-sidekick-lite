// SPDX-License-Identifier: GPL-3.0-only
using UnityEditor;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Providers.Claude
{
    /// <summary>
    /// Claude provider settings node (Project/Sidekick/Providers/Claude Code). Renders the common
    /// provider fields plus Claude-specific options (Bedrock). Lives in the Lite package's Claude
    /// provider folder; Codex/Cursor have their own nodes in the Pro package.
    /// </summary>
    internal sealed class ClaudeSettingsProvider : SettingsProvider
    {
        private const string ProviderId = "claude";

        public ClaudeSettingsProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new ClaudeSettingsProvider("Project/Sidekick/Providers/Claude Code", SettingsScope.Project)
            {
                keywords = new[] { "Sidekick", "Claude", "Anthropic", "model", "thinking", "bedrock", "permission", "cli" }
            };
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            var content = SidekickSettingsSectionBuilder.CreateScrollableRoot(rootElement);

            BuildContent(content);
        }

        private static void BuildContent(VisualElement content)
        {
            content.Clear();

            SidekickProviderEditorBuilder.BuildCommon(content, ProviderId, () => BuildContent(content));

            // Claude-specific: Bedrock routing, stored in the provider-keyed settings bag.
            var settings = SidekickSettings.instance;
            var advancedSection = SidekickSettingsSectionBuilder.Section("Advanced");
            content.Add(advancedSection);
            var bedrock = new Toggle { value = settings.GetProviderBool(ProviderId, ProviderSettingKeys.UseBedrock) };
            bedrock.RegisterValueChangedCallback(evt =>
                settings.SetProviderSetting(ProviderId, ProviderSettingKeys.UseBedrock, evt.newValue));
            advancedSection.Add(SidekickSettingsSectionBuilder.FieldRow("Use Bedrock", bedrock,
                "Route Claude CLI through Bedrock (requires AWS credentials)."));
        }
    }
}
