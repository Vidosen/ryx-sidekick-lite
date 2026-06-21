// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.Renderers;
using Ryx.Sidekick.Editor.Presentation.State;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.UseCases.Permissions;
using Unity.AppUI.MVVM;
using Debug = UnityEngine.Debug;

namespace Ryx.Sidekick.Editor.Presentation.ViewModels
{
    [ObservableObject]
    internal partial class PermissionOverlayViewModel : IDisposable
    {
        private readonly ResolvePermissionUseCase _resolvePermissionUseCase;
        private readonly SidekickStoreService _storeService;

        /// <summary>
        /// Re-wired per provider scope via <see cref="SetComposerViewModel"/>.
        /// May be null when no provider scope is active.
        /// </summary>
        private ComposerViewModel _composerViewModel;

        private IPermissionOverlayView _view;

        // Queue of pending permissions (FIFO)
        private readonly List<PendingPermission> _queue = new();
        private int _currentIndex;
        private bool _showingFullPreview;
        private bool _disposed;

        /// <summary>
        /// Whether the overlay is currently visible.
        /// </summary>
        public bool IsActive => _queue.Count > 0;

        public PermissionOverlayViewModel(
            PermissionService permissionService,
            ResolvePermissionUseCase resolvePermissionUseCase,
            SidekickStoreService storeService)
        {
            _resolvePermissionUseCase = resolvePermissionUseCase ?? new ResolvePermissionUseCase(permissionService);
            _storeService = storeService;
        }

        /// <summary>
        /// Re-wires the <see cref="ComposerViewModel"/> reference when the provider scope
        /// changes. Call this from the factory immediately after creating the new
        /// <see cref="ComposerViewModel"/> for the scope.
        /// </summary>
        public void SetComposerViewModel(ComposerViewModel composerVm)
        {
            _composerViewModel = composerVm;
        }

        // === Commands ===

        [ICommand]
        private void Allow(bool remember)
        {
            if (_currentIndex < 0 || _currentIndex >= _queue.Count) return;

            var permission = _queue[_currentIndex];

            if (SidekickSettings.instance.VerboseLogging)
            {
                Debug.Log($"[PermissionOverlayViewModel] Allow: {permission.ToolName}, remember={remember}");
            }

            _resolvePermissionUseCase?.TryResolveDecision(permission, allow: true, remember: remember);

            AdvanceQueue();
        }

        [ICommand]
        private void Deny(bool remember)
        {
            if (_currentIndex < 0 || _currentIndex >= _queue.Count) return;

            var permission = _queue[_currentIndex];

            if (SidekickSettings.instance.VerboseLogging)
            {
                Debug.Log($"[PermissionOverlayViewModel] Deny: {permission.ToolName}, remember={remember}");
            }

            _resolvePermissionUseCase?.TryResolveDecision(permission, allow: false, remember: remember, message: "User denied permission");

            AdvanceQueue();
        }

        [ICommand]
        private void ShowMore()
        {
            _showingFullPreview = !_showingFullPreview;

            if (_currentIndex >= 0 && _currentIndex < _queue.Count)
            {
                RenderCurrent();
            }
        }

        [ICommand]
        private void Close()
        {
            Deny(remember: false);
        }

        private bool CanDecide() => _queue.Count > 0;

        // === View binding ===

        public void BindView(IPermissionOverlayView view)
        {
            if (_view != null)
            {
                _view.ClosedRequested -= OnClosedRequested;
                _view.ShowMoreRequested -= OnShowMoreRequested;
                _view.AllowRequested -= OnAllowRequested;
                _view.DenyRequested -= OnDenyRequested;
            }

            _view = view;

            if (_view == null)
            {
                return;
            }

            _view.ClosedRequested += OnClosedRequested;
            _view.ShowMoreRequested += OnShowMoreRequested;
            _view.AllowRequested += OnAllowRequested;
            _view.DenyRequested += OnDenyRequested;
            RenderCurrent();
        }

        // === Public queue management ===

        /// <summary>
        /// Enqueue a permission request. Shows the overlay if not already visible.
        /// </summary>
        public void Enqueue(PendingPermission permission)
        {
            if (permission == null || string.IsNullOrEmpty(permission.ToolUseId)) return;

            // Check for duplicates in queue
            foreach (var p in _queue)
            {
                if (p.ToolUseId == permission.ToolUseId) return;
            }

            _queue.Add(permission);
            UpdateStoreSummary();

            if (SidekickSettings.instance.VerboseLogging)
            {
                Debug.Log($"[PermissionOverlayViewModel] Enqueued permission: {permission.ToolName}, queue size: {_queue.Count}");
            }

            // Show overlay if this is the first item
            if (_queue.Count == 1)
            {
                _currentIndex = 0;
                ShowOverlay(true);
                RenderCurrent();
            }
            else
            {
                RenderCurrent();
            }
        }

        /// <summary>
        /// Called when stream completes or session resets.
        /// Clears any remaining queue without sending responses (they're stale).
        /// </summary>
        public void Reset()
        {
            if (_queue.Count > 0)
            {
                if (SidekickSettings.instance.VerboseLogging)
                {
                    Debug.Log($"[PermissionOverlayViewModel] Reset: clearing {_queue.Count} pending permissions");
                }
            }
            CloseOverlay();
        }

