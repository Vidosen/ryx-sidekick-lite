// SPDX-License-Identifier: GPL-3.0-only
using Unity.AppUI.Redux;

namespace Ryx.Sidekick.Editor.Presentation.State
{
    internal static class ProviderStateSlice
    {
        internal static readonly Slice<ProviderState, SidekickAppState> Slice =
            new(
                SidekickStateSliceNames.Provider,
                ProviderState.Default,
                reducers =>
                {
                    reducers.AddCase(
                        ProviderStateActions.HydrateProviderState,
                        (_, action) => action.payload ?? ProviderState.Default);
                    reducers.AddCase(
                        ProviderStateActions.SetProvider,
                        (state, action) => state.With(providerId: action.payload));
                    reducers.AddCase(
                        ProviderStateActions.SetModel,
                        (state, action) => state.With(model: action.payload));
                    reducers.AddCase(
                        ProviderStateActions.SetCollaborationMode,
                        (state, action) => state.With(collaborationMode: action.payload));
                    reducers.AddCase(
                        ProviderStateActions.SetPermissionMode,
                        (state, action) => state.With(permissionMode: action.payload));
                    reducers.AddCase(
                        ProviderStateActions.SetReasoningEffort,
                        (state, action) => state.With(reasoningEffort: action.payload));
                },
                extraReducers =>
                {
                    extraReducers.AddCase(
                        SidekickStateActions.ApplyScopedProviderSnapshot,
                        (_, action) =>
                        {
                            var payload = action.payload;
                            return payload == null
                                ? ProviderState.Default
                                : new ProviderState(
                                    payload.ProviderId,
                                    payload.Model,
                                    payload.CollaborationMode,
                                    payload.PermissionMode,
                                    payload.ReasoningEffort);
                        });
                });
    }
}
