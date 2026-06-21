// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using Ryx.Sidekick.Editor.Presentation.State;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Unity.AppUI.Redux;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    internal sealed class ProviderMenuDisplayBinder : IDisposable
    {
        private readonly IProviderCatalog _providerCatalog;
        private readonly IDisposableSubscription _providerSubscription;

        private IProviderMenuView _view;
        private ProviderMenuDisplayState _currentState = ProviderMenuDisplayState.Default;
        private bool _disposed;

        public ProviderMenuDisplayBinder(IProviderCatalog providerCatalog, SidekickStoreService storeService)
        {
            _providerCatalog = providerCatalog ?? throw new ArgumentNullException(nameof(providerCatalog));
            if (storeService == null)
            {
                throw new ArgumentNullException(nameof(storeService));
            }

            _providerSubscription = storeService.SubscribeToProvider(HandleProviderStateChanged);
        }

        public void BindView(IProviderMenuView view)
        {
            _view = view;
            Render();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _view = null;
            _providerSubscription.Dispose();
        }

        private void HandleProviderStateChanged(ProviderState state)
        {
            _currentState = MapState(state);
            Render();
        }

        private ProviderMenuDisplayState MapState(ProviderState state)
        {
            state ??= ProviderState.Default;

            var providerModule = FindProviderModule(state.ProviderId);
            var metadata = providerModule?.Metadata;
            var providerDisplayName = !string.IsNullOrWhiteSpace(metadata?.DisplayName)
                ? metadata.DisplayName
                : state.ProviderId ?? string.Empty;
            var normalizedSelection = metadata?.NormalizeModeSelection(state.CollaborationMode, state.PermissionMode)
                ?? new ProviderModeSelection(state.CollaborationMode, state.PermissionMode);

            var collaborationModes = metadata?.CollaborationModes ?? Array.Empty<CollaborationModeDescriptor>();
            var collaborationMode = ResolveCollaborationMode(collaborationModes, normalizedSelection.CollaborationMode);
            var collaborationModeVisible = collaborationModes.Length > 1;

            var permissionModes = metadata?.GetPermissionModes(collaborationMode.Value) ?? Array.Empty<PermissionModeDescriptor>();
            var permissionMode = ResolvePermissionMode(permissionModes, normalizedSelection.PermissionMode);
            var permissionModeVisible = permissionModes.Length > 0;

            return new ProviderMenuDisplayState(
                providerDisplayName,
                state.Model,
                collaborationModeVisible,
                collaborationMode.Label,
                collaborationMode.Icon,
                permissionModeVisible,
                permissionMode.Label,
                permissionMode.Icon);
        }

        private void Render()
        {
            if (_view == null)
            {
                return;
            }

            _view.SetProviderDisplay(_currentState.ProviderDisplayName);
            _view.SetModelDisplay(_currentState.ModelDisplayName);
            _view.SetCollaborationModeDisplay(_currentState.CollaborationModeDisplayName);
            _view.SetCollaborationModeIcon(_currentState.CollaborationModeIconName);
            _view.SetCollaborationModeVisible(_currentState.IsCollaborationModeVisible);
            _view.SetPermissionModeDisplay(_currentState.PermissionModeDisplayName);
            _view.SetPermissionModeIcon(_currentState.PermissionModeIconName);
            _view.SetPermissionModeVisible(_currentState.IsPermissionModeVisible);
        }

        private IProviderModule FindProviderModule(string providerId)
        {
            return _providerCatalog.GetProvider(providerId);
        }

        private static CollaborationModeDescriptor ResolveCollaborationMode(
            CollaborationModeDescriptor[] modes,
            string currentMode)
        {
            if (modes == null || modes.Length == 0)
            {
                return default;
            }

            var descriptor = modes.FirstOrDefault(mode => string.Equals(mode.Value, currentMode, StringComparison.Ordinal));
            return string.IsNullOrEmpty(descriptor.Value)
                ? modes[0]
                : descriptor;
        }

        private static PermissionModeDescriptor ResolvePermissionMode(
            PermissionModeDescriptor[] modes,
            string currentMode)
        {
            if (modes == null || modes.Length == 0)
            {
                return default;
            }

            var descriptor = modes.FirstOrDefault(mode => string.Equals(mode.Value, currentMode, StringComparison.Ordinal));
            return string.IsNullOrEmpty(descriptor.Value)
                ? modes[0]
                : descriptor;
        }

        private readonly struct ProviderMenuDisplayState
        {
            internal static readonly ProviderMenuDisplayState Default =
                new(string.Empty, string.Empty, false, string.Empty, null, false, string.Empty, null);

            public ProviderMenuDisplayState(
                string providerDisplayName,
                string modelDisplayName,
                bool isCollaborationModeVisible,
                string collaborationModeDisplayName,
                string collaborationModeIconName,
                bool isPermissionModeVisible,
                string permissionModeDisplayName,
                string permissionModeIconName)
            {
                ProviderDisplayName = providerDisplayName ?? string.Empty;
                ModelDisplayName = modelDisplayName ?? string.Empty;
                IsCollaborationModeVisible = isCollaborationModeVisible;
                CollaborationModeDisplayName = collaborationModeDisplayName ?? string.Empty;
                CollaborationModeIconName = collaborationModeIconName;
                IsPermissionModeVisible = isPermissionModeVisible;
                PermissionModeDisplayName = permissionModeDisplayName ?? string.Empty;
                PermissionModeIconName = permissionModeIconName;
            }

            public string ProviderDisplayName { get; }

            public string ModelDisplayName { get; }

            public bool IsCollaborationModeVisible { get; }

            public string CollaborationModeDisplayName { get; }

            public string CollaborationModeIconName { get; }

            public bool IsPermissionModeVisible { get; }

            public string PermissionModeDisplayName { get; }

            public string PermissionModeIconName { get; }
        }
    }
}
