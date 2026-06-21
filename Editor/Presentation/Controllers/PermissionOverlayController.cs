// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.State;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.UseCases.Permissions;

namespace Ryx.Sidekick.Editor.Presentation.Controllers
{
    /// <summary>
    /// Thin facade over <see cref="PermissionOverlayViewModel"/>.
    /// Handles a queue of pending permissions sequentially (1/N).
    /// </summary>
    internal sealed class PermissionOverlayController
    {
        /// <summary>
        /// Whether the overlay is currently visible.
        /// </summary>
        public bool IsActive => ViewModel.IsActive;

        internal PermissionOverlayViewModel ViewModel { get; }

        public PermissionOverlayController(
            PermissionService service,
            SidekickStoreService storeService = null,
            ResolvePermissionUseCase resolvePermissionUseCase = null)
        {
            ViewModel = new PermissionOverlayViewModel(
                service,
                resolvePermissionUseCase,
                storeService);
        }

        /// <summary>
        /// Re-wires the <see cref="ComposerViewModel"/> reference when the provider scope
        /// changes. Delegates to <see cref="PermissionOverlayViewModel.SetComposerViewModel"/>.
        /// </summary>
        public void SetComposerViewModel(ComposerViewModel composerVm) =>
            ViewModel.SetComposerViewModel(composerVm);

        public void BindView(IPermissionOverlayView view) => ViewModel.BindView(view);

        /// <summary>
        /// Enqueue a permission request. Shows the overlay if not already visible.
        /// </summary>
        public void Enqueue(PendingPermission permission) => ViewModel.Enqueue(permission);

        /// <summary>
        /// Called when stream completes or session resets.
        /// Clears any remaining queue without sending responses (they're stale).
        /// </summary>
        public void Reset() => ViewModel.Reset();
    }
}
