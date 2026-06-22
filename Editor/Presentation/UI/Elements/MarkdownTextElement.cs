// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.UI.Elements
{
    /// <summary>
    /// A single selectable rich-text element for one Markdown block. Holds the FULL block
    /// text (so wrapping, selection and copy are native and correct) and renders inline
    /// extras on top of it without breaking the text flow:
    /// <list type="bullet">
    /// <item>inline-code spans get a rounded chip drawn BEHIND the text in
    /// <see cref="VisualElement.generateVisualContent"/> via Painter2D;</item>
    /// <item>asset-link spans get an <see cref="AssetLinkElement"/> overlaid on top of the
    /// (transparent) reserved text.</item>
    /// </list>
    /// Glyph geometry comes from the public <c>ITextSelection.GetCursorPositionFromStringIndex</c>
    /// (no reflection). Span offsets are converted from raw to displayed indices via
    /// <see cref="MarkdownDisplayedIndex"/> because that API indexes the visible text.
    /// </summary>
    internal sealed class MarkdownTextElement : SelectableLabel
    {
        // Fallback chip color (--sk-code-inline-bg, dark editor theme) used until the custom
        // style resolves; kept in sync via CustomStyleResolvedEvent so theme switches repaint.
        private static readonly Color DefaultCodeChipColor = new Color(110f / 255f, 118f / 255f, 129f / 255f, 0.2f);
        private static readonly CustomStyleProperty<Color> ChipBgProperty =
            new CustomStyleProperty<Color>("--sk-code-inline-bg");
        private Color _chipColor = DefaultCodeChipColor;

        private const float ChipRadius = 4f;
        // Most horizontal padding comes from the thin spaces CodeInlineRenderer reserves
        // inside the chip span; this is just a small outward nudge on top of that.
        private const float ChipHorizontalPad = 1f;
        private const float ChipVerticalPad = 2f;

        private readonly struct ResolvedSpan
        {
            public readonly MarkdownSpanKind Kind;
            public readonly int DisplayedStart;
            public readonly int DisplayedEnd;
            public readonly string Payload;

            public ResolvedSpan(MarkdownSpanKind kind, int start, int end, string payload)
            {
                Kind = kind;
                DisplayedStart = start;
                DisplayedEnd = end;
                Payload = payload;
            }
        }

        private readonly List<ResolvedSpan> _spans = new();
        private readonly List<Rect> _codeChipRects = new();
        private readonly List<AssetLinkElement> _assetOverlays = new();

        private TextElement _textElement;
        private float _lastWidth = -1f;

        // Test hooks (same-assembly/InternalsVisibleTo only).
        internal int CodeChipRectCount => _codeChipRects.Count;
        internal int AssetOverlayCount => _assetOverlays.Count;

        public MarkdownTextElement()
        {
            enableRichText = true;
            pickingMode = PickingMode.Position;
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        /// <summary>
        /// Sets the block's rich-text string and the inline spans to decorate. Span offsets
        /// are RAW (into <paramref name="richText"/>) and converted to displayed indices here.
        /// </summary>
        public void SetContent(string richText, IReadOnlyList<SpanDescriptor> spans)
        {
            SetValueWithoutNotify(richText ?? string.Empty);
            enableRichText = true;

            _spans.Clear();
            _codeChipRects.Clear();

            if (spans != null && spans.Count > 0)
            {
                var map = MarkdownDisplayedIndex.BuildMap(richText);
                foreach (var span in spans)
                {
                    int s = ClampIndex(map, span.StartIndex);
                    int e = ClampIndex(map, span.EndIndex);
                    if (e <= s)
                        continue;
                    _spans.Add(new ResolvedSpan(span.Kind, s, e, span.Payload));
                }
            }

            SyncAssetOverlays();

            _lastWidth = -1f; // force recompute on next geometry pass
            RecomputeGeometry();
        }

        private static int ClampIndex(int[] map, int rawIndex)
        {
            if (rawIndex < 0)
                return 0;
            if (rawIndex >= map.Length)
                return map[map.Length - 1];
            return map[rawIndex];
        }

        private void SyncAssetOverlays()
        {
            // Remove previous overlays — cheap (asset spans are rare) and avoids stale icons.
            foreach (var overlay in _assetOverlays)
                overlay.RemoveFromHierarchy();
            _assetOverlays.Clear();

            foreach (var span in _spans)
            {
                if (span.Kind != MarkdownSpanKind.AssetLink)
                    continue;
                var overlay = new AssetLinkElement(span.Payload);
                overlay.style.position = Position.Absolute;
                // Zero margins so the widget aligns to the reserved placeholder start.
                overlay.style.marginLeft = 0;
                overlay.style.marginRight = 0;
                overlay.style.marginTop = 0;
                overlay.style.marginBottom = 0;
                overlay.style.display = DisplayStyle.None; // positioned once geometry is known
                Add(overlay);
                _assetOverlays.Add(overlay);
            }
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // Recompute only when the line layout could have changed.
            if (Mathf.Approximately(evt.newRect.width, _lastWidth))
                return;
            _lastWidth = evt.newRect.width;
            RecomputeGeometry();
        }

        private void RecomputeGeometry()
        {
            _codeChipRects.Clear();
            _textElement ??= this.Q<TextElement>();
            if (_textElement?.selection == null || panel == null)
                return;

            float lineHeight = LineHeight();
            int overlayIndex = 0;

            foreach (var span in _spans)
            {
                if (span.Kind == MarkdownSpanKind.Code)
                {
                    AppendCodeChipRects(span, lineHeight);
                }
                else if (span.Kind == MarkdownSpanKind.AssetLink && overlayIndex < _assetOverlays.Count)
                {
                    PositionAssetOverlay(_assetOverlays[overlayIndex], span, lineHeight);
                    overlayIndex++;
                }
            }

            MarkDirtyRepaint();
        }

        private float LineHeight()
        {
            float fontSize = _textElement.resolvedStyle.fontSize;
            return fontSize > 0f ? fontSize * 1.2f : 16f;
        }

        private bool TryCursorPos(int displayedIndex, out Vector2 pos)
        {
            try
            {
                pos = _textElement.selection.GetCursorPositionFromStringIndex(displayedIndex);
                return true;
            }
            catch
            {
                pos = default;
                return false;
            }
        }

        private void AppendCodeChipRects(ResolvedSpan span, float lineHeight)
        {
            // Walk displayed indices, grouping by line (detected via a Y jump) so a wrapped
            // span produces one chip rect per line.
            if (!TryCursorPos(span.DisplayedStart, out var runStart))
                return;
            var prev = runStart;

            for (int i = span.DisplayedStart + 1; i <= span.DisplayedEnd; i++)
            {
                if (!TryCursorPos(i, out var p))
                    return;

                bool lineBreak = Mathf.Abs(p.y - prev.y) > lineHeight * 0.5f;
                bool last = i == span.DisplayedEnd;

                if (lineBreak)
                {
                    AddChipRect(runStart, prev, lineHeight);
                    runStart = p;
                }

                if (last)
                    AddChipRect(runStart, p, lineHeight);

                prev = p;
            }
        }

        private void AddChipRect(Vector2 startPos, Vector2 endPos, float lineHeight)
        {
            // startPos/endPos are te-local caret points (y = line bottom). The line box is
            // [y - lineHeight, y]; pad it outwards so the chip breathes around the glyphs.
            var teRect = new Rect(
                startPos.x - ChipHorizontalPad,
                startPos.y - lineHeight - ChipVerticalPad,
                (endPos.x - startPos.x) + 2f * ChipHorizontalPad,
                lineHeight + 2f * ChipVerticalPad);

            var topLeft = _textElement.ChangeCoordinatesTo(this, new Vector2(teRect.x, teRect.y));
            _codeChipRects.Add(new Rect(topLeft, teRect.size));
        }

        private void PositionAssetOverlay(AssetLinkElement overlay, ResolvedSpan span, float lineHeight)
        {
            if (!TryCursorPos(span.DisplayedStart, out var startPos))
            {
                overlay.style.display = DisplayStyle.None;
                return;
            }

            var teTop = new Vector2(startPos.x, startPos.y - lineHeight);
            var hostTop = _textElement.ChangeCoordinatesTo(this, teTop);
            overlay.style.left = hostTop.x;
            overlay.style.top = hostTop.y;
            overlay.style.display = DisplayStyle.Flex;
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(ChipBgProperty, out var color) && color != _chipColor)
            {
                _chipColor = color;
                MarkDirtyRepaint();
            }
        }

        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            if (_codeChipRects.Count == 0)
                return;

            var painter = ctx.painter2D;
            painter.fillColor = _chipColor;
            foreach (var rect in _codeChipRects)
                DrawRoundedRect(painter, rect, ChipRadius);
        }

        private static void DrawRoundedRect(Painter2D p, Rect r, float radius)
        {
            float rad = Mathf.Min(radius, r.width * 0.5f, r.height * 0.5f);
            if (rad < 0f)
                return;

            p.BeginPath();
            p.MoveTo(new Vector2(r.xMin + rad, r.yMin));
            p.LineTo(new Vector2(r.xMax - rad, r.yMin));
            p.ArcTo(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMin + rad), rad);
            p.LineTo(new Vector2(r.xMax, r.yMax - rad));
            p.ArcTo(new Vector2(r.xMax, r.yMax), new Vector2(r.xMax - rad, r.yMax), rad);
            p.LineTo(new Vector2(r.xMin + rad, r.yMax));
            p.ArcTo(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMax - rad), rad);
            p.LineTo(new Vector2(r.xMin, r.yMin + rad));
            p.ArcTo(new Vector2(r.xMin, r.yMin), new Vector2(r.xMin + rad, r.yMin), rad);
            p.ClosePath();
            p.Fill();
        }
    }
}
