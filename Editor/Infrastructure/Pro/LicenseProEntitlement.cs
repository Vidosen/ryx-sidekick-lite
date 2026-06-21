// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Pro;
// IClock lives in the Ryx.Sidekick.Editor namespace (Application/Contracts/IClock.cs).
using Ryx.Sidekick.Editor;

namespace Ryx.Sidekick.Editor.Infrastructure.Pro
{
    /// <summary>
    /// <see cref="IProEntitlement"/> backed by the cached, offline-verified entitlement token.
    /// Mirrors the offline check in <c>LicenseService.GetStatus</c>/<c>StatusFrom</c> but without the
    /// network/activation surface — it only answers "does this user own Pro, and is the support window
    /// still open". A valid signature means the user owns Pro even if the token's freshness window has
    /// lapsed (Pro features are perpetual).
    /// </summary>
    internal sealed class LicenseProEntitlement : IProEntitlement
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly IEntitlementCache _cache;
        private readonly IEntitlementVerifier _verifier;
        private readonly IClock _clock;

        public LicenseProEntitlement(IEntitlementCache cache, IEntitlementVerifier verifier, IClock clock)
        {
            _cache = cache;
            _verifier = verifier;
            _clock = clock;
        }

        public ProEntitlementInfo Get()
        {
            var token = _cache?.Read();
            if (string.IsNullOrEmpty(token) || _verifier == null)
                return default;

            var verification = _verifier.Verify(token);
            if (!verification.Valid || verification.Payload == null)
                return default;

            var p = verification.Payload;

            // IClock.Now is LOCAL; convert to UTC before epoch math (matches LicenseService.StatusFrom).
            long now = _clock != null
                ? (long)(_clock.Now.ToUniversalTime() - UnixEpoch).TotalSeconds
                : 0L;

            // OwnsPro is SKU-gated: only a 'pro' token means Pro ownership. A free 'lite' token — minted
            // for signed-in users so they can pull free Lite updates through the same signed-URL channel —
            // is a valid, signature-verified token but must NOT flip the paywall/chip into an "owns Pro"
            // state. Sku/EditionYear/SupportUntil still reflect the actual token so the unified update flow
            // can read the free token's support window.
            bool ownsPro = string.Equals(p.Sku, "pro", StringComparison.OrdinalIgnoreCase);
            bool supportActive = p.SupportUntil <= 0 || now <= p.SupportUntil;
            return new ProEntitlementInfo(
                ownsPro: ownsPro,
                supportActive: supportActive,
                sku: p.Sku,
                editionYear: p.EditionYear,
                supportUntil: p.SupportUntil);
        }
    }
}
