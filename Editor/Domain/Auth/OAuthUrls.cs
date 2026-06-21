// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Domain.Auth
{
    /// <summary>
    /// URLs for the OAuth flow (automatic loopback redirect plus the manual paste-back URL).
    /// Pure DTO — no I/O dependencies — lives in Domain so contracts in Application can
    /// reference it without pulling in Infrastructure.
    /// </summary>
    internal class OAuthUrls
    {
        public string AutomaticRedirectUrl;
        public string ManualRedirectUrl;
    }
}
