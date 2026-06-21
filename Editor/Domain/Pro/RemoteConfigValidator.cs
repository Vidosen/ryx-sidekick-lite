// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Domain.Pro
{
    internal static class RemoteConfigValidator
    {
        public const int SupportedEnvelopeVersion = 1;
        public const int SupportedOfferVersion = 1;
        public const int SupportedRecommendationsVersion = 1;

        public static bool IsValidEnvelope(RemoteConfig config)
            => config != null && config.SchemaVersion == SupportedEnvelopeVersion;

        public static bool IsValidOffer(ProOfferManifest offer)
            => offer != null
               && offer.SchemaVersion == SupportedOfferVersion
               && offer.Enabled
               && !string.IsNullOrWhiteSpace(offer.Headline)
               && offer.Cta != null
               && !string.IsNullOrWhiteSpace(offer.Cta.Label);

        // Version gate only (forward-compat: unknown version => treat as absent). The section does the
        // per-item filtering + hides itself when no renderable items survive.
        public static bool IsValidRecommendations(McpRecommendationsManifest reco)
            => reco != null
               && reco.SchemaVersion == SupportedRecommendationsVersion
               && reco.Items != null;

        // An item renders only with both a title and a url.
        public static bool IsRenderableItem(McpRecommendationItem item)
            => item != null
               && !string.IsNullOrWhiteSpace(item.Title)
               && !string.IsNullOrWhiteSpace(item.Url);
    }
}
