// SPDX-License-Identifier: GPL-3.0-only
using UnityEditor;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Landing page for the Providers group (Project/Sidekick/Providers). Each provider has its own
    /// child node (Claude in this package; Codex/Cursor contributed by the Pro package) so this page is
    /// just a short pointer. Plain UI Toolkit (no App UI).
    /// </summary>
    internal sealed class SidekickProvidersSettingsProvider : SettingsProvider
    {
        public SidekickProvidersSettingsProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SidekickProvidersSettingsProvider("Project/Sidekick/Providers", SettingsScope.Project)
            {
                keywords = new[] { "Sidekick", "provider", "model", "permission", "thinking", "cli", "claude", "cursor", "codex" }
            };
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            var root = SidekickSettingsSectionBuilder.CreateScrollableRoot(rootElement);

            var section = SidekickSettingsSectionBuilder.Section("Providers");
            root.Add(section);
            section.Add(SidekickSettingsSectionBuilder.Help(
                "Select a provider below to configure its CLI path, model, modes and other options. " +
                "Choose the active provider on the main Sidekick page."));
        }
    }
}
