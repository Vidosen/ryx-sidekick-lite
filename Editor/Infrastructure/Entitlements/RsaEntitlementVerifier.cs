// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Ryx.Sidekick.Editor.Domain.Licensing;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Entitlements
{
    /// <summary>
    /// Offline verifier for the compact entitlement token base64url(payloadJson)"."base64url(sig).
    /// Signature is RSA-2048 PKCS#1 v1.5 over SHA-256 of the first segment's UTF-8 bytes
    /// (matches Phase 1a crypto.sign('sha256', input, rsaKey)). RSA is used because Unity's
    /// Mono runtime does not implement ECDsa.Create(). Public key supplied as RSA JWK n/e bytes.
    /// </summary>
    internal sealed class RsaEntitlementVerifier : IEntitlementVerifier
    {
        private readonly byte[] _modulus;   // JWK n
        private readonly byte[] _exponent;  // JWK e

        public RsaEntitlementVerifier(byte[] modulus, byte[] exponent)
        {
            _modulus = modulus;
            _exponent = exponent;
        }

        public EntitlementVerification Verify(string token)
        {
            if (string.IsNullOrEmpty(token)) return EntitlementVerification.Invalid;
            var parts = token.Split('.');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
                return EntitlementVerification.Invalid;

            byte[] signingInput = Encoding.UTF8.GetBytes(parts[0]);
            byte[] signature;
            try { signature = Base64UrlDecode(parts[1]); }
            catch { return EntitlementVerification.Invalid; }

            try
            {
                using (var rsa = RSA.Create())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = _modulus, Exponent = _exponent });
                    if (!rsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                        return EntitlementVerification.Invalid;
                }
            }
            catch { return EntitlementVerification.Invalid; }

            EntitlementPayload payload;
            try
            {
                var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
                payload = JsonConvert.DeserializeObject<EntitlementPayload>(json);
            }
            catch { return EntitlementVerification.Invalid; }

            return payload == null
                ? EntitlementVerification.Invalid
                : new EntitlementVerification(true, payload);
        }

        /// <summary>RFC 4648 §5 base64url decode (no padding, '-'/'_').</summary>
        public static byte[] Base64UrlDecode(string input)
        {
            string s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 1: throw new FormatException("Invalid base64url length");
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }
    }
}
