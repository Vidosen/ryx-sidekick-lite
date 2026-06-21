// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Domain.Auth;
using Ryx.Sidekick.Editor.Infrastructure.Mcp;
using Ryx.Sidekick.Editor.Infrastructure.Platform;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.Providers;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;

namespace Ryx.Sidekick.Editor.Presentation.Presenters
{
    /// <summary>
    /// Owns the onboarding wizard lifecycle: step machine, CLI/auth/MCP validation, and
    /// view event wiring. Extracted from <c>SidekickWindow.Onboarding.cs</c> during
    /// APPUI-T11-02g.
    /// </summary>
    /// <remarks>
    /// Window-scoped: constructed once per bound window view and disposed with the
    /// root window presenter. Provider switching is delegated to
    /// <see cref="ProviderSwitchPresenter"/> so onboarding does not depend on the
    /// concrete EditorWindow shell.
    /// </remarks>
    internal sealed class OnboardingWizardPresenter : IDisposable
    {
        private const string McpForUnityPackageId = "com.coplaydev.unity-mcp";
        private const string McpForUnityGitUrl = "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity";
        private const string McpForUnityReadmeUrl = "https://github.com/CoplayDev/unity-mcp";

        private const int StepProvider = 0;
        private const int StepCli = 1;
        private const int StepAuth = 2;
        private const int StepMcp = 3;
        private const int StepDone = 4;
        private const int LastStep = StepDone;

        // Temporarily disables the onboarding MCP step for all providers (B2). Set true to restore the
        // original 5-step flow. See Documentation~/McpRework/02-onboarding-mcp-step-off.md.
        private const bool IncludeMcpStep = false;

        private static string OnboardingCompletedVersionKey =>
            SidekickAppConstants.EditorPrefsKeys.OnboardingCompletedVersion;

        private readonly IOnboardingView _view;
        private readonly IAuthService _authService;
        private readonly AuthController _authController;
        private readonly McpForUnityController _mcpController;
        private readonly Func<string, bool> _switchProvider;

        private int _onboardingStep;
        private int _onboardingStartStep;
        private string _selectedOnboardingProvider;
        private bool _onboardingHandlersRegistered;
        private bool _cliValid;
        private bool _authValid;
        private bool _mcpPackageInstalled;
        private bool _mcpPackageInstalling;
        private bool _mcpServerReachable;
        private bool _mcpServerChecking;
        private ListRequest _packageListRequest;
        private AddRequest _packageAddRequest;
        private UnityWebRequest _mcpProbeRequest;
        private bool _disposed;

        public OnboardingWizardPresenter(
            IOnboardingView view,
            IAuthService authService,
            AuthController authController,
            McpForUnityController mcpController,
            Func<string, bool> switchProvider)
        {
            _view = view;
            _authService = authService;
            _authController = authController;
            _mcpController = mcpController;
            _switchProvider = switchProvider ?? ((_) => false);
        }

        /// <summary>
        /// Shows the onboarding wizard if the persisted "completed version" pref does not
        /// match the current package version. Called once per window after view binding.
        /// </summary>
        public void InitializeIfNeeded()
        {
            var completedVersion = EditorPrefs.GetString(OnboardingCompletedVersionKey, "");
            var currentVersion = GetPackageVersion();

            if (string.IsNullOrEmpty(completedVersion) || completedVersion != currentVersion)
            {
                Show();
            }
        }