        // === IDisposable ===

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_view != null)
            {
                _view.ClosedRequested -= OnClosedRequested;
                _view.ShowMoreRequested -= OnShowMoreRequested;
                _view.AllowRequested -= OnAllowRequested;
                _view.DenyRequested -= OnDenyRequested;
                _view.Render(PermissionRequestViewState.Hidden);
                _view = null;
            }

            _queue.Clear();
        }

        // === Private helpers ===

        private void AdvanceQueue()
        {
            // Remove current item
            if (_currentIndex >= 0 && _currentIndex < _queue.Count)
            {
                _queue.RemoveAt(_currentIndex);
            }

            // If queue empty, close
            if (_queue.Count == 0)
            {
                CloseOverlay();
                return;
            }

            // Keep index within bounds (stay at same index since we removed the current one)
            if (_currentIndex >= _queue.Count)
            {
                _currentIndex = _queue.Count - 1;
            }

            // Render next
            _showingFullPreview = false;
            UpdateStoreSummary();
            RenderCurrent();
        }

        private void CloseOverlay()
        {
            _queue.Clear();
            _currentIndex = 0;
            _showingFullPreview = false;
            ShowOverlay(false);
            _composerViewModel?.SetInputEnabled(true);
            _storeService?.ClearPermissionQueueSummary();
        }

        private void ShowOverlay(bool show)
        {
            if (show)
            {
                _composerViewModel?.SetInputEnabled(false);
            }

            if (!show)
            {
                _view?.Render(PermissionRequestViewState.Hidden);
            }
        }

        private void RenderCurrent()
        {
            if (_view == null || _currentIndex < 0 || _currentIndex >= _queue.Count)
            {
                return;
            }

            var permission = _queue[_currentIndex];
            var previewText = BuildPreviewText(permission, out var canShowMore);

            _view.Render(new PermissionRequestViewState
            {
                IconName = MessageElementFactory.GetToolIcon(ToolPresentationCatalog.GetEffectiveKind(permission)),
                Title = "Permission Required",
                CounterText = _queue.Count > 1 ? $"{_currentIndex + 1}/{_queue.Count}" : string.Empty,
                ToolName = permission.ToolName ?? "Unknown",
                PathText = permission.FilePath,
                CommandText = ToolPresentationCatalog.GetEffectiveKind(permission) == ToolKind.Bash
                    ? permission.Command
                    : string.Empty,
                PreviewText = previewText,
                ReasonText = permission.DecisionReason,
                IsVisible = true,
                IsPreviewExpanded = _showingFullPreview,
                CanShowMore = canShowMore
            });
        }

        private string BuildPreviewText(PendingPermission permission, out bool canShowMore)
        {
            var toolKind = ToolPresentationCatalog.GetEffectiveKind(permission);
            string previewSource = null;
            var previewLimit = 0;

            if (toolKind == ToolKind.Edit && permission.Input != null)
            {
                var lines = new[]
                {
                    permission.Input["old_string"]?.ToString(),
                    permission.Input["new_string"]?.ToString()
                }
                    .Where(value => !string.IsNullOrEmpty(value))
                    .Select((value, index) => $"{(index == 0 ? "-" : "+")} {value}")
                    .ToArray();

                previewSource = string.Join(Environment.NewLine, lines);
                previewLimit = 400;
            }
            else if (toolKind == ToolKind.Write && permission.Input != null)
            {
                previewSource = permission.Input["content"]?.ToString();
                previewLimit = 300;
            }
            else
            {
                previewSource = permission.RawInput;
                previewLimit = 200;
            }

            canShowMore = !string.IsNullOrEmpty(previewSource) && previewSource.Length > previewLimit;
            if (string.IsNullOrEmpty(previewSource))
            {
                return string.Empty;
            }

            return _showingFullPreview || !canShowMore
                ? previewSource
                : previewSource.Substring(0, previewLimit) + "...";
        }

        private void UpdateStoreSummary()
        {
            if (_storeService == null)
            {
                return;
            }

            if (_currentIndex < 0 || _currentIndex >= _queue.Count)
            {
                _storeService.ClearPermissionQueueSummary();
                return;
            }

            var current = _queue[_currentIndex];
            var toolKind = ToolPresentationCatalog.GetEffectiveKind(current);
            var pathOrCommand = current.FilePath ?? current.Command ?? string.Empty;

            _storeService.SetPermissionQueueSummary(
                _queue.Count,
                current.ToolUseId,
                current.ToolName,
                toolKind,
                pathOrCommand,
                hasActiveRequest: true);
        }

        private void OnAllowRequested(bool remember) => AllowCommand.Execute(remember);
        private void OnDenyRequested(bool remember) => DenyCommand.Execute(remember);
        private void OnShowMoreRequested() => ShowMoreCommand.Execute(null);
        private void OnClosedRequested() => CloseCommand.Execute(null);
    }
}
