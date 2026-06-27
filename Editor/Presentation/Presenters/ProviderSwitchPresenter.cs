// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.Shell.Modals;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using Ryx.Sidekick.Editor.Providers;
using UnityEditor;

namespace Ryx.Sidekick.Editor.Presentation.Presenters
{
    internal sealed class ProviderSwitchPresenter : IDisposable
    {
        private readonly SidekickModalLayer _layer;
        private readonly SidekickEditorAppHost _appHost;
        private readonly ProviderSelectorViewModel _providerSelectorViewModel;
        private OnboardingWizardPresenter _onboardingPresenter;
        private bool _disposed;

        public ProviderSwitchPresenter(
            SidekickModalLayer layer,
            SidekickEditorAppHost appHost,
            ProviderSelectorViewModel providerSelectorViewModel)
        {
            _layer = layer;
            _appHost = appHost;
            _providerSelectorViewModel = providerSelectorViewModel;

            if (_providerSelectorViewModel != null)
            {
                _providerSelectorViewModel.ProviderSwitchRequested += OnProviderSwitchRequested;
                _providerSelectorViewModel.InterruptRuntimeRequested += OnInterruptRuntimeRequested;
                _providerSelectorViewModel.RuntimePermissionModeChangeRequested += OnRuntimePermissionModeChangeRequested;
            }
        }

        public void SetOnboardingPresenter(OnboardingWizardPresenter onboardingPresenter)
        {
            _onboardingPresenter = onboardingPresenter;
        }

        public bool SwitchProviderFromOnboarding(string providerId)
        {
            return SwitchProvider(providerId, promptForProviderSetup: false);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_providerSelectorViewModel != null)
            {
                _providerSelectorViewModel.ProviderSwitchRequested -= OnProviderSwitchRequested;
                _providerSelectorViewModel.InterruptRuntimeRequested -= OnInterruptRuntimeRequested;
                _providerSelectorViewModel.RuntimePermissionModeChangeRequested -= OnRuntimePermissionModeChangeRequested;
            }

            _onboardingPresenter = null;
        }

        private void OnProviderSwitchRequested(string providerId)
        {
            SwitchProvider(providerId, promptForProviderSetup: true);
        }

        private async void OnInterruptRuntimeRequested()
        {
            await _appHost.InterruptRuntimeAsync();
        }

        private async void OnRuntimePermissionModeChangeRequested(string mode)
        {
            await _appHost.SetRuntimePermissionModeAsync(mode);
        }

        private bool SwitchProvider(string providerId, bool promptForProviderSetup)
        {
            var switched = _appHost?.SwitchProvider(providerId) ?? false;
            _providerSelectorViewModel?.CloseProviderPopupCommand.Execute(null);
            _providerSelectorViewModel?.CloseModelPopupCommand.Execute(null);

            if (!switched)
            {
                return false;
            }

            if (!promptForProviderSetup)
            {
                return true;
            }

            var key = SidekickAppConstants.EditorPrefsKeys.ProviderOnboardingCompleted(providerId);
            if (EditorPrefs.GetBool(key, false))
            {
                return true;
            }

            var provider = CliProviderRegistry.GetProvider(providerId);
            SidekickConfirmDialog.Show(
                _layer,
                title: "Setup Required",
                description: $"{provider.DisplayName} hasn't been set up yet.\nWould you like to run the setup wizard?",
                primaryActionText: "Set Up Now",
                cancelActionText: "Skip",
                onPrimary: () => _onboardingPresenter?.Show(startStep: 1));

            return true;
        }
    }
}