        public void Show(int startStep = 0)
        {
            _onboardingStartStep = startStep;
            _onboardingStep = startStep;

            if (startStep == StepProvider)
            {
                _selectedOnboardingProvider = SidekickSettings.instance.ActiveProvider?.Id;
            }

            SetupOnboardingEventHandlers();
            _view?.Show();
            // Safe after Show(): the MCP element refs are resolved at view construction, not Modal attach.
            // When IncludeMcpStep is true this is effectively a no-op (SetStep manages the panel).
            _view?.SetMcpStepIncluded(IncludeMcpStep);
            if (!IncludeMcpStep && _onboardingStep == StepMcp)
            {
                _onboardingStep = OnboardingStepNavigator.NextVisibleStep(_onboardingStep, +1, IncludeMcpStep, StepMcp);
            }
            SetupProviderSelectionStep();
            UpdateOnboardingStep();

            if (_authService != null)
            {
                _authService.OnAuthStatusChanged += OnAuthStatusChangedForOnboarding;
            }

            if (_mcpController != null)
            {
                _mcpController.OnStatusChanged += OnMcpStatusChangedForOnboarding;
                UpdateMcpSetupButtonForOnboarding(_mcpController.Status);
            }

            ValidateCliForOnboarding();
            UpdateAuthStatusForOnboarding();
            CheckMcpPackageInstalled();
        }

        public void Hide()
        {
            UnsubscribeRuntimeSources();
            _view?.Hide();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            UnsubscribeRuntimeSources();

            if (_onboardingHandlersRegistered && _view != null)
            {
                _view.SkipRequested -= OnOnboardingSkipRequested;
                _view.BackRequested -= OnOnboardingBackRequested;
                _view.NextRequested -= OnOnboardingNextRequested;
                _view.FinishRequested -= OnOnboardingFinishRequested;
                _view.CliValidateRequested -= OnCliValidateRequested;
                _view.CliSettingsRequested -= OnCliSettingsRequested;
                _view.CliHelpRequested -= OnCliHelpRequested;
                _view.AuthLoginRequested -= OnAuthLoginRequested;
                _view.AuthCliLoginRequested -= OnAuthCliLoginRequested;
                _view.AuthCheckStatusRequested -= OnAuthCheckStatusRequested;
                _view.AuthUrlRequested -= OnAuthUrlRequested;
                _view.McpInstallRequested -= OnMcpInstallRequested;
                _view.McpSetupRequested -= OnMcpSetupRequested;
                _view.McpRefreshRequested -= OnMcpRefreshRequested;
                _view.McpHelpRequested -= OnMcpHelpRequested;
                _view.ProviderSelected -= OnOnboardingProviderSelected;
                _onboardingHandlersRegistered = false;
            }

            if (_mcpProbeRequest != null)
            {
                _mcpProbeRequest.Dispose();
                _mcpProbeRequest = null;
            }
        }

        private void UnsubscribeRuntimeSources()
        {
            if (_authService != null)
            {
                _authService.OnAuthStatusChanged -= OnAuthStatusChangedForOnboarding;
            }

            if (_mcpController != null)
            {
                _mcpController.OnStatusChanged -= OnMcpStatusChangedForOnboarding;
            }
        }

        private void CompleteOnboarding()
        {
            var currentVersion = GetPackageVersion();
            EditorPrefs.SetString(OnboardingCompletedVersionKey, currentVersion);

            var providerId = SidekickSettings.instance.ActiveProvider?.Id;
            if (!string.IsNullOrEmpty(providerId))
            {
                EditorPrefs.SetBool(
                    SidekickAppConstants.EditorPrefsKeys.ProviderOnboardingCompleted(providerId), true);
            }

            Hide();
        }

        private void SetupOnboardingEventHandlers()
        {
            // Handlers stay subscribed for the OnboardingView lifetime; re-show is idempotent.
            if (_onboardingHandlersRegistered) return;
            if (_view == null) return;
            _onboardingHandlersRegistered = true;

            _view.SkipRequested += OnOnboardingSkipRequested;
            _view.BackRequested += OnOnboardingBackRequested;
            _view.NextRequested += OnOnboardingNextRequested;
            _view.FinishRequested += OnOnboardingFinishRequested;
            _view.CliValidateRequested += OnCliValidateRequested;
            _view.CliSettingsRequested += OnCliSettingsRequested;
            _view.CliHelpRequested += OnCliHelpRequested;
            _view.AuthLoginRequested += OnAuthLoginRequested;
            _view.AuthCliLoginRequested += OnAuthCliLoginRequested;
            _view.AuthCheckStatusRequested += OnAuthCheckStatusRequested;
            _view.AuthUrlRequested += OnAuthUrlRequested;
            _view.McpInstallRequested += OnMcpInstallRequested;
            _view.McpSetupRequested += OnMcpSetupRequested;
            _view.McpRefreshRequested += OnMcpRefreshRequested;
            _view.McpHelpRequested += OnMcpHelpRequested;
            _view.ProviderSelected += OnOnboardingProviderSelected;
        }

