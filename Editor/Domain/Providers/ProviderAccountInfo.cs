// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor.Providers
{
    /// <summary>
    /// Account information returned by a provider's initialize handshake
    /// (e.g. Claude CLI <c>control_response / initialize</c> or Codex <c>account/read</c>).
    /// All fields are optional — populate only what the provider returns.
    /// </summary>
    [Serializable]
    internal sealed class ProviderAccountInfo
    {
        /// <summary>Provider that owns this account (e.g. "claude", "codex").</summary>
        public string ProviderId { get; set; }

        /// <summary>Authenticated user e-mail address.</summary>
        public string Email { get; set; }

        /// <summary>Organisation name as reported by the provider.</summary>
        public string Organization { get; set; }

        /// <summary>Human-readable subscription tier (e.g. "Claude Team", "Pro").</summary>
        public string SubscriptionType { get; set; }

        /// <summary>API provider identifier (e.g. "firstParty", "bedrock").</summary>
        public string ApiProvider { get; set; }

        /// <summary>
        /// Additional account-type discriminator (usage varies by provider;
        /// empty when not applicable).
        /// </summary>
        public string AccountType { get; set; }

        /// <summary>
        /// When <c>true</c> the provider still requires the user to authenticate.
        /// <c>false</c> means an authenticated session was confirmed by the CLI.
        /// </summary>
        public bool RequiresAuth { get; set; }
    }
}
