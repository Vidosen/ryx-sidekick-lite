// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Pro
{
    /// <summary>
    /// Single decision point for the Pro gate: combines whether the Pro package is installed
    /// (<see cref="IProPresence"/>) with whether the user owns Pro (<see cref="IProEntitlement"/>)
    /// into a <see cref="ProAccessState"/>. All gate surfaces (paywall modal, status-bar chip,
    /// locked provider rows, skills palette, MCP upsell) read from this so behaviour stays consistent.
    /// </summary>
    internal sealed class ResolveProAccessStateQuery
    {
        private readonly IProPresence _presence;
        private readonly IProEntitlement _entitlement;

        public ResolveProAccessStateQuery(IProPresence presence, IProEntitlement entitlement)
        {
            _presence = presence;
            _entitlement = entitlement;
        }

        public ProAccessState Resolve()
        {
            if (_presence != null && _presence.IsInstalled)
                return ProAccessState.Installed;

            if (_entitlement != null && _entitlement.Get().OwnsPro)
                return ProAccessState.OwnedNotInstalled;

            return ProAccessState.Locked;
        }
    }
}