        private void OnOnboardingSkipRequested() => CompleteOnboarding();

        private void OnOnboardingBackRequested()
        {
            if (_onboardingStep > _onboardingStartStep)
            {
                _onboardingStep = OnboardingStepNavigator.NextVisibleStep(_onboardingStep, -1, IncludeMcpStep, StepMcp);
                UpdateOnboardingStep();
            }
        }

        private void OnOnboardingNextRequested()
        {
            if (_onboardingStep < LastStep)
            {
                _onboardingStep = OnboardingStepNavigator.NextVisibleStep(_onboardingStep, +1, IncludeMcpStep, StepMcp);
                UpdateOnboardingStep();

                if (_onboardingStep == StepCli)
                    ValidateCliForOnboarding();
                if (_onboardingStep == StepAuth)
                    UpdateAuthStatusForOnboarding();
            }
        }

        private void OnOnboardingFinishRequested() => CompleteOnboarding();

        private void OnCliValidateRequested() => ValidateCliForOnboarding();

        private void OnCliSettingsRequested() => SettingsService.OpenProjectSettings("Project/Sidekick");

        private void OnCliHelpRequested()
        {
            var installUrl = SidekickSettings.instance.ActiveProvider?.InstallUrl
                ?? "https://docs.anthropic.com/en/docs/claude-code/getting-started";
            Application.OpenURL(installUrl);
        }

        private void OnAuthLoginRequested() => _authController?.HandleAuthButtonClick();

        private void OnAuthCliLoginRequested() => RunCliLogin();

        private void OnAuthCheckStatusRequested() => CheckCliAuthStatus();

        private static void OnAuthUrlRequested() => OpenAuthUrl();

        private void OnMcpInstallRequested() => InstallMcpPackage();

        private void OnMcpSetupRequested() => HandleMcpSetupButtonClick();

        private void OnMcpRefreshRequested()
        {
            CheckMcpPackageInstalled();
            ProbeMcpEndpoint();
        }

        private static void OnMcpHelpRequested() => Application.OpenURL(McpForUnityReadmeUrl);

        private void OnOnboardingProviderSelected(string providerId)
        {
            if (!_switchProvider(providerId))
            {
                return;
            }

            _selectedOnboardingProvider = providerId;
            RenderOnboardingProviderCards();
            _view?.SetProviderNextEnabled(true);
        }

        #region Provider Selection Step

        private void SetupProviderSelectionStep()
        {
            RenderOnboardingProviderCards();
            _view?.SetProviderNextEnabled(!string.IsNullOrEmpty(_selectedOnboardingProvider));
        }

        private void RenderOnboardingProviderCards()
        {
            if (_view == null) return;

            var options = new List<OnboardingProviderOption>();
            foreach (var provider in CliProviderRegistry.AllProviders)
            {
                var info = provider.GetOnboardingInfo();
                options.Add(new OnboardingProviderOption(
                    provider.Id,
                    provider.DisplayName,
                    info?.ProviderDescription ?? string.Empty,
                    provider.Id == _selectedOnboardingProvider));
            }

            _view.RenderProviders(options);
        }

        #endregion

        private void UpdateOnboardingStep()
        {
            _view?.SetStep(_onboardingStep, _onboardingStartStep, LastStep);

            if (_onboardingStep == StepProvider)
            {
                _view?.SetProviderNextEnabled(!string.IsNullOrEmpty(_selectedOnboardingProvider));
            }
            else
            {
                _view?.SetProviderNextEnabled(true);
            }

            // Auth step: toggle variant sections by AuthOnboardingKind
            if (_onboardingStep == StepAuth)
            {
                ConfigureAuthStepVariant();
            }

            // Summary indicators on final step
            if (_onboardingStep == LastStep)
            {
                UpdateSummaryIndicators();
            }
        }

