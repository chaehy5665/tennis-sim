using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TennisSim.Coach
{
    // Shared drawing for the 2D top views. Colours are the design tokens; engine metres map linearly to the element.
    static class CourtPaint
    {
        public static readonly Color Ground = Hex("#060b11"), Court = Hex("#144040"), Line = Hex("#ffffff"), Net = Hex("#8c969e"),
            LineMuted = Hex("#aab4bb"), PlayerA = Hex("#ff731f"), PlayerB = Hex("#40a6ff"), Ball = Hex("#ffeb04");
        static Color Hex(string html) { ColorUtility.TryParseHtmlString(html, out var c); return c; }

        // Singles lines are the engine's outside edges (Court.HalfWidth 4.115, HalfLength 11.885, service line 6.40).
        public const float HalfWidth = 4.115f, HalfLength = 11.885f, ServiceLine = 6.40f;

        // Largest rect with the given aspect inside r, centred: the whole court range stays visible at any size.
        public static Rect Fit(Rect r, float w, float h)
        {
            float s = Mathf.Min(r.width / w, r.height / h), fw = w * s, fh = h * s;
            return new Rect(r.x + (r.width - fw) / 2, r.y + (r.height - fh) / 2, fw, fh);
        }
        public static void Segment(Painter2D p, Vector2 a, Vector2 b, Color color, float width)
        {
            p.strokeColor = color; p.lineWidth = width;
            p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
        }
        public static void Rect(Painter2D p, Vector2 a, Vector2 b, Color color, float width)
        {
            p.strokeColor = color; p.lineWidth = width;
            p.BeginPath(); p.MoveTo(a); p.LineTo(new Vector2(b.x, a.y)); p.LineTo(b); p.LineTo(new Vector2(a.x, b.y)); p.ClosePath(); p.Stroke();
        }
        public static void Fill(Painter2D p, Rect r, Color color)
        {
            p.fillColor = color;
            p.BeginPath(); p.MoveTo(new Vector2(r.xMin, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMax)); p.LineTo(new Vector2(r.xMin, r.yMax)); p.ClosePath(); p.Fill();
        }
        // Polygon circle: avoids depending on the Arc/Angle overloads.
        static void CirclePath(Painter2D p, Vector2 c, float radius)
        {
            p.BeginPath();
            for (int i = 0; i <= 24; i++)
            {
                float a = i * Mathf.PI * 2 / 24;
                var v = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath();
        }
        public static void Dot(Painter2D p, Vector2 c, float radius, Color fill, Color ring, float ringWidth)
        {
            CirclePath(p, c, radius);
            if (fill.a > 0) { p.fillColor = fill; p.Fill(); }
            if (ringWidth > 0) { p.strokeColor = ring; p.lineWidth = ringWidth; p.Stroke(); }
        }
        // Player capsule seen from above: a pill, radius-pill, ground outline.
        public static void Capsule(Painter2D p, Vector2 c, Color color)
        {
            p.fillColor = color; p.strokeColor = Ground; p.lineWidth = 2;
            p.BeginPath();
            const float half = 7, r = 9;
            for (int i = 0; i <= 12; i++) { float a = Mathf.PI / 2 + i * Mathf.PI / 12; var v = new Vector2(c.x - half + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r); if (i == 0) p.MoveTo(v); else p.LineTo(v); }
            for (int i = 0; i <= 12; i++) { float a = -Mathf.PI / 2 + i * Mathf.PI / 12; p.LineTo(new Vector2(c.x + half + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r)); }
            p.ClosePath(); p.Fill(); p.Stroke();
        }
    }

    // Live top view, court length horizontal. Z spans ±16.5 m (players have been recorded 3.7 m behind the baseline),
    // X spans ±5.5 m. The range scales with the view, keeping proportions, inside a space-3 edge margin.
    public sealed class CourtView : VisualElement
    {
        MatchView view;
        const float SpanZ = 16.5f, SpanX = 5.5f, Margin = 16f;

        public CourtView() { generateVisualContent += Draw; }
        public void Set(MatchView v) { view = v; MarkDirtyRepaint(); }

        Vector2 Map(float x, float z, Rect r) => new Vector2(r.xMin + (z + SpanZ) / (2 * SpanZ) * r.width, r.yMin + (x + SpanX) / (2 * SpanX) * r.height);

        void Draw(MeshGenerationContext ctx)
        {
            var full = contentRect;
            if (full.width <= 0 || full.height <= 0) return;
            var p = ctx.painter2D;
            CourtPaint.Fill(p, full, CourtPaint.Court);
            var inner = new Rect(full.x + Margin, full.y + Margin, Mathf.Max(1, full.width - 2 * Margin), Mathf.Max(1, full.height - 2 * Margin));
            var r = CourtPaint.Fit(inner, 2 * SpanZ, 2 * SpanX);
            float hw = CourtPaint.HalfWidth, hl = CourtPaint.HalfLength, sl = CourtPaint.ServiceLine;
            CourtPaint.Rect(p, Map(-hw, -hl, r), Map(hw, hl, r), CourtPaint.Line, 2);
            CourtPaint.Segment(p, Map(-hw, -sl, r), Map(hw, -sl, r), CourtPaint.Line, 2);
            CourtPaint.Segment(p, Map(-hw, sl, r), Map(hw, sl, r), CourtPaint.Line, 2);
            CourtPaint.Segment(p, Map(0, -sl, r), Map(0, sl, r), CourtPaint.Line, 2);
            CourtPaint.Segment(p, Map(-5.029f, 0, r), Map(5.029f, 0, r), CourtPaint.Net, 4);
            if (view == null) return;
            CourtPaint.Capsule(p, Map(view.PlayerX[0], view.PlayerZ[0], r), CourtPaint.PlayerA);
            CourtPaint.Capsule(p, Map(view.PlayerX[1], view.PlayerZ[1], r), CourtPaint.PlayerB);
            if (view.BallVisible)
            {
                // Ball drawn slightly larger with height so a lob reads as a lob; position stays the engine's X/Z.
                float size = 5 + Mathf.Clamp(view.BallY, 0, 4);
                CourtPaint.Dot(p, Map(view.BallX, view.BallZ, r), size, CourtPaint.Ball, CourtPaint.Ground, 1.5f);
            }
        }
    }

    // One player's first landings on the opponent's half, net at the bottom. Line-muted fill = backhand-target shot,
    // line-muted ring = other shot, line ring = out; no player colours (design system v25 heatmap rule).
    public sealed class HalfCourtView : VisualElement
    {
        List<Landing> landings;
        // Net to 1.6 m past the baseline and 1.4 m past each sideline, widened when a landing falls further out. The
        // span comes from every landing given at construction, so a filtered view keeps the same scale.
        readonly float spanX = 5.5f, spanZ = 13.5f;

        public HalfCourtView(List<Landing> landings)
        {
            this.landings = landings;
            foreach (var l in landings) { spanX = Mathf.Max(spanX, Mathf.Abs(l.X) + .5f); spanZ = Mathf.Max(spanZ, l.Z + .5f); }
            generateVisualContent += Draw;
        }

        // Shows a subset of the landings (a heatmap filter) at the scale of the full list.
        public void Set(List<Landing> shown) { landings = shown; MarkDirtyRepaint(); }

        Vector2 Map(float x, float z, Rect r) => new Vector2(r.xMin + (x + spanX) / (2 * spanX) * r.width, r.yMin + (spanZ - z) / spanZ * r.height);

        void Draw(MeshGenerationContext ctx)
        {
            var full = contentRect;
            if (full.width <= 0 || full.height <= 0) return;
            var p = ctx.painter2D;
            // The ground surface comes from the element's Inset class; only lines and dots are painted here.
            var r = CourtPaint.Fit(full, 2 * spanX, spanZ);
            float hw = CourtPaint.HalfWidth, hl = CourtPaint.HalfLength, sl = CourtPaint.ServiceLine;
            CourtPaint.Rect(p, Map(-hw, hl, r), Map(hw, 0, r), CourtPaint.Line, 2);
            CourtPaint.Segment(p, Map(-hw, sl, r), Map(hw, sl, r), CourtPaint.Line, 2);
            CourtPaint.Segment(p, Map(0, sl, r), Map(0, 0, r), CourtPaint.Line, 2);
            CourtPaint.Segment(p, Map(-spanX, 0, r), Map(spanX, 0, r), CourtPaint.Net, 4);
            foreach (var l in landings)
            {
                var c = Map(l.X, l.Z, r);
                if (!l.In) CourtPaint.Dot(p, c, 4, Color.clear, CourtPaint.Line, 1.5f);
                // Neutral fill, never a player colour: the tactic relation is shown by the filter chips, not by colour.
                else if (l.BackhandTarget) CourtPaint.Dot(p, c, 4, CourtPaint.LineMuted, CourtPaint.LineMuted, 0);
                else CourtPaint.Dot(p, c, 4, Color.clear, CourtPaint.LineMuted, 1.5f);
            }
        }
    }
}
