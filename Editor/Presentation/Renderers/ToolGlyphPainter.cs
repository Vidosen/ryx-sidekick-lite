// SPDX-License-Identifier: GPL-3.0-only
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    /// <summary>
    /// Painter2D drawings for tool-header glyphs, scaled from a 24×24 design viewBox to the
    /// host element's content rect. Vector-drawn (no texture assets, no com.unity.vectorgraphics)
    /// so the icon stays crisp at any DPI and follows the element's resolved color.
    /// </summary>
    internal static class ToolGlyphPainter
    {
        // Terminal ">_" glyph. Design SVG (viewBox 0 0 24 24, stroke 1.7, round caps/joins):
        //   M4 17 l6-6 -6-6   (the ">" caret)
        //   M12 19 h8         (the "_" baseline)
        internal static void DrawTerminal(MeshGenerationContext mgc, Color color)
        {
            var rect = mgc.visualElement.contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            var sx = rect.width / 24f;
            var sy = rect.height / 24f;
            Vector2 P(float x, float y) => new Vector2(x * sx, y * sy);

            var painter = mgc.painter2D;
            painter.lineWidth = Mathf.Max(1f, 1.7f * Mathf.Min(sx, sy));
            painter.strokeColor = color;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            // ">" caret
            painter.BeginPath();
            painter.MoveTo(P(4f, 17f));
            painter.LineTo(P(10f, 11f));
            painter.LineTo(P(4f, 5f));
            painter.Stroke();

            // "_" baseline
            painter.BeginPath();
            painter.MoveTo(P(12f, 19f));
            painter.LineTo(P(20f, 19f));
            painter.Stroke();
        }

        // MCP badge hexagon. Design SVG (viewBox 0 0 24 24, stroke 2, round joins):
        //   M12 2 L21 7 V17 L12 22 L3 17 V7 Z
        internal static void DrawMcpHexagon(MeshGenerationContext mgc, Color color)
        {
            var rect = mgc.visualElement.contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            var sx = rect.width / 24f;
            var sy = rect.height / 24f;
            Vector2 P(float x, float y) => new Vector2(x * sx, y * sy);

            var painter = mgc.painter2D;
            painter.lineWidth = Mathf.Max(1f, 2f * Mathf.Min(sx, sy));
            painter.strokeColor = color;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            painter.BeginPath();
            painter.MoveTo(P(12f, 2f));
            painter.LineTo(P(21f, 7f));
            painter.LineTo(P(21f, 17f));
            painter.LineTo(P(12f, 22f));
            painter.LineTo(P(3f, 17f));
            painter.LineTo(P(3f, 7f));
            painter.ClosePath();
            painter.Stroke();
        }
    }
}