        private void ConfigureAuthStepVariant()
        {
            var info = SidekickSettings.instance.ActiveProvider?.GetOnboardingInfo();
            var kind = info?.AuthKind ?? AuthOnboardingKind.OAuthBuiltIn;

            var variant = kind switch
            {
                AuthOnboardingKind.CliCommand => AuthOnboardingVariant.CliCommand,
                AuthOnboardingKind.ExternalUrl => AuthOnboardingVariant.ExternalUrl,
                _ => AuthOnboardingVariant.OAuthBuiltIn
            };

            _view?.SetAuthVariant(variant, info?.AuthDescription ?? string.Empty);

            if (kind == AuthOnboardingKind.CliCommand)
                CheckCliAuthStatus();
            else if (kind == AuthOnboardingKind.OAuthBuiltIn)
                UpdateAuthStatusForOnboarding();
        }

        #region CLI Auth (CliCommand variant)

        private void RunCliLogin()
        {
            var info = SidekickSettings.instance.ActiveProvider?.GetOnboardingInfo();
            if (string.IsNullOrEmpty(info?.AuthLoginArg)) return;

            var cliPath = SidekickSettings.instance.GetResolvedCliPath();
            if (string.IsNullOrEmpty(cliPath))
            {
                _view?.SetAuthStatus(IndicatorState.Error, "CLI path not configured");
                return;
            }

            try
            {
                var platform = ClaudePlatformFactory.GetPlatform();
                var psi = platform.CreateDebugProcessStartInfo(cliPath, info.AuthLoginArg, SidekickSettings.instance.WorkingDirectory);
                Process.Start(psi);

                _view?.SetAuthStatus(
                    IndicatorState.Warning,
                    "Login launched in terminal — click Check Status when done");
            }
            catch (Exception ex)
            {
                _view?.SetAuthStatus(IndicatorState.Error, $"Failed to launch: {ex.Message}");
            }
        }

        private void CheckCliAuthStatus()
        {
            var info = SidekickSettings.instance.ActiveProvider?.GetOnboardingInfo();
            if (string.IsNullOrEmpty(info?.AuthStatusArgs) || info.IsAuthenticatedFromOutput == null)
            {
                _view?.SetAuthStatus(IndicatorState.Warning, "Status check not available");
                return;
            }

            _view?.SetAuthStatus(IndicatorState.Checking, "Checking...");

            var cliPath = SidekickSettings.instance.GetResolvedCliPath();
            if (string.IsNullOrEmpty(cliPath))
            {
                _view?.SetAuthStatus(IndicatorState.Error, "CLI path not configured");
                return;
            }

            try
            {
                var platform = ClaudePlatformFactory.GetPlatform();
                var psi = platform.CreateProcessStartInfo(cliPath, info.AuthStatusArgs, SidekickSettings.instance.WorkingDirectory);

                using var process = Process.Start(psi);
                if (process == null)
                {
                    _view?.SetAuthStatus(IndicatorState.Error, "Failed to start status check");
                    return;
                }

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);

                var combinedOutput = stdout + stderr;
                var authenticated = info.IsAuthenticatedFromOutput(combinedOutput);
                _authValid = authenticated;

                _view?.SetAuthStatus(
                    authenticated ? IndicatorState.Success : IndicatorState.Warning,
                    authenticated ? "Logged in" : "Not logged in");
            }
            catch (Exception ex)
            {
                _view?.SetAuthStatus(IndicatorState.Error, $"Check failed: {ex.Message}");
            }
        }

        private static void OpenAuthUrl()
        {
            var info = SidekickSettings.instance.ActiveProvider?.GetOnboardingInfo();
            if (!string.IsNullOrEmpty(info?.AuthUrl))
                Application.OpenURL(info.AuthUrl);
        }

        #endregion

        #region CLI Validation

