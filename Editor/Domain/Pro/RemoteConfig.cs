// SPDX-License-Identifier: GPL-3.0-only
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Updates;

namespace Ryx.Sidekick.Editor.Domain.Pro
{
    internal sealed class RemoteConfig
    {
        public int SchemaVersion { get; set; }
        public ProOfferManifest Offer { get; set; }
        public ReleasesInfo Releases { get; set; }
        public JToken Announcement { get; set; }
        public McpRecommendationsManifest McpRecommendations { get; set; }
    }
}
