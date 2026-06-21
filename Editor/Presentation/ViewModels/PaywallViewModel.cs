// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Pro;
using Ryx.Sidekick.Editor.Presentation.State;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Pro;
using Unity.AppUI.MVVM;

namespace Ryx.Sidekick.Editor.Presentation.ViewModels
{
    [ObservableObject]
    internal partial class PaywallViewModel : IDisposable
    {
        private readonly GetProOfferQuery _getOffer;
        private readonly IExternalUrlOpener _urlOpener;
        private readonly ResolveProAccessStateQuery _resolveAccess;
        private readonly IProInstaller _installer;
        private IPaywallView _view;
        private string _highlightFeatureId;
        private ProCtaDescriptor _currentCta;
        private bool _installInProgress;
        private string _installStatus;
        private bool _disposed;

        public PaywallViewModel(
            GetProOfferQuery getOffer,
            IExternalUrlOpener urlOpener,
            ResolveProAccessStateQuery resolveAccess = null,
            IProInstaller installer = null)
        {
            _getOffer = getOffer;
            _urlOpener = urlOpener;
            _resolveAccess = resolveAccess;
            _installer = installer;
        }

        public void BindView(IPaywallView view)
        {
            if (_view != null)
            {
                _view.PrimaryActionRequested -= OnPrimary;
                _view.InstallActionRequested -= OnInstall;
                _view.DismissRequested -= OnDismiss;
            }

            _view = view;
            if (_view == null) return;

            _view.PrimaryActionRequested += OnPrimary;
            _view.InstallActionRequested += OnInstall;
            _view.DismissRequested += OnDismiss;
        }

        public void Open(string highlightFeatureId = null)
        {
            _highlightFeatureId = highlightFeatureId;
            // Reset transient install state each time the paywall opens.
            _installInProgress = false;
            _installStatus = null;
            RenderFrom(_getOffer.Get());
            _ = RefreshAndRerender();
        }

        private async Task RefreshAndRerender()
        {
            await _getOffer.RefreshAsync();
            if (!_disposed) RenderFrom(_getOffer.Get());
        }

        private PaywallMode ResolveMode()
        {
            // Owner-without-package → install experience; everyone else → buy/upsell.
            return _resolveAccess != null && _resolveAccess.Resolve() == ProAccessState.OwnedNotInstalled
                ? PaywallMode.Install
                : PaywallMode.Buy;
        }

        private void RenderFrom(ProOfferManifest offer)
        {
            if (_view == null) return;

            var mode = ResolveMode();

            if (mode == PaywallMode.Install)
            {
                _view.Render(new PaywallViewState(
                    isVisible: true,
                    headline: "You own Sidekick Pro",
                    subhead: "Install it to unlock every provider and Pro feature.",
                    ctaLabel: "Install Pro",
                    ctaEnabled: !_installInProgress,
                    price: null,
                    requiresLiteVersion: offer?.RequiresLiteVersion,
                    features: BuildFeatureItems(offer),
                    mode: PaywallMode.Install,
                    installStatus: _installStatus,
                    installInProgress: _installInProgress));
                return;
            }

            if (offer == null)
            {
                _view.Render(PaywallViewState.Hidden);
                return;
            }

            _currentCta = offer.Cta;

            _view.Render(new PaywallViewState(
                isVisible: true,
                headline: offer.Headline,
                subhead: offer.Subhead,
                ctaLabel: offer.Cta?.Label,
                ctaEnabled: offer.Cta != null && !string.IsNullOrWhiteSpace(offer.Cta.Url),
                price: offer.Cta?.Price,
                requiresLiteVersion: offer.RequiresLiteVersion,
                features: BuildFeatureItems(offer)));
        }

        private List<PaywallFeatureItem> BuildFeatureItems(ProOfferManifest offer)
        {
            return (offer?.Features ?? new List<ProFeatureDescriptor>())
                .Select(f => new PaywallFeatureItem(
                    f.Id,
                    f.DisplayName,
                    f.ShortDescription,
                    f.Icon,
                    !string.IsNullOrEmpty(_highlightFeatureId) && string.Equals(f.Id, _highlightFeatureId, StringComparison.Ordinal)))
                .ToList();
        }

        [ICommand]
        private void PrimaryAction()
        {
            if (_currentCta != null && !string.IsNullOrWhiteSpace(_currentCta.Url))
                _urlOpener.Open(_currentCta.Url);
        }

        [ICommand]
        private void InstallPro()
        {
            if (_installer == null || _installInProgress) return;
            _ = RunInstallAsync();
        }

        private async Task RunInstallAsync()
        {
            _installInProgress = true;
            _installStatus = "Starting…";
            RenderCurrent();

            try
            {
                var result = await _installer.InstallLatestAsync(status =>
                {
                    if (_disposed) return;
                    _installStatus = status;
                    RenderCurrent();
                });

                if (_disposed) return;

                _installStatus = result.Message;
                // On a staged install the editor reloads to finish — keep the button disabled.
                // Otherwise (needs activation / window ended / failed) re-enable so the user can retry.
                _installInProgress = result.Outcome == InstallProOutcome.Staged;
            }
            catch (Exception ex)
            {
                if (_disposed) return;
                _installStatus = "Install failed: " + ex.Message;
                _installInProgress = false;
            }

            RenderCurrent();
        }

        private void RenderCurrent() => RenderFrom(_getOffer.Get());

        private void OnPrimary() => PrimaryActionCommand.Execute(null);
        private void OnInstall() => InstallProCommand.Execute(null);
        private void OnDismiss() => _view?.Render(PaywallViewState.Hidden);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_view != null)
            {
                _view.PrimaryActionRequested -= OnPrimary;
                _view.InstallActionRequested -= OnInstall;
                _view.DismissRequested -= OnDismiss;
                _view = null;
            }
        }
    }
}