        private void ValidateCliForOnboarding()
        {
            _view?.SetCliStatus(IndicatorState.Checking, "Checking...");

            var (success, message) = SidekickSettings.instance.ValidateCli();
            _cliValid = success;

            _view?.SetCliStatus(
                success ? IndicatorState.Success : IndicatorState.Error,
                message);
        }

        #endregion

        #region Auth Status (OAuthBuiltIn variant)

        private void OnAuthStatusChangedForOnboarding(AuthStatus status)
        {
            EditorApplication.delayCall += UpdateAuthStatusForOnboarding;
        }

        private void UpdateAuthStatusForOnboarding()
        {
            var authKind = SidekickSettings.instance.ActiveProvider?.GetOnboardingInfo()?.AuthKind
                ?? AuthOnboardingKind.OAuthBuiltIn;

            if (authKind != AuthOnboardingKind.OAuthBuiltIn) return;

            var status = _authService?.GetAuthStatus();
            var isAuthenticated = status?.IsAuthenticated ?? false;
            _authValid = isAuthenticated;

            _view?.SetAuthStatus(
                isAuthenticated ? IndicatorState.Success : IndicatorState.Warning,
                isAuthenticated ? "Logged in" : "Not logged in",
                isAuthenticated ? "Switch Account" : "Login");
        }

        #endregion

        #region MCP Package Management

        private void CheckMcpPackageInstalled()
        {
            _view?.SetMcpPackageStatus(IndicatorState.Checking, "Checking...", showInstallButton: false);

            _packageListRequest = Client.List(offlineMode: true);
            EditorApplication.update += OnPackageListProgress;
        }

        private void OnPackageListProgress()
        {
            if (_packageListRequest is not { IsCompleted: true })
                return;

            EditorApplication.update -= OnPackageListProgress;

            if (_packageListRequest.Status == StatusCode.Success)
            {
                _mcpPackageInstalled = false;
                foreach (var package in _packageListRequest.Result)
                {
                    if (package.name == McpForUnityPackageId)
                    {
                        _mcpPackageInstalled = true;
                        break;
                    }
                }

                _view?.SetMcpPackageStatus(
                    _mcpPackageInstalled ? IndicatorState.Success : IndicatorState.Warning,
                    _mcpPackageInstalled ? "Installed" : "Not installed",
                    showInstallButton: !_mcpPackageInstalled);

                if (_mcpPackageInstalled)
                {
                    ProbeMcpEndpoint();
                }
            }
            else
            {
                _view?.SetMcpPackageStatus(IndicatorState.Error, "Failed to check", showInstallButton: false);
            }

            _packageListRequest = null;
        }

        private void InstallMcpPackage()
        {
            if (_mcpPackageInstalling)
                return;

            _mcpPackageInstalling = true;
            _view?.SetMcpPackageStatus(IndicatorState.Checking, "Installing...", showInstallButton: true);
            _view?.SetMcpPackageInstallEnabled(false);

            _packageAddRequest = Client.Add(McpForUnityGitUrl);
            EditorApplication.update += OnPackageAddProgress;
        }

        private void OnPackageAddProgress()
        {
            if (_packageAddRequest is not { IsCompleted: true })
                return;

            EditorApplication.update -= OnPackageAddProgress;
            _mcpPackageInstalling = false;

            _view?.SetMcpPackageInstallEnabled(true);

            if (_packageAddRequest.Status == StatusCode.Success)
            {
                _mcpPackageInstalled = true;
                _view?.SetMcpPackageStatus(IndicatorState.Success, "Installed", showInstallButton: false);

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                ProbeMcpEndpoint();
            }
            else
            {
                _view?.SetMcpPackageStatus(
                    IndicatorState.Error,
                    $"Install failed: {_packageAddRequest.Error?.message ?? "Unknown error"}",
                    showInstallButton: true);
            }

            _packageAddRequest = null;
        }

        private void HandleMcpSetupButtonClick()
        {
            if (_mcpController == null) return;

            if (_mcpController.Status == McpForUnityController.McpStatus.Connected)
            {
                _mcpController.Stop();
            }
            else
            {
                _mcpController.StartServerAndConnect();
            }
        }

