// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Infrastructure.Pro
{
    internal static class ProOfferEndpoints
    {
        // Combined config (offer + releases). Final Firebase URL pinned in spec 2.
        public const string ManifestUrl = "https://ryx-sidekick.pro/config.json";
        public const int TimeoutSeconds = 8;
    }
}
