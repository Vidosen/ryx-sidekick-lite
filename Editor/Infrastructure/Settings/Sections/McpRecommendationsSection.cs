// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Ryx.Sidekick.Editor.Domain.Pro;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Pro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// "Recommended for Unity" section on the MCP Project Settings page (B4).
    /// Renders curated MCP server cards sourced from the remote-config envelope.
    /// Hides itself (returns empty root) when no renderable items survive filtering.
    /// </summary>
    internal sealed class McpRecommendationsSection : IMcpSettingsSection
    {
        private readonly Func<GetMcpRecommendationsQuery> _queryFactory;
        private readonly IExternalUrlOpener _urlOpener;
        private readonly RecommendationIconLoader _iconLoader = new RecommendationIconLoader();
        private GetMcpRecommendationsQuery _query;

        public string Id => "mcp-recommendations";
        public int Order => 40;

        /// <summary>
        /// ctor: stores the factory and url opener. No IO happens here — the Lazy<> in the bootstrap
        /// defers source composition to first access; this ctor is called during [InitializeOnLoad] time.
        /// </summary>
        public McpRecommendationsSection(Func<GetMcpRecommendationsQuery> queryFactory, IExternalUrlOpener urlOpener)
        {
            _queryFactory = queryFactory;
            _urlOpener = urlOpener;
        }

        public VisualElement Build(McpSettingsSectionContext ctx)
        {
            // Lazily resolve the query on first Build — avoids any IO at [InitializeOnLoad] time.
            _query ??= _queryFactory();

            var root = new VisualElement();

            void Populate()
            {
                root.Clear();

                var manifest = _query.Get();
                var items = manifest?.Items?.Where(RemoteConfigValidator.IsRenderableItem).ToList()
                            ?? new List<McpRecommendationItem>();

                if (items.Count == 0)
                {
                    // Section hides itself — root stays empty, no header or placeholder.
                    root.RemoveFromClassList("sk-mcpset-group");
                    return;
                }

                root.AddToClassList("sk-mcpset-group");

                root.Add(SidekickSettingsSectionBuilder.SectionHeader("Popular with Unity developers"));

                var subtext = new Label(
                    "Third-party servers — Sidekick doesn't audit or endorse them; " +
                    "review each project before installing.");
                subtext.AddToClassList("sk-mcpset-reco-subtext");
                root.Add(subtext);

                foreach (var item in items)
                    root.Add(BuildCard(item));
            }

            Populate();

            // Fire one async refresh; re-render only this section on completion.
            KickRefresh(root, Populate);

            return root;
        }

        private async void KickRefresh(VisualElement root, Action repopulate)
        {
            try { await _query.RefreshAsync(); }
            catch { return; }
            // Marshal to main thread; guard against the page having been closed (detached panel).
            EditorApplication.delayCall += () =>
            {
                if (root.panel != null) repopulate();
            };
        }

        private VisualElement BuildCard(McpRecommendationItem item)
        {
            var card = new VisualElement();
            card.AddToClassList("sk-mcpset-reco-card");
            card.style.flexDirection = FlexDirection.Row;
            card.style.alignItems = Align.Center;

            // --- Logo: 32x32 element with fallback letter tile ---
            var logo = new VisualElement();
            logo.AddToClassList("sk-mcpset-reco-logo");
            logo.style.width = 32;
            logo.style.height = 32;
            logo.style.flexShrink = 0;
            logo.style.marginRight = 10;

            // Fallback letter tile — always present; hidden if icon loads successfully.
            var letter = new Label(!string.IsNullOrWhiteSpace(item.Title)
                ? item.Title[0].ToString().ToUpperInvariant()
                : "?");
            letter.AddToClassList("sk-mcpset-reco-logo-letter");
            letter.style.width = Length.Percent(100);
            letter.style.height = Length.Percent(100);
            letter.style.unityTextAlign = TextAnchor.MiddleCenter;
            logo.Add(letter);

            // Async icon load — self-contained, never throws into the section.
            if (!string.IsNullOrWhiteSpace(item.IconUrl))
            {
                _iconLoader.Load(item.IconUrl, tex =>
                {
                    if (logo.panel != null && tex != null)
                    {
                        logo.style.backgroundImage = new StyleBackground(tex);
                        letter.style.display = DisplayStyle.None;
                    }
                });
            }

            card.Add(logo);

            // --- Middle column: title + description ---
            var middle = new VisualElement();
            middle.style.flexGrow = 1;
            middle.style.flexShrink = 1;
            middle.style.minWidth = 0;

            var title = new Label(item.Title);
            title.AddToClassList("sk-mcpset-reco-title");
            middle.Add(title);

            if (!string.IsNullOrEmpty(item.ShortDescription))
            {
                var desc = new Label(item.ShortDescription);
                desc.AddToClassList("sk-mcpset-reco-desc");
                desc.style.whiteSpace = WhiteSpace.Normal;
                middle.Add(desc);
            }

            card.Add(middle);

            // --- Right: "Learn more" button ---
            var learnMore = new Button(() => _urlOpener.Open(item.Url)) { text = "Learn more" };
            learnMore.AddToClassList("sk-mcpset-reco-learn");
            learnMore.style.flexShrink = 0;
            learnMore.style.marginLeft = 10;
            card.Add(learnMore);

            return card;
        }
    }
}
