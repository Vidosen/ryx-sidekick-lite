// SPDX-License-Identifier: GPL-3.0-only
using System;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// One ordered, registrable block on the MCP Project Settings page (Project/Sidekick/MCP).
    /// Sections are pure functions of <see cref="SidekickSettings"/>: scalar edits auto-save via the
    /// settings property setters; structural changes call <see cref="McpSettingsSectionContext.RequestRebuild"/>
    /// to re-render the whole page. Plain UI Toolkit only — no App UI.
    /// </summary>
    internal interface IMcpSettingsSection
    {
        /// <summary>Stable identity. Replace-by-Id in the registry (Pro can override a Lite section by reusing its Id).</summary>
        string Id { get; }

        /// <summary>Ascending sort key. Built-ins: config-source=10, servers=20. Reserved: B4~15, B5~30, B6~90.</summary>
        int Order { get; }

        /// <summary>Builds the section's subtree. May return an empty container; never null.</summary>
        VisualElement Build(McpSettingsSectionContext ctx);
    }

    /// <summary>Immutable per-render context handed to each section. No shared mutable state between sections.</summary>
    internal sealed class McpSettingsSectionContext
    {
        public SidekickSettings Settings { get; }

        /// <summary>Re-renders the entire MCP settings page. Call after any STRUCTURAL change
        /// (add/remove server, add/remove arg/env/header, transport switch, custom-config toggle).
        /// Do NOT call for scalar/text field edits — those only SaveSettings.</summary>
        public Action RequestRebuild { get; }

        public McpSettingsSectionContext(SidekickSettings settings, Action requestRebuild)
        {
            Settings = settings;
            RequestRebuild = requestRebuild;
        }
    }
}
