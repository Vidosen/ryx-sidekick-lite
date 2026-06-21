// SPDX-License-Identifier: GPL-3.0-only
using Unity.AppUI.Redux;

namespace Ryx.Sidekick.Editor.Presentation.State
{
    internal static class ProviderStateActions
    {
        internal static readonly ActionCreator<ProviderState> HydrateProviderState =
            new("provider/HydrateProviderState");

        internal static readonly ActionCreator<string> SetProvider =
            new("provider/SetProvider");

        internal static readonly ActionCreator<string> SetModel =
            new("provider/SetModel");

        internal static readonly ActionCreator<string> SetCollaborationMode =
            new("provider/SetCollaborationMode");

        internal static readonly ActionCreator<string> SetPermissionMode =
            new("provider/SetPermissionMode");
        internal static readonly ActionCreator<string> SetReasoningEffort =
            new("provider/SetReasoningEffort");
    }
}
