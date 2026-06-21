// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Licensing;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    internal readonly struct EntitlementVerification
    {
        public readonly bool Valid;
        public readonly EntitlementPayload Payload;
        public EntitlementVerification(bool valid, EntitlementPayload payload)
        {
            Valid = valid;
            Payload = payload;
        }
        public static EntitlementVerification Invalid => new EntitlementVerification(false, null);
    }

    /// <summary>
    /// Verifies an entitlement token's signature OFFLINE (spec §6.3). Does not check
    /// expiry — the caller (LicenseService) decides based on the payload's expiresAt.
    /// </summary>
    internal interface IEntitlementVerifier
    {
        EntitlementVerification Verify(string token);
    }
}
