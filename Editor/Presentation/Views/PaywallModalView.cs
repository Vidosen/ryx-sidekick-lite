// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Presentation.State;
using Unity.AppUI.Core;
using Unity.AppUI.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    /// <summary>
    /// App UI <see cref="Modal"/>-hosted paywall dialog. Renders two variants driven by
    /// <see cref="PaywallViewState.Mode"/>:
    /// <list type="bullet">
    /// <item><see cref="PaywallMode.Buy"/> — the upsell/purchase dialog (centred, outside-click dismiss).</item>
    /// <item><see cref="PaywallMode.Install"/> — a full-screen "you own Pro → install it" dialog with a
    ///   one-click install button and a live status line.</item>
    /// </list>
    /// The caller passes a <paramref name="referenceView"/> inside the active App UI panel —
    /// typically <c>SidekickWindowView.Root</c> — so the modal anchors to the correct panel.
    /// </summary>
    internal sealed class PaywallModalView : IPaywallView, IDisposable
    {
        private readonly VisualElement _referenceView;
        private Modal _modal;
        private bool _suppressDismissEvent;

        // In-place update handles for the Install variant — avoids rebuilding (and flashing) the
        // full-screen modal on every install-progress status tick.
        private PaywallMode _currentMode = PaywallMode.Buy;
        private Label _installStatusLabel;
        private Button _installButton;

        public PaywallModalView(VisualElement referenceView)
        {
            _referenceView = referenceView;
        }

        public event Action PrimaryActionRequested;
        public event Action InstallActionRequested;
        public event Action DismissRequested;

        /// <inheritdoc />
        public void Render(PaywallViewState state)
        {
            if (!state.IsVisible)
            {
                DismissModal();
                return;
            }

            // While an Install modal is already open, just refresh its status/button in place so the
            // full-screen surface doesn't flash on each progress update.
            if (_modal != null && _currentMode == PaywallMode.Install && state.Mode == PaywallMode.Install)
            {
                UpdateInstallStatus(state);
                return;
            }

            // Dismiss any existing modal before rebuilding so we never have two open at once.
            if (_modal != null)
            {
                DismissModal();
            }

            _currentMode = state.Mode;
            _installStatusLabel = null;
            _installButton = null;

            var content = state.Mode == PaywallMode.Install
                ? BuildInstallContent(state)
                : BuildBuyContent(state);

            // Both variants render as a compact centred modal over a dim scrim. (Install used to
            // be ModalFullScreenMode.FullScreen, but App UI's fullscreen theme rule force-stretches
            // .appui-modal__content to full width — which blew the card out and exposed App UI's
            // own modal-content background as a second backdrop.)
            _modal = Modal.Build(_referenceView, content)
                // Install carries its own close button and ignores outside-click so the user makes
                // a deliberate choice; the Buy dialog still dismisses on outside-click.
                .SetOutsideClickDismiss(state.Mode == PaywallMode.Buy)
                .SetKeyboardDismiss(true);

            // Tag App UI's .appui-modal__content wrapper (the content's parent after Build) so USS
            // can cap the modal width without cycling against the auto-sized wrapper.
            content.parent?.AddToClassList("sk-modal-paywall-content");

            _modal.dismissed += OnModalDismissed;
            _modal.Show();
        }

        // ── Buy variant (upsell) ──────────────────────────────────────────────────

        private VisualElement BuildBuyContent(PaywallViewState state)
        {
            var root = new VisualElement();
            root.AddToClassList("sk-paywall-modal");

            root.Add(BuildProPill());

            var headline = new Label(state.Headline ?? string.Empty);
            headline.AddToClassList("sk-paywall-headline");
            root.Add(headline);

            var oneTimePill = new Label("✓ One-time purchase · no subscription");
            oneTimePill.AddToClassList("sk-paywall-onetime-pill");
            root.Add(oneTimePill);

            if (!string.IsNullOrEmpty(state.Subhead))
            {
                var subhead = new Label(state.Subhead);
                subhead.AddToClassList("sk-paywall-subhead");
                root.Add(subhead);
            }

            root.Add(BuildFeatureGrid(state));

            var ctaLabel = !string.IsNullOrEmpty(state.Price)
                ? $"{state.CtaLabel} — {state.Price}"
                : state.CtaLabel ?? string.Empty;

            var cta = new Button(() => PrimaryActionRequested?.Invoke());
            cta.text = ctaLabel;
            cta.SetEnabled(state.CtaEnabled);
            cta.AddToClassList("sk-paywall-cta");
            root.Add(cta);

            if (!string.IsNullOrEmpty(state.RequiresLiteVersion))
            {
                var footer = new Label($"requires Lite {state.RequiresLiteVersion}+");
                footer.AddToClassList("sk-paywall-footer");
                root.Add(footer);
            }

            return root;
        }

        // ── Install variant (owned, one-click install) ────────────────────────────

        private VisualElement BuildInstallContent(PaywallViewState state)
        {
            // Full-screen container that fills the panel and centres the install card.
            var screen = new VisualElement();
            screen.AddToClassList("sk-paywall-install-screen");

            // Explicit close affordance, anchored to the card's top-right corner. Added to the
            // tree AFTER the card (below) so it paints above the card instead of behind it —
            // the card now fills the screen wrapper, so an earlier sibling would be occluded.
            var closeButton = new Button(() => DismissRequested?.Invoke()) { text = "✕" };
            closeButton.AddToClassList("sk-paywall-close");

            var card = new VisualElement();
            card.AddToClassList("sk-paywall-modal");
            card.AddToClassList("sk-paywall-modal--install");

            card.Add(BuildProPill());

            var headline = new Label(state.Headline ?? "You own Sidekick Pro");
            headline.AddToClassList("sk-paywall-headline");
            card.Add(headline);

            if (!string.IsNullOrEmpty(state.Subhead))
            {
                var subhead = new Label(state.Subhead);
                subhead.AddToClassList("sk-paywall-subhead");
                card.Add(subhead);
            }

            card.Add(BuildFeatureGrid(state));

            _installButton = new Button(() => InstallActionRequested?.Invoke())
            {
                text = string.IsNullOrEmpty(state.CtaLabel) ? "Install Pro" : state.CtaLabel
            };
            _installButton.AddToClassList("sk-paywall-cta");
            _installButton.SetEnabled(!state.InstallInProgress);
            card.Add(_installButton);

            _installStatusLabel = new Label(state.InstallStatus ?? string.Empty);
            _installStatusLabel.AddToClassList("sk-paywall-install-status");
            _installStatusLabel.style.display = string.IsNullOrEmpty(state.InstallStatus)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            card.Add(_installStatusLabel);

            screen.Add(card);
            screen.Add(closeButton);
            return screen;
        }

        private void UpdateInstallStatus(PaywallViewState state)
        {
            if (_installButton != null)
            {
                _installButton.SetEnabled(!state.InstallInProgress);
            }

            if (_installStatusLabel != null)
            {
                _installStatusLabel.text = state.InstallStatus ?? string.Empty;
                _installStatusLabel.style.display = string.IsNullOrEmpty(state.InstallStatus)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }
        }

        // ── Shared helpers ────────────────────────────────────────────────────────

        private static Label BuildProPill()
        {
            var proPill = new Label("RYX SIDEKICK PRO");
            proPill.AddToClassList("sk-paywall-pro-pill");
            return proPill;
        }

        private static VisualElement BuildFeatureGrid(PaywallViewState state)
        {
            var grid = new VisualElement();
            grid.AddToClassList("sk-paywall-grid");

            if (state.Features != null)
            {
                foreach (var item in state.Features)
                {
                    grid.Add(BuildFeatureCard(item));
                }
            }

            return grid;
        }

        private static VisualElement BuildFeatureCard(PaywallFeatureItem item)
        {
            var card = new VisualElement();
            card.AddToClassList("sk-paywall-feature");
            if (item.IsHighlighted)
            {
                card.AddToClassList("highlighted");
            }

            // Icon — resolved via SidekickIconCatalog; falls back to first letter of DisplayName.
            // TODO(T14): wire icon catalog when available for paywall-specific IconKey values
            //            (e.g. "pro-codex", "pro-cursor"). Until those keys are added to
            //            SidekickIconCatalog.IconCandidates the label will show the fallback glyph.
            var iconLabel = new Label();
            iconLabel.AddToClassList("sk-paywall-feature-icon");
            var fallbackGlyph = string.IsNullOrEmpty(item.DisplayName)
                ? "·"
                : item.DisplayName.Substring(0, 1).ToUpperInvariant();
            SidekickIconCatalog.ApplyToLabel(iconLabel, item.IconKey, fallbackGlyph, 16f);
            card.Add(iconLabel);

            var nameLabel = new Label(item.DisplayName ?? string.Empty);
            nameLabel.AddToClassList("sk-paywall-feature-name");
            card.Add(nameLabel);

            if (!string.IsNullOrEmpty(item.Description))
            {
                var descLabel = new Label(item.Description);
                descLabel.AddToClassList("sk-paywall-feature-desc");
                card.Add(descLabel);
            }

            return card;
        }

        private void DismissModal()
        {
            if (_modal == null)
            {
                return;
            }

            _installStatusLabel = null;
            _installButton = null;
            _suppressDismissEvent = true;
            _modal.Dismiss(DismissType.Manual);
        }

        private void OnModalDismissed(Modal modal, DismissType reason)
        {
            modal.dismissed -= OnModalDismissed;
            _modal = null;

            if (_suppressDismissEvent)
            {
                _suppressDismissEvent = false;
                return;
            }

            // User-driven dismiss (outside-click or ESC) — notify the ViewModel.
            _referenceView.schedule.Execute(() => DismissRequested?.Invoke());
        }

        /// <inheritdoc />
        public void Dispose()
        {
            DismissModal();
        }
    }
}
