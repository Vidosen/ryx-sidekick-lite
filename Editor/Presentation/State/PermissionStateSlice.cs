// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using Unity.AppUI.Redux;

namespace Ryx.Sidekick.Editor.Presentation.State
{
    internal static class PermissionStateSlice
    {
        internal static readonly Slice<PermissionState, SidekickAppState> Slice =
            new(
                SidekickStateSliceNames.Permission,
                PermissionState.Default,
                reducers =>
                {
                    reducers.AddCase(
                        PermissionStateActions.PermissionQueueUpdated,
                        (_, action) =>
                        {
                            var payload = action.payload;
                            return payload == null
                                ? PermissionState.Default
                                : new PermissionState(
                                    payload.PendingCount,
                                    payload.CurrentToolUseId,
                                    payload.CurrentToolName,
                                    payload.CurrentToolKind,
                                    payload.CurrentPathOrCommand,
                                    payload.HasActiveRequest);
                        });
                    reducers.AddCase(
                        PermissionStateActions.PermissionQueueCleared,
                        (_, _) => new PermissionState(0, string.Empty, string.Empty, ToolKind.Unknown, string.Empty, false));
                },
                extraReducers =>
                {
                    extraReducers.AddCase(
                        SidekickStateActions.ApplyScopedProviderSnapshot,
                        (_, _) => PermissionState.Default);
                });
    }
}
