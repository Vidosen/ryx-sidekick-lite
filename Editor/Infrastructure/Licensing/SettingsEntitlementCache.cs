// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Licensing
{
    internal sealed class SettingsEntitlementCache : IEntitlementCache
    {
        public string Read()
        {
            var t = SidekickSettings.instance.EntitlementToken;
            return string.IsNullOrEmpty(t) ? null : t;
        }
        public void Write(string token) => SidekickSettings.instance.EntitlementToken = token;
        public void Clear() => SidekickSettings.instance.EntitlementToken = string.Empty;
    }
}
