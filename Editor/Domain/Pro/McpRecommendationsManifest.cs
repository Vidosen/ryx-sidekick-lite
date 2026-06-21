// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace Ryx.Sidekick.Editor.Domain.Pro
{
    /// <summary>Curated "Recommended for Unity" MCP servers (B4). A LITE feature — it lives in
    /// Domain/Pro only because it rides the shared remote-config envelope, NOT because it is Pro-gated.</summary>
    internal sealed class McpRecommendationsManifest
    {
        public int SchemaVersion { get; set; }
        public List<McpRecommendationItem> Items { get; set; } = new List<McpRecommendationItem>();
    }
}
