// SPDX-License-Identifier: GPL-3.0-only
using System;
using Unity.AppUI.Redux;

namespace Ryx.Sidekick.Editor.Presentation.State
{
    [Serializable]
    internal sealed class TurnContextUsagePayload
    {
        internal TurnContextUsagePayload(int usedTokens, int contextWindow)
        {
            UsedTokens = usedTokens;
            ContextWindow = contextWindow;
        }

        public int UsedTokens { get; }

        public int ContextWindow { get; }
    }

    internal static class TurnStateActions
    {
        internal static readonly ActionCreator TurnStarted =
            new("turn/TurnStarted");

        internal static readonly ActionCreator TurnStopRequested =
            new("turn/TurnStopRequested");

        internal static readonly ActionCreator TurnFinished =
            new("turn/TurnFinished");

        internal static readonly ActionCreator<TurnContextUsagePayload> ContextUsageUpdated =
            new("turn/ContextUsageUpdated");

        internal static readonly ActionCreator ResetTurnState =
            new("turn/ResetTurnState");
    }
}
