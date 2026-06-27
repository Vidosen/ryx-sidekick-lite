// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text;

namespace Ryx.Sidekick.AgentHost;

internal static class ConstantTime
{
    /// <summary>
    /// Constant-time string comparison for token auth. Returns false for any
    /// null. Uses <see cref="CryptographicOperations.FixedTimeEquals"/> over
    /// the UTF-8 bytes; length is folded into the result so differing lengths
    /// do not short-circuit (FixedTimeEquals requires equal-length spans).
    /// </summary>
    public static bool Equals(string? a, string? b)
    {
        if (a is null || b is null)
            return false;

        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);

        // Hash both to a fixed width so the comparison span length never
        // leaks the secret's length, then FixedTimeEquals the digests.
        using var sha = SHA256.Create();
        var ha = sha.ComputeHash(ba);
        var hb = sha.ComputeHash(bb);
        return CryptographicOperations.FixedTimeEquals(ha, hb);
    }
}