// SPDX-License-Identifier: GPL-3.0-only
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    /// <summary>Vector glyphs used by the Pro paywall (refined "F" design).</summary>
    internal enum PaywallIcon
    {
        Spark,      // PRO pill — 4-point star
        Engines,    // providers hero — robot
        Skills,     // skills card — </>
        Mcp,        // MCP card — stacked server bars
        Arrow,      // CTA — right arrow
        ChipCursor, // engine chip — play/cursor triangle
        ChipCodex,  // engine chip — terminal prompt
        ChipMore    // engine chip — plus
    }

    /// <summary>
    /// Draws the paywall's line-art icons with <see cref="Painter2D"/> via
    /// <c>generateVisualContent</c> — no external SVG/VectorImage dependency, theme-independent,
    /// crisp at any DPI. Paths are ported from the "Paywall Variants" design (variant F),
    /// authored against a 24×24 view box and scaled into the element's content rect.
    /// </summary>
    internal static class PaywallIconRenderer
    {
        private const float ViewBox = 24f;

        /// <summary>Build a fixed-size element that paints <paramref name="icon"/> in <paramref name="color"/>.</summary>
        public static VisualElement Create(PaywallIcon icon, Color color, float sizePx)
        {
            var el = new VisualElement();
            el.pickingMode = PickingMode.Ignore;
            el.style.width = sizePx;
            el.style.height = sizePx;
            el.style.minWidth = sizePx;
            el.style.minHeight = sizePx;
            el.style.flexShrink = 0;
            el.generateVisualContent += ctx => Paint(ctx, icon, color);
            return el;
        }

        private static void Paint(MeshGenerationContext ctx, PaywallIcon icon, Color color)
        {
            var rect = ctx.visualElement.contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            var s = rect.width / ViewBox;
            var p = ctx.painter2D;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.strokeColor = color;
            p.fillColor = color;

            Vector2 V(float x, float y) => new Vector2(rect.x + x * s, rect.y + y * s);

            switch (icon)
            {
                case PaywallIcon.Spark:
                    p.BeginPath();
                    p.MoveTo(V(12f, 3f));
                    p.LineTo(V(13.8f, 7.8f));
                    p.LineTo(V(18.6f, 9.6f));
                    p.LineTo(V(13.8f, 11.4f));
                    p.LineTo(V(12f, 16.2f));
                    p.LineTo(V(10.2f, 11.4f));
                    p.LineTo(V(5.4f, 9.6f));
                    p.LineTo(V(10.2f, 7.8f));
                    p.ClosePath();
                    p.Fill();
                    break;

                case PaywallIcon.Engines:
                    p.lineWidth = 1.8f * s;
                    // body
                    p.BeginPath();
                    p.MoveTo(V(4f, 8f));
                    p.LineTo(V(20f, 8f));
                    p.LineTo(V(20f, 19f));
                    p.LineTo(V(4f, 19f));
                    p.ClosePath();
                    // antenna + side nubs
                    p.MoveTo(V(12f, 8f));
                    p.LineTo(V(12f, 4.6f));
                    p.MoveTo(V(4f, 13f));
                    p.LineTo(V(2.4f, 13f));
                    p.MoveTo(V(20f, 13f));
                    p.LineTo(V(21.6f, 13f));
                    p.Stroke();
                    // dots (antenna tip + two eyes)
                    Dot(p, V(12f, 3f), 1.2f * s);
                    Dot(p, V(9f, 13.5f), 1.25f * s);
                    Dot(p, V(15f, 13.5f), 1.25f * s);
                    break;

                case PaywallIcon.Skills:
                    p.lineWidth = 2f * s;
                    p.BeginPath();
                    p.MoveTo(V(8f, 6f));
                    p.LineTo(V(3f, 12f));
                    p.LineTo(V(8f, 18f));
                    p.MoveTo(V(16f, 6f));
                    p.LineTo(V(21f, 12f));
                    p.LineTo(V(16f, 18f));
                    p.MoveTo(V(14f, 4f));
                    p.LineTo(V(10f, 20f));
                    p.Stroke();
                    break;

                case PaywallIcon.Mcp:
                {
                    // Official Model Context Protocol logo (three swooshes). Paths are absolute and
                    // authored against a 180×180 view box, so they get their own mapper here.
                    var m = rect.width / 180f;
                    Vector2 W(float x, float y) => new Vector2(rect.x + x * m, rect.y + y * m);
                    p.lineWidth = 15f * m;
                    p.BeginPath();
                    // swoosh 1
                    p.MoveTo(W(18f, 84.8528f));
                    p.LineTo(W(85.8822f, 16.9706f));
                    p.BezierCurveTo(W(95.2548f, 7.59798f), W(110.451f, 7.59798f), W(119.823f, 16.9706f));
                    p.BezierCurveTo(W(129.196f, 26.3431f), W(129.196f, 41.5391f), W(119.823f, 50.9117f));
                    p.LineTo(W(68.5581f, 102.177f));
                    // swoosh 2
                    p.MoveTo(W(69.2652f, 101.47f));
                    p.LineTo(W(119.823f, 50.9117f));
                    p.BezierCurveTo(W(129.196f, 41.5391f), W(144.392f, 41.5391f), W(153.765f, 50.9117f));
                    p.LineTo(W(154.118f, 51.2652f));
                    p.BezierCurveTo(W(163.491f, 60.6378f), W(163.491f, 75.8338f), W(154.118f, 85.2063f));
                    p.LineTo(W(92.7248f, 146.6f));
                    p.BezierCurveTo(W(89.6006f, 149.724f), W(89.6006f, 154.789f), W(92.7248f, 157.913f));
                    p.LineTo(W(105.331f, 170.52f));
                    // swoosh 3
                    p.MoveTo(W(102.853f, 33.9411f));
                    p.LineTo(W(52.6482f, 84.1457f));
                    p.BezierCurveTo(W(43.2756f, 93.5183f), W(43.2756f, 108.714f), W(52.6482f, 118.087f));
                    p.BezierCurveTo(W(62.0208f, 127.459f), W(77.2167f, 127.459f), W(86.5893f, 118.087f));
                    p.LineTo(W(136.794f, 67.8822f));
                    p.Stroke();
                    break;
                }

                case PaywallIcon.Arrow:
                    p.lineWidth = 2.4f * s;
                    p.BeginPath();
                    p.MoveTo(V(5f, 12f));
                    p.LineTo(V(19f, 12f));
                    p.MoveTo(V(13f, 6f));
                    p.LineTo(V(19f, 12f));
                    p.LineTo(V(13f, 18f));
                    p.Stroke();
                    break;

                case PaywallIcon.ChipCursor:
                    p.BeginPath();
                    p.MoveTo(V(5f, 3f));
                    p.LineTo(V(20f, 10.5f));
                    p.LineTo(V(13.5f, 12.5f));
                    p.LineTo(V(11.5f, 19f));
                    p.ClosePath();
                    p.Fill();
                    break;

                case PaywallIcon.ChipCodex:
                    p.lineWidth = 2.2f * s;
                    p.BeginPath();
                    p.MoveTo(V(7f, 8f));
                    p.LineTo(V(11f, 12f));
                    p.LineTo(V(7f, 16f));
                    p.MoveTo(V(13f, 16f));
                    p.LineTo(V(17f, 16f));
                    p.Stroke();
                    break;

                case PaywallIcon.ChipMore:
                    p.lineWidth = 2.4f * s;
                    p.BeginPath();
                    p.MoveTo(V(12f, 5f));
                    p.LineTo(V(12f, 19f));
                    p.MoveTo(V(5f, 12f));
                    p.LineTo(V(19f, 12f));
                    p.Stroke();
                    break;
            }
        }

        private static void Dot(Painter2D p, Vector2 center, float radius)
        {
            p.BeginPath();
            p.Arc(center, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            p.Fill();
        }

        // ── Top accent hairline (gold gradient, transparent→gold→transparent) ──────────

        private static Texture2D _hairlineTex;

        /// <summary>A 2px-tall element painted with a soft gold gradient — the card's top accent.</summary>
        public static VisualElement CreateTopHairline()
        {
            var bar = new VisualElement();
            bar.pickingMode = PickingMode.Ignore;
            bar.AddToClassList("sk-paywall-hairline");
            bar.style.backgroundImage = new StyleBackground(GetHairlineTexture());
            return bar;
        }

        private static Texture2D GetHairlineTexture()
        {
            if (_hairlineTex != null)
            {
                return _hairlineTex;
            }

            const int w = 96;
            var gold = new Color(0xE9 / 255f, 0xC4 / 255f, 0x6A / 255f);
            var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (var x = 0; x < w; x++)
            {
                // Triangular alpha ramp peaking at the centre, capped at ~0.7 like the design.
                var t = x / (float)(w - 1);
                var a = (1f - Mathf.Abs(t - 0.5f) * 2f) * 0.7f;
                tex.SetPixel(x, 0, new Color(gold.r, gold.g, gold.b, a));
            }
            tex.Apply();
            _hairlineTex = tex;
            return tex;
        }
    }
}
