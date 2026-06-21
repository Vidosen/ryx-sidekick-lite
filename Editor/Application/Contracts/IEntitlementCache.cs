// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    internal interface IEntitlementCache
    {
        string Read();          // cached entitlement token, or null
        void Write(string token);
        void Clear();
    }
}
