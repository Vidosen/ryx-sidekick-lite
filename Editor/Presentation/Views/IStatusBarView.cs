// SPDX-License-Identifier: GPL-3.0-only
using System;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    internal interface IStatusBarView
    {
        event Action McpRequested;

        void SetMcpStatus(
            IndicatorState state,
            string text,
            string buttonText,
            bool buttonVisible,
            bool buttonEnabled,
            string tooltip = null);

        void SetMcpSectionVisible(bool visible);

        void SetContextStatus(string text, IndicatorState state = IndicatorState.Neutral);

        void UpdateContextUsage(int usedTokens, int contextWindow);

        /// <summary>Fired when the user clicks the "Upgrade to Pro" chip.</summary>
        event Action ProUpgradeClicked
        {
            add { }
            remove { }
        }

        /// <summary>Shows or hides the Pro chip in the status bar.</summary>
        void SetProChipVisible(bool visible) { }

        /// <summary>Sets the Pro chip's label (e.g. "★ Upgrade to Pro" vs "Install Pro").</summary>
        void SetProChipLabel(string label) { }
    }

    internal sealed class StatusBarView : IStatusBarView
    {
        private readonly VisualElement _mcpSection;
        private readonly VisualElement _mcpIndicator;
        private readonly Label _mcpText;
        private readonly Unity.AppUI.UI.Button _mcpButton;
        private readonly VisualElement _contextIndicator;
        private readonly Label _contextText;
        private readonly VisualElement _contextUsage;
        private readonly VisualElement _contextUsagePie;
        private readonly Label _contextUsageText;
        private readonly Unity.AppUI.UI.Button _proUpgradeChip;

        public StatusBarView(
            VisualElement mcpIndicator,
            Label mcpText,
            Unity.AppUI.UI.Button mcpButton,
            VisualElement contextIndicator,
            Label contextText,
            VisualElement contextUsage,
            VisualElement contextUsagePie,
            Label contextUsageText,
            Unity.AppUI.UI.Button proUpgradeChip = null)
        {
            _mcpSection = mcpIndicator?.parent;
            _mcpIndicator = mcpIndicator;
            _mcpText = mcpText;
            _mcpButton = mcpButton;
            _contextIndicator = contextIndicator;
            _contextText = contextText;
            _contextUsage = contextUsage;
            _contextUsagePie = contextUsagePie;
            _contextUsageText = contextUsageText;
            _proUpgradeChip = proUpgradeChip;

            _mcpButton?.RegisterCallback<ClickEvent>(_ => McpRequested?.Invoke());
            _proUpgradeChip?.RegisterCallback<ClickEvent>(_ => ProUpgradeClicked?.Invoke());
        }

        public event Action McpRequested;

        public event Action ProUpgradeClicked;

        public void SetProChipVisible(bool visible)
        {
            if (_proUpgradeChip != null)
            {
                _proUpgradeChip.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void SetProChipLabel(string label)
        {
            if (_proUpgradeChip != null && !string.IsNullOrEmpty(label))
            {
                _proUpgradeChip.title = label;
            }
        }

        public void SetMcpStatus(
            IndicatorState state,
            string text,
            string buttonText,
            bool buttonVisible,
            bool buttonEnabled,
            string tooltip = null)
        {
            ViewIndicatorStyler.Apply(_mcpIndicator, state);
            if (_mcpText != null)
            {
                _mcpText.text = text ?? string.Empty;
                _mcpText.tooltip = tooltip;
            }

            if (_mcpButton != null)
            {
                _mcpButton.title = buttonText ?? string.Empty;
                _mcpButton.style.display = buttonVisible ? DisplayStyle.Flex : DisplayStyle.None;
                _mcpButton.SetEnabled(buttonEnabled);
            }
        }

        public void SetMcpSectionVisible(bool visible)
        {
            if (_mcpSection != null)
            {
                _mcpSection.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                _mcpSection.EnableInClassList("hidden", !visible);
            }
        }

        public void SetContextStatus(string text, IndicatorState state = IndicatorState.Neutral)
        {
            if (_contextText != null)
            {
                _contextText.text = text ?? string.Empty;
            }

            ViewIndicatorStyler.Apply(_contextIndicator, state);
        }

        public void UpdateContextUsage(int usedTokens, int contextWindow)
        {
            if (_contextUsage == null)
            {
                return;
            }

            if (contextWindow <= 0)
            {
                _contextUsage.style.display = DisplayStyle.None;
                return;
            }

            var usagePercent = (float)usedTokens / contextWindow * 100f;
            var remainingPercent = 100f - usagePercent;

            if (usagePercent < 50f)
            {
                _contextUsage.style.display = DisplayStyle.None;
                return;
            }

            _contextUsage.style.display = DisplayStyle.Flex;

            if (_contextUsageText != null)
            {
                _contextUsageText.text = $"{usagePercent:F0}% used";
            }

            if (_contextUsagePie != null)
            {
                if (remainingPercent < 25f)
                {
                    _contextUsagePie.AddToClassList("high-usage");
                }
                else
                {
                    _contextUsagePie.RemoveFromClassList("high-usage");
                }
            }

            _contextUsage.tooltip = $"{remainingPercent:F0}% of context remaining until auto-compact. Click to compact now.";
        }
    }
}
