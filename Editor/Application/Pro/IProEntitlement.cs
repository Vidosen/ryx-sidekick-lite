// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.UseCases.Pro
{
    /// <summary>
    /// Snapshot of the user's Pro entitlement, derived offline from the cached entitlement token.
    /// <c>default</c> means "not entitled" (no token / invalid signature).
    /// </summary>
    internal readonly struct ProEntitlementInfo
    {
        /// <summary>True when a valid (signature-verified) entitlement token is cached, regardless of
        /// the support window — Pro features are perpetual, so an expired support window still "owns" Pro.</summary>
        public readonly bool OwnsPro;

        /// <summary>True when the support/update window is still open (<c>now &lt;= supportUntil</c>).</summary>
        public readonly bool SupportActive;

        public readonly string Sku;
        public readonly int EditionYear;

        /// <summary>Support-window end (unix seconds). Drives which release version is downloadable.</summary>
        public readonly long SupportUntil;

        public ProEntitlementInfo(bool ownsPro, bool supportActive, string sku, int editionYear, long supportUntil)
        {
            OwnsPro = ownsPro;
            SupportActive = supportActive;
            Sku = sku;
            EditionYear = editionYear;
            SupportUntil = supportUntil;
        }
    }

    /// <summary>
    /// Reads the user's Pro entitlement from the locally cached, offline-verified entitlement token.
    /// The token is written by both sign-in paths (account login and license-key activation), so a
    /// single signal covers both. No network calls.
    /// </summary>
    internal interface IProEntitlement
    {
        ProEntitlementInfo Get();
    }
}
