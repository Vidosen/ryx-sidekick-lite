// SPDX-License-Identifier: GPL-3.0-only
using Unity.AppUI.Redux;

namespace Ryx.Sidekick.Editor.Presentation.State
{
    internal static class TurnStateSlice
    {
        internal static readonly Slice<TurnState, SidekickAppState> Slice =
            new(
                SidekickStateSliceNames.Turn,
                TurnState.Default,
                reducers =>
                {
                    reducers.AddCase(
                        TurnStateActions.TurnStarted,
                        (state, _) => state.With(isTurnActive: true, isStopping: false));
                    reducers.AddCase(
                        TurnStateActions.TurnStopRequested,
                        (state, _) => state.With(isStopping: true));
                    reducers.AddCase(
                        TurnStateActions.TurnFinished,
                        (state, _) => state.With(isTurnActive: false, isStopping: false));
                    reducers.AddCase(
                        TurnStateActions.ContextUsageUpdated,
                        (state, action) => state.With(
                            lastUsedTokens: action.payload?.UsedTokens ?? 0,
                            lastContextWindow: action.payload?.ContextWindow ?? 0));
                    reducers.AddCase(
                        TurnStateActions.ResetTurnState,
                        (_, _) => TurnState.Default);
                },
                extraReducers =>
                {
                    extraReducers.AddCase(
                        SidekickStateActions.ApplyScopedProviderSnapshot,
                        (_, _) => TurnState.Default);
                });
    }
}
