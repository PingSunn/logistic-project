using System;
using SkiaSharp;

namespace logistic;

internal static partial class PdfExporter
{
    private sealed partial class Renderer
    {
        // ── Text ──────────────────────────────────────────────────────────────

        private void DrawText(string text, float x, float y, float size,
                              SKColor color, bool bold = false)
        {
            using var p = MkPaint(color, size, bold);
            p.GetFontMetrics(out var m);
            _canvas.DrawText(text, x, y + (-m.Ascent), p);
        }

        private void DrawTextRight(string text, float rightX, float y, float size, SKColor color)
        {
            using var p = MkPaint(color, size);
            p.GetFontMetrics(out var m);
            float tw = p.MeasureText(text);
            _canvas.DrawText(text, rightX - tw, y + (-m.Ascent), p);
        }

        private float MeasureText(string text, float size, bool bold = false)
        {
            using var p = MkPaint(SKColors.Black, size, bold);
            return p.MeasureText(text);
        }

        // ── Shapes ────────────────────────────────────────────────────────────

        private void DrawRoundRect(float x, float y, float w, float h, float r, SKPaint paint)
        {
            _canvas.DrawRoundRect(x, y, w, h, r, r, paint);
            paint.Dispose();
        }

        private void DrawCbmBar(float x, float y, float w, float h, float ratio)
        {
            ratio = Math.Clamp(ratio, 0f, 1f);
            DrawRoundRect(x, y, w, h, h / 2f, Fill(C(0xEA, 0xED, 0xF2)));
            SKColor fill = ratio < 0.95f ? C(0x22, 0xC5, 0x5E) : C(0xEF, 0x44, 0x44);
            DrawRoundRect(x, y, Math.Max(ratio * w, h), h, h / 2f, Fill(fill));
        }

        private float DrawStat(float x, float y, string label, string value,
                               SKColor? valColor = null)
        {
            using var lp = MkPaint(C(0x33, 0x41, 0x55), 9, bold: true);
            lp.GetFontMetrics(out var m);
            float baseline = y + (-m.Ascent);
            _canvas.DrawText(label, x, baseline, lp);
            float lw = lp.MeasureText(label);

            x += lw + 3;
            using var vp = MkPaint(valColor ?? C(0x33, 0x41, 0x55), 9);
            _canvas.DrawText(value, x, baseline, vp);
            return x + vp.MeasureText(value);
        }

        // ── Color helpers ─────────────────────────────────────────────────────

        private static SKColor DarkenColor(SKColor c, float t) => new(
            (byte)Math.Clamp(c.Red   * (1f - t), 0, 255),
            (byte)Math.Clamp(c.Green * (1f - t), 0, 255),
            (byte)Math.Clamp(c.Blue  * (1f - t), 0, 255));

        // ── Paint factories ───────────────────────────────────────────────────

        private SKPaint MkPaint(SKColor color, float size, bool bold = false) => new()
        {
            Typeface     = tf,
            TextSize     = size,
            Color        = color,
            IsAntialias  = true,
            FakeBoldText = bold,
        };

        private static SKPaint Fill(SKColor color) => new()
        {
            Color       = color,
            Style       = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        private static SKPaint Stroke(SKColor color, float width) => new()
        {
            Color       = color,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = width,
            IsAntialias = true,
        };

        private static SKColor C(byte r, byte g, byte b) => new(r, g, b);
    }
}
