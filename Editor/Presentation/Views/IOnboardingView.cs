// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Presentation.Shell.Modals;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    internal interface IOnboardingView
    {
        event Action SkipRequested;

        event Action BackRequested;

        event Action NextRequested;

        event Action FinishRequested;

        event Action CliValidateRequested;

        event Action CliSettingsRequested;

        event Action CliHelpRequested;

        event Action AuthLoginRequested;

        event Action AuthCliLoginRequested;

        event Action AuthCheckStatusRequested;

        event Action AuthUrlRequested;

        event Action McpInstallRequested;

        event Action McpSetupRequested;

        event Action McpRefreshRequested;

        event Action McpHelpRequested;

        event Action<string> ProviderSelected;

        void Show();

        void Hide();

        void SetStep(int currentStep, int startStep, int lastStep);

        /// <summary>Show/hide the MCP onboarding step panel, its progress dot, and the Done-page MCP summary row.</summary>
        void SetMcpStepIncluded(bool included);

        void RenderProviders(IReadOnlyList<OnboardingProviderOption> providers);

        void SetProviderNextEnabled(bool enabled);

        void SetCliStatus(IndicatorState state, string text);

        void SetAuthStatus(IndicatorState state, string text, string loginButtonText = null);

        void SetAuthVariant(AuthOnboardingVariant variant, string description);

        void SetMcpPackageStatus(IndicatorState state, string text, bool showInstallButton);

        void SetMcpPackageInstallEnabled(bool enabled);

        void SetMcpServerStatus(IndicatorState state, string text, string setupButtonText, bool setupButtonEnabled);

        void SetSummaryIndicators(IndicatorState cliState, IndicatorState authState, IndicatorState mcpState);
    }

    internal enum AuthOnboardingVariant
    {
        OAuthBuiltIn,
        CliCommand,
        ExternalUrl
    }

    internal readonly struct OnboardingProviderOption
    {
        public OnboardingProviderOption(string id, string displayName, string description, bool selected)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            Selected = selected;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public bool Selected { get; }
    }

    internal sealed class OnboardingView : IOnboardingView
    {
        private const int McpStepIndex = 3; // must match OnboardingWizardPresenter.StepMcp

        private readonly SidekickModalLayer _layer;
        private readonly VisualElement _content;
        private readonly VisualElement[] _progressDots;
        private readonly VisualElement _stepProvider;
        private readonly VisualElement _providersContainer;
        private readonly VisualElement _stepCli;
        private readonly VisualElement _stepAuth;
        private readonly VisualElement _stepMcp;
        private readonly VisualElement _stepDone;
        private readonly VisualElement _cliIndicator;
        private readonly Label _cliText;
        private readonly VisualElement _authIndicator;
        private readonly Label _authText;
        private readonly Button _authLoginButton;
        private readonly VisualElement _authLoginContainer;
        private readonly VisualElement _authCliSection;
        private readonly Label _authCliDesc;
        private readonly VisualElement _authUrlSection;
        private readonly Label _authUrlDesc;
        private readonly VisualElement _mcpPackageIndicator;
        private readonly Label _mcpPackageText;
        private readonly Button _mcpInstallButton;
        private readonly VisualElement _mcpServerIndicator;
        private readonly Label _mcpServerText;
        private readonly Button _mcpSetupButton;
        private readonly VisualElement _summaryCliIndicator;
        private readonly VisualElement _summaryAuthIndicator;
        private readonly VisualElement _summaryMcpIndicator;
        private readonly Button _backButton;
        private readonly Button _nextButton;
        private readonly Button _finishButton;

        private SidekickModalHandle _handle;
        private bool _suppressDismissEvent;

        /// <summary>
        /// Constructs the view from a pre-instantiated onboarding content fragment. The fragment
        /// is kept off-tree until <see cref="Show"/> presents it via the <see cref="SidekickModalLayer"/>.
        /// </summary>
        public OnboardingView(SidekickModalLayer layer, VisualElement contentFragment)
        {
            _layer = layer;
            _content = contentFragment;

            if (_content == null)
            {
                return;
            }

            var skipButton = _content.Q<Button>("onboarding-skip-btn");
            _progressDots = new VisualElement[5];
            for (int i = 0; i < 5; i++)
            {
                _progressDots[i] = _content.Q<VisualElement>($"progress-dot-{i + 1}");
            }

            _stepProvider = _content.Q<VisualElement>("onboarding-step-provider");
            _providersContainer = _content.Q<VisualElement>("providers-container");
            _stepCli = _content.Q<VisualElement>("onboarding-step-cli");
            _stepAuth = _content.Q<VisualElement>("onboarding-step-auth");
            _stepMcp = _content.Q<VisualElement>("onboarding-step-mcp");
            _stepDone = _content.Q<VisualElement>("onboarding-step-done");

            _cliIndicator = _content.Q<VisualElement>("cli-status-indicator");
            _cliText = _content.Q<Label>("cli-status-text");
            var cliValidateButton = _content.Q<Button>("cli-validate-btn");
            var cliSettingsButton = _content.Q<Button>("cli-settings-btn");
            var cliHelpButton = _content.Q<Button>("cli-help-btn");

            _authIndicator = _content.Q<VisualElement>("auth-status-indicator");
            _authText = _content.Q<Label>("auth-status-text");
            _authLoginButton = _content.Q<Button>("auth-login-btn");
            _authLoginContainer = _authLoginButton?.parent;
            _authCliSection = _content.Q<VisualElement>("auth-cli-section");
            _authCliDesc = _content.Q<Label>("auth-cli-desc");
            var authCliButton = _content.Q<Button>("auth-cli-btn");
            var authCheckStatusButton = _content.Q<Button>("auth-check-status-btn");
            _authUrlSection = _content.Q<VisualElement>("auth-url-section");
            _authUrlDesc = _content.Q<Label>("auth-url-desc");
            var authUrlButton = _content.Q<Button>("auth-url-btn");

            _mcpPackageIndicator = _content.Q<VisualElement>("mcp-pkg-indicator");
            _mcpPackageText = _content.Q<Label>("mcp-pkg-text");
            _mcpInstallButton = _content.Q<Button>("mcp-install-btn");
            _mcpServerIndicator = _content.Q<VisualElement>("mcp-server-indicator");
            _mcpServerText = _content.Q<Label>("mcp-server-text");
            _mcpSetupButton = _content.Q<Button>("mcp-setup-btn");
            var mcpRefreshButton = _content.Q<Button>("mcp-refresh-btn");
            var mcpHelpButton = _content.Q<Button>("mcp-help-btn");

            _summaryCliIndicator = _content.Q<VisualElement>("summary-cli-indicator");
            _summaryAuthIndicator = _content.Q<VisualElement>("summary-auth-indicator");
            _summaryMcpIndicator = _content.Q<VisualElement>("summary-mcp-indicator");

            _backButton = _content.Q<Button>("onboarding-back-btn");
            _nextButton = _content.Q<Button>("onboarding-next-btn");
            _finishButton = _content.Q<Button>("onboarding-finish-btn");

            skipButton?.RegisterCallback<ClickEvent>(_ => SkipRequested?.Invoke());
            _backButton?.RegisterCallback<ClickEvent>(_ => BackRequested?.Invoke());
            _nextButton?.RegisterCallback<ClickEvent>(_ => NextRequested?.Invoke());
            _finishButton?.RegisterCallback<ClickEvent>(_ => FinishRequested?.Invoke());
            cliValidateButton?.RegisterCallback<ClickEvent>(_ => CliValidateRequested?.Invoke());
            cliSettingsButton?.RegisterCallback<ClickEvent>(_ => CliSettingsRequested?.Invoke());
            cliHelpButton?.RegisterCallback<ClickEvent>(_ => CliHelpRequested?.Invoke());
            _authLoginButton?.RegisterCallback<ClickEvent>(_ => AuthLoginRequested?.Invoke());
            authCliButton?.RegisterCallback<ClickEvent>(_ => AuthCliLoginRequested?.Invoke());
            authCheckStatusButton?.RegisterCallback<ClickEvent>(_ => AuthCheckStatusRequested?.Invoke());
            authUrlButton?.RegisterCallback<ClickEvent>(_ => AuthUrlRequested?.Invoke());
            _mcpInstallButton?.RegisterCallback<ClickEvent>(_ => McpInstallRequested?.Invoke());
            _mcpSetupButton?.RegisterCallback<ClickEvent>(_ => McpSetupRequested?.Invoke());
            mcpRefreshButton?.RegisterCallback<ClickEvent>(_ => McpRefreshRequested?.Invoke());
            mcpHelpButton?.RegisterCallback<ClickEvent>(_ => McpHelpRequested?.Invoke());
        }

        public event Action SkipRequested;

        public event Action BackRequested;

        public event Action NextRequested;

        public event Action FinishRequested;

        public event Action CliValidateRequested;

        public event Action CliSettingsRequested;

        public event Action CliHelpRequested;

        public event Action AuthLoginRequested;

        public event Action AuthCliLoginRequested;

        public event Action AuthCheckStatusRequested;

        public event Action AuthUrlRequested;

        public event Action McpInstallRequested;

        public event Action McpSetupRequested;

        public event Action McpRefreshRequested;

        public event Action McpHelpRequested;

        public event Action<string> ProviderSelected;

        /// <summary>
        /// Exposes the cached content fragment for tests that need to introspect the
        /// rendered DOM without standing up an App UI Panel or modal layer.
        /// </summary>
        internal VisualElement ContentForTests => _content;

        public void Show()
        {
            if (_handle != null && _handle.IsOpen)
            {
                return;
            }

            if (_layer == null || _content == null)
            {
                return;
            }

            // Onboarding is the forced first-run flow today: ESC and outside-click are
            // BOTH no-ops. Users dismiss via Skip / Finish, which the flow partial
            // routes through CompleteOnboarding → Hide().
            _handle = _layer.Show(_content, new SidekickModalOptions(false, false, "sk-modal-onboarding-content"));
            _handle.Dismissed += OnHandleDismissed;
        }

        public void Hide()
        {
            if (_handle == null || !_handle.IsOpen)
            {
                return;
            }

            _suppressDismissEvent = true;
            _handle.Dismiss(SidekickModalDismissType.Manual);
        }

        private void OnHandleDismissed(SidekickModalDismissType type)
        {
            _handle = null;

            if (_suppressDismissEvent)
            {
                _suppressDismissEvent = false;
                return;
            }

            // Onboarding has no ClosedRequested analog — Skip is explicit via the
            // Skip button (SkipRequested → CompleteOnboarding → Hide()) and we
            // explicitly disable outside-click/ESC above. Any unsuppressed dismiss
            // here is a defensive no-op.
        }

        public void SetStep(int currentStep, int startStep, int lastStep)
        {
            var steps = new[] { _stepProvider, _stepCli, _stepAuth, _stepMcp, _stepDone };
            foreach (var step in steps)
            {
                if (step != null)
                {
                    step.style.display = DisplayStyle.None;
                }
            }

            if (currentStep >= 0 && currentStep < steps.Length && steps[currentStep] != null)
            {
                steps[currentStep].style.display = DisplayStyle.Flex;
            }

            if (_progressDots != null)
            {
                for (int i = 0; i < _progressDots.Length; i++)
                {
                    var dot = _progressDots[i];
                    if (dot == null)
                    {
                        continue;
                    }

                    dot.RemoveFromClassList("active");
                    dot.RemoveFromClassList("completed");

                    if (i == currentStep)
                    {
                        dot.AddToClassList("active");
                    }
                    else if (i < currentStep)
                    {
                        dot.AddToClassList("completed");
                    }
                }
            }

            if (_backButton != null)
            {
                _backButton.style.display = currentStep > startStep ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_nextButton != null)
            {
                _nextButton.style.display = currentStep < lastStep ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_finishButton != null)
            {
                _finishButton.style.display = currentStep == lastStep ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void SetMcpStepIncluded(bool included)
        {
            var display = included ? DisplayStyle.Flex : DisplayStyle.None;

            // Step panel: when excluded, force-hide it (navigation never lands on it, and SetStep
            // also hides non-current panels). When included we intentionally do nothing here —
            // SetStep re-shows the panel when the MCP step is reached.
            if (!included && _stepMcp != null)
            {
                _stepMcp.style.display = DisplayStyle.None;
            }

            // Progress dot for the MCP step (1 dot per step; index matches step number).
            if (_progressDots != null && _progressDots.Length > McpStepIndex && _progressDots[McpStepIndex] != null)
            {
                _progressDots[McpStepIndex].style.display = display;
            }

            // Done-page summary row: the indicator + its "MCP for Unity" label share a parent container.
            if (_summaryMcpIndicator?.parent != null)
            {
                _summaryMcpIndicator.parent.style.display = display;
            }
        }

        public void RenderProviders(IReadOnlyList<OnboardingProviderOption> providers)
        {
            if (_providersContainer == null)
            {
                return;
            }

            _providersContainer.Clear();
            if (providers == null)
            {
                return;
            }

            foreach (var provider in providers)
            {
                var providerId = provider.Id;
                var card = new VisualElement
                {
                    name = $"provider-card-{providerId}"
                };
                card.AddToClassList("sk-provider-card");
                if (provider.Selected)
                {
                    card.AddToClassList("selected");
                }

                var nameLabel = new Label(provider.DisplayName ?? string.Empty);
                nameLabel.AddToClassList("sk-provider-card-name");
                card.Add(nameLabel);

                var descriptionLabel = new Label(provider.Description ?? string.Empty);
                descriptionLabel.AddToClassList("sk-provider-card-desc");
                card.Add(descriptionLabel);

                card.RegisterCallback<ClickEvent>(_ =>
                {
                    ProviderSelected?.Invoke(providerId);
                });
                _providersContainer.Add(card);
            }
        }

        public void SetProviderNextEnabled(bool enabled)
        {
            _nextButton?.SetEnabled(enabled);
        }

        public void SetCliStatus(IndicatorState state, string text)
        {
            ViewIndicatorStyler.Apply(_cliIndicator, state);
            if (_cliText != null)
            {
                _cliText.text = text ?? string.Empty;
            }
        }

        public void SetAuthStatus(IndicatorState state, string text, string loginButtonText = null)
        {
            ViewIndicatorStyler.Apply(_authIndicator, state);
            if (_authText != null)
            {
                _authText.text = text ?? string.Empty;
            }

            if (_authLoginButton != null && loginButtonText != null)
            {
                _authLoginButton.text = loginButtonText;
            }
        }

        public void SetAuthVariant(AuthOnboardingVariant variant, string description)
        {
            if (_authLoginContainer != null)
            {
                _authLoginContainer.style.display =
                    variant == AuthOnboardingVariant.OAuthBuiltIn ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_authCliSection != null)
            {
                _authCliSection.style.display =
                    variant == AuthOnboardingVariant.CliCommand ? DisplayStyle.Flex : DisplayStyle.None;

                if (variant == AuthOnboardingVariant.CliCommand && _authCliDesc != null)
                {
                    _authCliDesc.text = description ?? string.Empty;
                }
            }

            if (_authUrlSection != null)
            {
                _authUrlSection.style.display =
                    variant == AuthOnboardingVariant.ExternalUrl ? DisplayStyle.Flex : DisplayStyle.None;

                if (variant == AuthOnboardingVariant.ExternalUrl && _authUrlDesc != null)
                {
                    _authUrlDesc.text = description ?? string.Empty;
                }
            }
        }

        public void SetMcpPackageStatus(IndicatorState state, string text, bool showInstallButton)
        {
            ViewIndicatorStyler.Apply(_mcpPackageIndicator, state);
            if (_mcpPackageText != null)
            {
                _mcpPackageText.text = text ?? string.Empty;
            }

            if (_mcpInstallButton != null)
            {
                _mcpInstallButton.style.display = showInstallButton ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void SetMcpPackageInstallEnabled(bool enabled)
        {
            _mcpInstallButton?.SetEnabled(enabled);
        }

        public void SetMcpServerStatus(IndicatorState state, string text, string setupButtonText, bool setupButtonEnabled)
        {
            ViewIndicatorStyler.Apply(_mcpServerIndicator, state);
            if (_mcpServerText != null)
            {
                _mcpServerText.text = text ?? string.Empty;
            }

            if (_mcpSetupButton != null)
            {
                _mcpSetupButton.text = setupButtonText ?? _mcpSetupButton.text;
                _mcpSetupButton.SetEnabled(setupButtonEnabled);
            }
        }

        public void SetSummaryIndicators(IndicatorState cliState, IndicatorState authState, IndicatorState mcpState)
        {
            ViewIndicatorStyler.Apply(_summaryCliIndicator, cliState);
            ViewIndicatorStyler.Apply(_summaryAuthIndicator, authState);
            ViewIndicatorStyler.Apply(_summaryMcpIndicator, mcpState);
        }
    }
}
