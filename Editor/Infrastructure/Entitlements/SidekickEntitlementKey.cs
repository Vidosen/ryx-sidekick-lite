// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Infrastructure.Entitlements
{
    /// <summary>
    /// The PROD entitlement RSA public key (base64url JWK n/e from
    /// `node functions/scripts/genEntitlementKeys.js`). Owner pastes these after
    /// minting the prod keypair. Until then live activation fails verification
    /// (tests use the fixture key).
    /// </summary>
    internal static class SidekickEntitlementKey
    {
        public const string PublicKeyN = "52mAp4fUvaGP9O139clZzZ3A2P1iHswdfBSGzYGGa8h0mOPrLMzFU54YKpM7Fy8kVxyllJQnccpCXK1CSYxwDJ0BfqzKCuKAosQk_T2KSF_cAl404Rwq9ShsTpqFC26wm3bSapycKVW1hy5bYmtXicKNeL_ZMU8usKOP-1sbuscN284ku8ZmWmp5vGIEyaIMQ2aGnCpzhr1kUT3GYqyUxdV1uc0MjLHsylo3B0OlHdfYm7NG_38_-fI9Epg2pU_9sOUxXtm9kBQBxs9GQ5QmpfgQ8bSOboPtVgxbXTe47kT3W9LMJ3h9yfmclM6OWgY8fAkUvgtBDfoxQFGD03gFxw"; // prod JWK "n" (RSA-2048 modulus, base64url)
        public const string PublicKeyE = "AQAB"; // prod JWK "e" (RSA exponent 65537, base64url)
    }
}
