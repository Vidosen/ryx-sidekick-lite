// SPDX-License-Identifier: GPL-3.0-only
using System;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Event channel that lets Infrastructure-layer Settings pages signal the Presentation layer
    /// to open modals (Paywall, Account) without creating an asmdef cycle.
    /// <para>
    /// Infrastructure raises <see cref="Activated"/>/<see cref="Deactivated"/> (both carry the page's
    /// root <see cref="VisualElement"/> so the bridge can tell which page is leaving) when a Settings
    /// page is mounted/unmounted. A Presentation-layer <c>[InitializeOnLoad]</c> bridge
    /// (<c>SidekickSettingsModalBridge</c>) subscribes and creates a local <c>SidekickModalLayer</c>
    /// + modal views over the Settings root element.
    /// </para>
    /// </summary>
    internal static class SidekickSettingsModalHost
    {
        private static Action _accountOpener;

        /// <summary>Raised by a Settings page on activation; payload = the page's root VisualElement.</summary>
        internal static event Action<VisualElement> Activated;

        /// <summary>Raised by a Settings page on deactivation; payload = the deactivating page's root.</summary>
        internal static event Action<VisualElement> Deactivated;

        /// <summary>Called by Settings pages when they activate (OnActivate).</summary>
        internal static void NotifyActivated(VisualElement rootElement) => Activated?.Invoke(rootElement);

        /// <summary>
        /// Called by Settings pages when they deactivate (DetachFromPanelEvent). The root is passed so
        /// the bridge ignores a stale deactivation from a page it has already superseded.
        /// </summary>
        internal static void NotifyDeactivated(VisualElement rootElement) => Deactivated?.Invoke(rootElement);

        /// <summary>
        /// Registers the opener the Presentation bridge uses to show the Account modal in-place.
        /// Returns a token that unregisters only this opener (identity-guarded), mirroring
        /// <c>ProPaywallLauncher.RegisterInPlaceHandler</c> — so disposing a stale token cannot
        /// clobber a newer registration.
        /// </summary>
        internal static IDisposable RegisterAccountOpener(Action opener)
        {
            _accountOpener = opener;
            return new OpenerRegistration(opener);
        }

        /// <summary>Invoked by a Settings page (e.g. the "Manage account" button) to open the Account modal.</summary>
        internal static void RequestAccountModal() => _accountOpener?.Invoke();

        private sealed class OpenerRegistration : IDisposable
        {
            private readonly Action _opener;

            internal OpenerRegistration(Action opener) => _opener = opener;

            public void Dispose()
            {
                if (_accountOpener == _opener)
                    _accountOpener = null;
            }
        }
    }
}
