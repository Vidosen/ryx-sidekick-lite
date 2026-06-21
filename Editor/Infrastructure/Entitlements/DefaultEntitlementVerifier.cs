// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Entitlements
{
    /// <summary>
    /// Parameterless <see cref="IEntitlementVerifier"/> seeded from the baked production public key
    /// (<see cref="SidekickEntitlementKey"/>). Exists so the verifier can be registered type-based in
    /// the App UI DI container (which has no factory-lambda registrations). Delegates to a
    /// <see cref="RsaEntitlementVerifier"/> built from the key, mirroring the inline construction in
    /// SidekickGeneralSettingsProvider's License section.
    /// </summary>
    internal sealed class DefaultEntitlementVerifier : IEntitlementVerifier
    {
        private readonly RsaEntitlementVerifier _inner = new RsaEntitlementVerifier(
            RsaEntitlementVerifier.Base64UrlDecode(
                string.IsNullOrEmpty(SidekickEntitlementKey.PublicKeyN) ? "AQAB" : SidekickEntitlementKey.PublicKeyN),
            RsaEntitlementVerifier.Base64UrlDecode(
                string.IsNullOrEmpty(SidekickEntitlementKey.PublicKeyE) ? "AQAB" : SidekickEntitlementKey.PublicKeyE));

        public EntitlementVerification Verify(string token) => _inner.Verify(token);
    }
}
