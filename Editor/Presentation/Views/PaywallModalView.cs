// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Ryx.Sidekick.Editor.Presentation.Renderers;
using Ryx.Sidekick.Editor.Presentation.State;
using Unity.AppUI.Core;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    /// <summary>
    /// App UI <see cref="Modal"/>-hosted paywall dialog (refined "F" design). Renders two variants
    /// driven by <see cref="PaywallViewState.Mode"/>:
    /// <list type="bullet">
    /// <item><see cref="PaywallMode.Buy"/> — the upsell/purchase dialog (centred, outside-click dismiss).</item>
    /// <item><see cref="PaywallMode.Install"/> — a "you own Pro → install it" dialog with a one-click
    ///   install button, a live status line and an explicit close button.</item>
    /// </list>
    /// Features are grouped by <see cref="PaywallFeatureItem.IsProvider"/>: provider features collapse
    /// into a single wide "engines" hero (with one chip each), and the remaining features render as a
    /// two-column card grid below. The caller passes a <paramref name="referenceView"/> inside the
    /// active App UI panel so the modal anchors to the correct panel.
    /// </summary>
    internal sealed class PaywallModalView : IPaywallView, IDisposable
    {
        // Engines-hero copy is presentation marketing copy (the provider chips below are data-driven).
        private const string EnginesTitle = "A growing roster of engines";
        private const string EnginesDescription =
            "Run Cursor and OpenAI Codex today — new providers land in every update.";
        private const string MoreChipLabel = "more each update";
        private const string PriceCaption =
            "one-time · includes a year of updates & support · no subscription";

        // Palette (mirrors the design's per-feature accents).
        private static readonly Color Gold = new Color(0xE9 / 255f, 0xC4 / 255f, 0x6A / 255f);
        private static readonly Color Blue = new Color(0x4A / 255f, 0x9E / 255f, 0xFF / 255f);
        private static readonly Color Teal = new Color(0x4E / 255f, 0xC9 / 255f, 0xB0 / 255f);
        private static readonly Color Green = new Color(0x89 / 255f, 0xD1 / 255f, 0x85 / 255f);
        private static readonly Color Dark = new Color(0x18 / 255f, 0x18 / 255f, 0x18 / 255f);
        private static readonly Color Muted = new Color(0x8A / 255f, 0x85 / 255f, 0x7C / 255f);

        private readonly VisualElement _referenceView;
        private Modal _modal;
        private bool _suppressDismissEvent;

        // In-place update handles for the Install variant — avoids rebuilding (and flashing) the
        // modal on every install-progress status tick.
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
            // surface doesn't flash on each progress update.
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
            var card = NewCard();

            card.Add(BuildProPill());
            card.Add(Headline(state.Headline));
            if (!string.IsNullOrEmpty(state.Subhead))
            {
                card.Add(Subhead(state.Subhead));
            }

            card.Add(BuildFeatureSection(state));

            if (!string.IsNullOrEmpty(state.Price))
            {
                card.Add(BuildPriceBlock(state.Price));
            }

            card.Add(BuildCta(
                string.IsNullOrEmpty(state.CtaLabel) ? "Get Sidekick Pro" : state.CtaLabel,
                state.CtaEnabled,
                () => PrimaryActionRequested?.Invoke(),
                withArrow: true));

            if (!string.IsNullOrEmpty(state.RequiresLiteVersion))
            {
                var footer = new Label($"Requires Sidekick Lite {state.RequiresLiteVersion}+ · on the Unity Asset Store");
                footer.AddToClassList("sk-paywall-footer");
                card.Add(footer);
            }

            return card;
        }

        // ── Install variant (owned, one-click install) ────────────────────────────

        private VisualElement BuildInstallContent(PaywallViewState state)
        {
            // Wrapper that fills the panel; the card and close button position within it.
            var screen = new VisualElement();
            screen.AddToClassList("sk-paywall-install-screen");

            // Explicit close affordance, anchored to the card's top-right. Added to the tree AFTER
            // the card (below) so it paints above instead of behind it.
            var closeButton = new Button(() => DismissRequested?.Invoke()) { text = "✕" };
            closeButton.AddToClassList("sk-paywall-close");

            var card = NewCard();
            card.AddToClassList("sk-paywall-modal--install");

            card.Add(BuildProPill());
            card.Add(Headline(string.IsNullOrEmpty(state.Headline) ? "You own Sidekick Pro" : state.Headline));
            if (!string.IsNullOrEmpty(state.Subhead))
            {
                card.Add(Subhead(state.Subhead));
            }

            card.Add(BuildFeatureSection(state));

            _installButton = BuildCta(
                string.IsNullOrEmpty(state.CtaLabel) ? "Install Pro" : state.CtaLabel,
                !state.InstallInProgress,
                () => InstallActionRequested?.Invoke(),
                withArrow: false);
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

        // ── Shared building blocks ──────────────────────────────────────────────────

        private static VisualElement NewCard()
        {
            var card = new VisualElement();
            card.AddToClassList("sk-paywall-modal");
            card.Add(PaywallIconRenderer.CreateTopHairline());
            return card;
        }

        private static VisualElement BuildProPill()
        {
            var pill = new VisualElement();
            pill.AddToClassList("sk-paywall-pro-pill");

            var icon = PaywallIconRenderer.Create(PaywallIcon.Spark, Dark, 11f);
            icon.AddToClassList("sk-paywall-pro-pill-icon");
            pill.Add(icon);

            var label = new Label("RYX SIDEKICK PRO");
            label.AddToClassList("sk-paywall-pro-pill-label");
            pill.Add(label);
            return pill;
        }

        private static Label Headline(string text)
        {
            var headline = new Label(text ?? string.Empty);
            headline.AddToClassList("sk-paywall-headline");
            return headline;
        }

        private static Label Subhead(string text)
        {
            var subhead = new Label(text);
            subhead.AddToClassList("sk-paywall-subhead");
            return subhead;
        }

        private static VisualElement BuildFeatureSection(PaywallViewState state)
        {
            var section = new VisualElement();
            section.AddToClassList("sk-paywall-features");

            var features = state.Features ?? Array.Empty<PaywallFeatureItem>();
            var providers = features.Where(f => f.IsProvider).ToList();
            var others = features.Where(f => !f.IsProvider).ToList();

            if (providers.Count > 0)
            {
                section.Add(BuildEnginesHero(providers));
            }

            if (others.Count > 0)
            {
                var grid = new VisualElement();
                grid.AddToClassList("sk-paywall-cards2");
                foreach (var item in others)
                {
                    grid.Add(BuildFeatureCard(item));
                }
                section.Add(grid);
            }

            return section;
        }

        private static VisualElement BuildEnginesHero(List<PaywallFeatureItem> providers)
        {
            var hero = new VisualElement();
            hero.AddToClassList("sk-paywall-hero");
            if (providers.Any(p => p.IsHighlighted))
            {
                hero.AddToClassList("highlighted");
            }

            hero.Add(IconBox(PaywallIcon.Engines, Blue, "sk-paywall-iconbox--engines"));

            var body = new VisualElement();
            body.AddToClassList("sk-paywall-hero-body");

            var name = new Label(EnginesTitle);
            name.AddToClassList("sk-paywall-feature-name");
            body.Add(name);

            var desc = new Label(EnginesDescription);
            desc.AddToClassList("sk-paywall-feature-desc");
            body.Add(desc);

            var chips = new VisualElement();
            chips.AddToClassList("sk-paywall-chips");
            foreach (var provider in providers)
            {
                chips.Add(BuildChip(provider));
            }
            chips.Add(BuildMoreChip());
            body.Add(chips);

            hero.Add(body);
            return hero;
        }

        private static VisualElement BuildChip(PaywallFeatureItem provider)
        {
            var chip = new VisualElement();
            chip.AddToClassList("sk-paywall-chip");

            var (kind, color) = ResolveChipIcon(provider.Id);
            if (kind.HasValue)
            {
                var icon = PaywallIconRenderer.Create(kind.Value, color, 10f);
                icon.AddToClassList("sk-paywall-chip-icon");
                chip.Add(icon);
            }

            var label = new Label(provider.DisplayName ?? string.Empty);
            label.AddToClassList("sk-paywall-chip-label");
            chip.Add(label);
            return chip;
        }

        private static VisualElement BuildMoreChip()
        {
            var chip = new VisualElement();
            chip.AddToClassList("sk-paywall-chip");
            chip.AddToClassList("sk-paywall-chip--more");

            var icon = PaywallIconRenderer.Create(PaywallIcon.ChipMore, Muted, 10f);
            icon.AddToClassList("sk-paywall-chip-icon");
            chip.Add(icon);

            var label = new Label(MoreChipLabel);
            label.AddToClassList("sk-paywall-chip-label");
            chip.Add(label);
            return chip;
        }

        private static VisualElement BuildFeatureCard(PaywallFeatureItem item)
        {
            var card = new VisualElement();
            card.AddToClassList("sk-paywall-card");
            if (item.IsHighlighted)
            {
                card.AddToClassList("highlighted");
            }

            var (kind, color, boxModifier) = ResolveCardVisual(item.Id);
            card.Add(IconBox(kind, color, boxModifier));

            var name = new Label(item.DisplayName ?? string.Empty);
            name.AddToClassList("sk-paywall-feature-name");
            card.Add(name);

            if (!string.IsNullOrEmpty(item.Description))
            {
                var desc = new Label(item.Description);
                desc.AddToClassList("sk-paywall-feature-desc");
                card.Add(desc);
            }

            return card;
        }

        private static VisualElement IconBox(PaywallIcon icon, Color color, string modifierClass)
        {
            var box = new VisualElement();
            box.AddToClassList("sk-paywall-iconbox");
            if (!string.IsNullOrEmpty(modifierClass))
            {
                box.AddToClassList(modifierClass);
            }
            box.Add(PaywallIconRenderer.Create(icon, color, 18f));
            return box;
        }

        private static VisualElement BuildPriceBlock(string price)
        {
            var row = new VisualElement();
            row.AddToClassList("sk-paywall-price-row");

            var priceLabel = new Label(price);
            priceLabel.AddToClassList("sk-paywall-price");
            row.Add(priceLabel);

            var caption = new Label(PriceCaption);
            caption.AddToClassList("sk-paywall-price-caption");
            row.Add(caption);
            return row;
        }

        private static Button BuildCta(string label, bool enabled, Action onClick, bool withArrow)
        {
            var cta = new Button(() => onClick?.Invoke()) { text = string.Empty };
            cta.AddToClassList("sk-paywall-cta");
            cta.SetEnabled(enabled);

            var labelEl = new Label(label);
            labelEl.AddToClassList("sk-paywall-cta-label");
            cta.Add(labelEl);

            if (withArrow)
            {
                var arrow = PaywallIconRenderer.Create(PaywallIcon.Arrow, Dark, 15f);
                arrow.AddToClassList("sk-paywall-cta-arrow");
                cta.Add(arrow);
            }

            return cta;
        }

        private static (PaywallIcon? kind, Color color) ResolveChipIcon(string id)
        {
            switch (id)
            {
                case "cursor": return (PaywallIcon.ChipCursor, Blue);
                case "codex": return (PaywallIcon.ChipCodex, Green);
                default: return (null, Muted);
            }
        }

        private static (PaywallIcon kind, Color color, string boxModifier) ResolveCardVisual(string id)
        {
            switch (id)
            {
                case "skills": return (PaywallIcon.Skills, Teal, "sk-paywall-iconbox--skills");
                case "mcp-management": return (PaywallIcon.Mcp, Gold, "sk-paywall-iconbox--mcp");
                default: return (PaywallIcon.Spark, Gold, "sk-paywall-iconbox--mcp");
            }
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
