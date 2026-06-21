// SPDX-License-Identifier: GPL-3.0-only
using Newtonsoft.Json;

namespace Ryx.Sidekick.Editor.Domain.Licensing
{
    /// <summary>
    /// Entitlement token payload (mirrors the Phase 1a server payload, spec §6.3).
    /// Field names map to the canonical lowercase JSON keys the server signs.
    /// </summary>
    internal sealed class EntitlementPayload
    {
        [JsonProperty("v")] public int Version { get; set; }
        [JsonProperty("sku")] public string Sku { get; set; }
        [JsonProperty("ownerHash")] public string OwnerHash { get; set; }
        [JsonProperty("deviceId")] public string DeviceId { get; set; }
        [JsonProperty("seatsMax")] public int SeatsMax { get; set; }
        [JsonProperty("issuedAt")] public long IssuedAt { get; set; }
        [JsonProperty("expiresAt")] public long ExpiresAt { get; set; }
        [JsonProperty("editionYear")] public int EditionYear { get; set; }
        [JsonProperty("supportUntil")] public long SupportUntil { get; set; }
    }
}
