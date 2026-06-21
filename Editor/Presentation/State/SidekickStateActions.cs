// SPDX-License-Identifier: GPL-3.0-only
using Unity.AppUI.Redux;

namespace Ryx.Sidekick.Editor.Presentation.State
{
    internal static class SidekickStateActions
    {
        internal static readonly ActionCreator<ActiveProviderStateSnapshot> ApplyScopedProviderSnapshot =
            new("sidekick/ApplyScopedProviderSnapshot");
    }
}
