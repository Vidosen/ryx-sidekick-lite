// SPDX-License-Identifier: GPL-3.0-only
using Newtonsoft.Json.Linq;

namespace Ryx.Sidekick.Editor.Domain.Pro
{
    /// <summary>One curated MCP server recommendation (B4). Title + Url are required to render;
    /// the rest are optional. <see cref="Install"/> is a reserved opaque object for a future v2
    /// prefill feature — readers MUST ignore it in v1.</summary>
    internal sealed class McpRecommendationItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string ShortDescription { get; set; }
        public string IconUrl { get; set; }
        public string Url { get; set; }
        public JToken Install { get; set; } // reserved (v2); ignored in v1
    }
}
