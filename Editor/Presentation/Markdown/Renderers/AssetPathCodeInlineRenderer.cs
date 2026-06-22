// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using Markdig.Syntax.Inlines;
using Ryx.Sidekick.Editor.Infrastructure.Assets;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    /// <summary>
    /// Renders code inlines that contain valid asset paths as clickable AssetLinkElements.
    /// Has higher priority (lower number) than CodeInlineRenderer to intercept asset paths first.
    /// Fixed: No longer adds rich text between markers to avoid "b>" artifacts.
    /// </summary>
    internal class AssetPathCodeInlineRenderer : IMarkdownInlineRenderer
    {
        // Lower priority number = higher precedence (runs before CodeInlineRenderer at 100)
        public int Priority => 50;

        public bool CanRender(Inline inline)
        {
            if (inline is CodeInline code)
            {
                return AssetLinkService.IsValidAssetPath(code.Content);
            }
            return false;
        }

        public void Render(Inline inline, StringBuilder builder, VisualElement parent,
            MarkdownRenderContext context, RenderInlineChildrenDelegate renderChildren)
        {
            var code = (CodeInline)inline;
            var path = code.Content;

            if (context.UseRichTextForInlines && builder != null)
            {
                // Reserve a transparent placeholder under the overlaid AssetLinkElement and
                // record an asset-link span.
                var fileName = AssetLinkService.GetAssetNameWithExtension(path);
                MarkdownRichText.AppendAssetPlaceholder(builder, fileName, context.Spans, path);
            }
            else
            {
                // In non-rich-text mode, we can add the AssetLinkElement directly
                var assetLink = new AssetLinkElement(path);
                parent?.Add(assetLink);
            }
        }
    }
}