        private void OnMcpStatusChangedForOnboarding(McpForUnityController.McpStatus status)
        {
            EditorApplication.delayCall += () => UpdateMcpSetupButtonForOnboarding(status);
        }

        private void UpdateMcpSetupButtonForOnboarding(McpForUnityController.McpStatus status)
        {
            _mcpServerReachable = status == McpForUnityController.McpStatus.Connected;

            var isTransitioning = status == McpForUnityController.McpStatus.Starting
                                  || status == McpForUnityController.McpStatus.Stopping;

            var setupButtonText = status switch
            {
                McpForUnityController.McpStatus.Connected => "Stop MCP Server",
                McpForUnityController.McpStatus.Starting => "Starting...",
                McpForUnityController.McpStatus.Stopping => "Stopping...",
                _ => "Start MCP Server"
            };

            var indicatorState = status switch
            {
                McpForUnityController.McpStatus.Connected => IndicatorState.Success,
                McpForUnityController.McpStatus.Starting => IndicatorState.Checking,
                McpForUnityController.McpStatus.Stopping => IndicatorState.Checking,
                McpForUnityController.McpStatus.Error => IndicatorState.Error,
                _ => IndicatorState.Warning
            };

            var serverText = status switch
            {
                McpForUnityController.McpStatus.Connected => "Connected",
                McpForUnityController.McpStatus.Starting => "Starting...",
                McpForUnityController.McpStatus.Stopping => "Stopping...",
                McpForUnityController.McpStatus.Error => "Error",
                McpForUnityController.McpStatus.Off => "Server not running",
                _ => "Checking..."
            };

            _view?.SetMcpServerStatus(indicatorState, serverText, setupButtonText, !isTransitioning);
        }

        #endregion

        #region MCP Endpoint Probing

        private void ProbeMcpEndpoint()
        {
            if (_mcpServerChecking)
                return;

            _mcpServerChecking = true;
            _view?.SetMcpServerStatus(IndicatorState.Checking, "Checking...", setupButtonText: null, setupButtonEnabled: true);

            var url = SidekickSettings.instance.McpServerUrl;
            if (string.IsNullOrEmpty(url))
            {
                url = "http://localhost:8080/mcp";
            }

            _mcpProbeRequest = UnityWebRequest.Get(url);
            _mcpProbeRequest.timeout = 5;

            var operation = _mcpProbeRequest.SendWebRequest();
            operation.completed += OnMcpProbeComplete;
        }

        private void OnMcpProbeComplete(AsyncOperation operation)
        {
            _mcpServerChecking = false;

            if (_mcpProbeRequest == null)
                return;

            var isReachable = _mcpProbeRequest.result != UnityWebRequest.Result.ConnectionError;
            _mcpServerReachable = isReachable;

            _view?.SetMcpServerStatus(
                isReachable ? IndicatorState.Success : IndicatorState.Warning,
                isReachable ? "Server reachable" : "Server not running",
                setupButtonText: null,
                setupButtonEnabled: true);

            _mcpProbeRequest.Dispose();
            _mcpProbeRequest = null;
        }

        #endregion

        #region Summary

        private void UpdateSummaryIndicators()
        {
            var cliState = _cliValid ? IndicatorState.Success : IndicatorState.Error;
            var authState = _authValid ? IndicatorState.Success : IndicatorState.Warning;
            var mcpState = _mcpPackageInstalled && _mcpServerReachable ? IndicatorState.Success :
                           _mcpPackageInstalled ? IndicatorState.Warning : IndicatorState.Error;

            _view?.SetSummaryIndicators(cliState, authState, mcpState);
        }

        #endregion

        #region Helpers

        private static string GetPackageVersion()
        {
            var packageJsonPath = SidekickUiConstants.PackageJsonPath;
            try
            {
                var json = System.IO.File.ReadAllText(packageJsonPath);
                var match = System.Text.RegularExpressions.Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            catch
            {
                // Ignore
            }

            return "unknown";
        }

        #endregion
    }
}
