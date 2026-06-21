// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Models;
using Unity.AppUI.Redux;

namespace Ryx.Sidekick.Editor.Presentation.State
{
    internal sealed class SidekickStoreService : IDisposable
    {
        private readonly ISettingsStore _settingsStore;
        private readonly IStore<SidekickAppState> _store;
        private bool _disposed;

        public SidekickStoreService(ISettingsStore settingsStore)
        {
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            _store = StoreFactory.CreateStore<SidekickAppState>(
                new ISlice<SidekickAppState>[]
                {
                    ProviderStateSlice.Slice,
                    TurnStateSlice.Slice,
                    PermissionStateSlice.Slice
                });

            ApplyScopedProviderSnapshot(_settingsStore.CurrentActiveProviderState);
            _settingsStore.ActiveProviderStateChanged += HandleActiveProviderStateChanged;
        }

        internal IStore<SidekickAppState> Store => _store;

        internal ProviderState CurrentProviderState => _store.GetState().Get<ProviderState>(SidekickStateSliceNames.Provider);

        internal TurnState CurrentTurnState => _store.GetState().Get<TurnState>(SidekickStateSliceNames.Turn);

        internal PermissionState CurrentPermissionState => _store.GetState().Get<PermissionState>(SidekickStateSliceNames.Permission);

        internal void Dispatch(IAction action)
        {
            _store.Dispatch(action);
        }

        internal IDisposableSubscription SubscribeToProvider(global::System.Action<ProviderState> listener, bool fireImmediately = true)
        {
            return SubscribeToProvider(state => state, listener, fireImmediately);
        }

        internal IDisposableSubscription SubscribeToProvider<TSelected>(
            Func<ProviderState, TSelected> selector,
            global::System.Action<TSelected> listener,
            bool fireImmediately = true)
        {
            return SubscribeToSlice(
                state => selector(state.Get<ProviderState>(SidekickStateSliceNames.Provider)),
                listener,
                fireImmediately);
        }

        internal IDisposableSubscription SubscribeToTurn(global::System.Action<TurnState> listener, bool fireImmediately = true)
        {
            return SubscribeToTurn(state => state, listener, fireImmediately);
        }

        internal IDisposableSubscription SubscribeToTurn<TSelected>(
            Func<TurnState, TSelected> selector,
            global::System.Action<TSelected> listener,
            bool fireImmediately = true)
        {
            return SubscribeToSlice(
                state => selector(state.Get<TurnState>(SidekickStateSliceNames.Turn)),
                listener,
                fireImmediately);
        }

        internal IDisposableSubscription SubscribeToPermission(global::System.Action<PermissionState> listener, bool fireImmediately = true)
        {
            return SubscribeToPermission(state => state, listener, fireImmediately);
        }

        internal IDisposableSubscription SubscribeToPermission<TSelected>(
            Func<PermissionState, TSelected> selector,
            global::System.Action<TSelected> listener,
            bool fireImmediately = true)
        {
            return SubscribeToSlice(
                state => selector(state.Get<PermissionState>(SidekickStateSliceNames.Permission)),
                listener,
                fireImmediately);
        }

        internal void SetTurnStarted()
        {
            Dispatch(TurnStateActions.TurnStarted.Invoke());
        }

        internal void SetTurnStopRequested()
        {
            Dispatch(TurnStateActions.TurnStopRequested.Invoke());
        }

        internal void SetTurnFinished()
        {
            Dispatch(TurnStateActions.TurnFinished.Invoke());
        }

        internal void SetContextUsage(int usedTokens, int contextWindow)
        {
            Dispatch(TurnStateActions.ContextUsageUpdated.Invoke(new TurnContextUsagePayload(usedTokens, contextWindow)));
        }

        internal void ResetTurnState()
        {
            Dispatch(TurnStateActions.ResetTurnState.Invoke());
        }

        internal void SetPermissionQueueSummary(
            int pendingCount,
            string currentToolUseId,
            string currentToolName,
            ToolKind currentToolKind,
            string currentPathOrCommand,
            bool hasActiveRequest)
        {
            Dispatch(PermissionStateActions.PermissionQueueUpdated.Invoke(new PermissionQueueSummaryPayload(
                pendingCount,
                currentToolUseId,
                currentToolName,
                currentToolKind,
                currentPathOrCommand,
                hasActiveRequest)));
        }

        internal void ClearPermissionQueueSummary()
        {
            Dispatch(PermissionStateActions.PermissionQueueCleared.Invoke());
        }

        internal void ApplyScopedProviderSnapshot(ActiveProviderStateSnapshot snapshot)
        {
            snapshot ??= _settingsStore.CurrentActiveProviderState;
            Dispatch(SidekickStateActions.ApplyScopedProviderSnapshot.Invoke(snapshot));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _settingsStore.ActiveProviderStateChanged -= HandleActiveProviderStateChanged;
            _store.Dispose();
        }

        private void HandleActiveProviderStateChanged(ActiveProviderStateSnapshot snapshot)
        {
            snapshot ??= _settingsStore.CurrentActiveProviderState;

            var currentProviderState = CurrentProviderState;
            if (!string.Equals(snapshot.ProviderId, currentProviderState.ProviderId, StringComparison.Ordinal))
            {
                return;
            }

            var updatedProviderState = new ProviderState(
                snapshot.ProviderId,
                snapshot.Model,
                snapshot.CollaborationMode,
                snapshot.PermissionMode,
                snapshot.ReasoningEffort);
            if (currentProviderState.Equals(updatedProviderState))
            {
                return;
            }

            Dispatch(ProviderStateActions.HydrateProviderState.Invoke(updatedProviderState));
        }

        private IDisposableSubscription SubscribeToSlice<TSelected>(
            Func<SidekickAppState, TSelected> selector,
            global::System.Action<TSelected> listener,
            bool fireImmediately)
        {
            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            return _store.Subscribe(
                state => selector(state),
                value => listener(value),
                new SubscribeOptions<TSelected> { fireImmediately = fireImmediately });
        }
    }
}
