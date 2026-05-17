using System;
using System.Linq;
using SkiaSharp;

namespace logistic;

internal static partial class PdfExporter
{
    private sealed partial class Renderer
    {
        // ── Page header ───────────────────────────────────────────────────────

        private void DrawPageHeader()
        {
            DrawText("รายงานการจัดเรียงสินค้า", MX, _y, 17, C(0x1E, 0x29, 0x3B), bold: true);
            _y += 26;

            double pct         = containerCbm > 0 ? usedCbm / containerCbm * 100 : 0;
            double totalWeight = statsRows.Sum(r => r.Spec.WeightPerBoxKg * r.TotalPacked);

            DrawText(
                $"{container.Name}  ({container.SizeLabel})   วันที่ {DateTime.Today:dd/MM/yyyy}" +
                $"   CBM {usedCbm:F2}/{containerCbm:F2} m³ ({pct:F0}%)" +
                $"   น้ำหนักรวม {totalWeight:F1} kg",
                MX, _y, 9.5f, C(0x64, 0x74, 0x8B));
            _y += 15;

            DrawCbmBar(MX, _y, ContentW, 6f, (float)(usedCbm / containerCbm));
            _y += 10;

            using var hrP = Fill(C(0xE2, 0xE8, 0xF0));
            _canvas.DrawRect(MX, _y, ContentW, 1.5f, hrP);
            _y += 11;
        }

        // ── Summary table ─────────────────────────────────────────────────────

        private void DrawSummaryTable()
        {
            // Column x-positions
            float xSwatch  = MX;
            float xName    = MX + 10;
            float xPacked  = MX + 150;
            float xWeight  = MX + 210;
            float xCbm     = MX + 262;
            float xFull    = MX + 308;
            float xMixed   = MX + 343;
            float xCondo   = MX + 378;
            float xScatter = MX + 413;
            float xRem     = MX + 448;

            const float rowH    = 14f;
            const float headerH = 16f;
            const float totalH  = 14f;

            int rowCount  = statsRows.Count;
            float tableH  = headerH + rowH * rowCount + totalH + 8;
            EnsureSpace(tableH);

            // Header
            using (var bgP = Fill(C(0xF1, 0xF5, 0xF9)))
                _canvas.DrawRect(MX, _y, ContentW, headerH, bgP);

            float hy = _y + 3;
            DrawColHeader("สินค้า",     xName,    hy);
            DrawColHeader("บรรจุ/สั่ง", xPacked,  hy);
            DrawColHeader("น้ำหนัก",    xWeight,  hy);
            DrawColHeader("CBM%",       xCbm,     hy);
            DrawColHeader("ต๊ง",        xFull,    hy);
            DrawColHeader("ผสม",        xMixed,   hy);
            DrawColHeader("คอนโด",      xCondo,   hy);
            DrawColHeader("กระจาย",     xScatter, hy);
            DrawColHeader("เหลือ",      xRem,     hy);
            _y += headerH;

            foreach (var row in statsRows)
            {
                int pi = row.ProductIndex;
                SKColor col = Palette[pi % Palette.Length];

                if (pi % 2 == 1)
                {
                    using var altP = Fill(C(0xF8, 0xFA, 0xFC));
                    _canvas.DrawRect(MX, _y, ContentW, rowH, altP);
                }

                float ry = _y + 2;

                using (var swP = Fill(col))
                    _canvas.DrawRoundRect(xSwatch, ry + 2, 7, 7, 2, 2, swP);

                DrawCell($"{row.Spec.Description} {row.Spec.Content}", xName, ry, 7.5f, C(0x1E, 0x29, 0x3B));

                if (row.HasPattern)
                {
                    double weight   = row.Spec.WeightPerBoxKg * row.TotalPacked;
                    double cbmPct   = containerCbm > 0 ? row.TotalPacked * row.Spec.Cbm / containerCbm * 100 : 0;
                    int    rem      = row.Requested - row.TotalPacked;
                    bool   full     = rem <= 0;
                    SKColor remCol  = full ? C(0x16, 0xA3, 0x4A) : C(0xD9, 0x77, 0x06);

                    DrawCell($"{row.TotalPacked}/{row.Requested}", xPacked,  ry, 7.5f, C(0x33, 0x41, 0x55));
                    DrawCell($"{weight:F0} kg",                    xWeight,  ry, 7.5f, C(0x33, 0x41, 0x55));
                    DrawCell($"{cbmPct:F1}%",                      xCbm,     ry, 7.5f, C(0x33, 0x41, 0x55));
                    DrawCell($"{row.FullStacks}",                  xFull,    ry, 7.5f, C(0x33, 0x41, 0x55));
                    DrawCell($"{row.MixedPlaced}",                 xMixed,   ry, 7.5f, C(0x33, 0x41, 0x55));
                    DrawCell($"{row.CondoPlaced}",                 xCondo,   ry, 7.5f, C(0x33, 0x41, 0x55));
                    DrawCell($"{row.ScatterPlaced}",               xScatter, ry, 7.5f, C(0x33, 0x41, 0x55));
                    DrawCell(full ? "ครบ" : $"{rem} ลัง",          xRem,     ry, 7.5f, remCol);
                }
                else
                {
                    DrawCell("ไม่มี Pattern", xPacked, ry, 7.5f, C(0x94, 0xA3, 0xB8));
                }

                using var divP = Fill(C(0xE8, 0xEC, 0xF0));
                _canvas.DrawRect(MX, _y + rowH - 0.5f, ContentW, 0.5f, divP);
                _y += rowH;
            }

            // Totals row
            double totalWt  = statsRows.Sum(r => r.Spec.WeightPerBoxKg * r.TotalPacked);
            double totalCbm = containerCbm > 0 ? usedCbm / containerCbm * 100 : 0;

            using (var totBg = Fill(C(0xEF, 0xF2, 0xF7)))
                _canvas.DrawRect(MX, _y, ContentW, totalH, totBg);
            float ty = _y + 2;
            DrawCell("รวมทั้งหมด",        xName,   ty, 7.5f, C(0x1E, 0x29, 0x3B), bold: true);
            DrawCell($"{totalWt:F0} kg",  xWeight, ty, 7.5f, C(0x1E, 0x29, 0x3B), bold: true);
            DrawCell($"{totalCbm:F1}%",  xCbm,    ty, 7.5f, C(0x1E, 0x29, 0x3B), bold: true);
            _y += totalH + 10;
        }

        private void DrawColHeader(string text, float x, float y)
        {
            using var p = MkPaint(C(0x4B, 0x55, 0x63), 7.5f, bold: true);
            p.GetFontMetrics(out var m);
            _canvas.DrawText(text, x, y + (-m.Ascent), p);
        }

        private void DrawCell(string text, float x, float y, float size, SKColor color, bool bold = false)
        {
            using var p = MkPaint(color, size, bold);
            p.GetFontMetrics(out var m);
            _canvas.DrawText(text, x, y + (-m.Ascent), p);
        }
    }
}
