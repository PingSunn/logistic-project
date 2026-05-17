using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace logistic;

internal static partial class PdfExporter
{
    private sealed partial class Renderer
    {
        // ── Appendix header (before Pattern A/B reference cards) ──────────────

        private void DrawAppendixHeader()
        {
            EnsureSpace(34);
            DrawText("ภาคผนวก — รูปแบบการวางต่อชั้น (Pattern A / B)",
                     MX, _y, 12, C(0x1E, 0x29, 0x3B), bold: true);
            _y += 16;
            DrawText("ใช้สำหรับตรวจสอบรูปแบบการวางในแต่ละชั้นของสินค้าแต่ละชนิด",
                     MX, _y, 9, C(0x64, 0x74, 0x8B));
            _y += 14;
        }

        // ── Product card ──────────────────────────────────────────────────────

        private void DrawProductCard(StatsCalculator.PackStatRow row)
        {
            int pi = row.ProductIndex;
            var (patA, patB, _, _) = ExtractBoxes(pi);
            bool hasB = patB.Count > 0;

            // Compute layers per stack and stack count from primary placements
            int layersPerStack = 0, stackCount = 0;
            {
                var primary = placements
                    .Where(p => p.ProductIndex == pi && p.StackIndex < PackingEngine.CondoStackBase)
                    .ToList();
                if (primary.Count > 0)
                {
                    var byStack = primary.GroupBy(p => p.StackIndex).ToList();
                    stackCount     = byStack.Count;
                    layersPerStack = byStack.Max(g => g.Select(b => b.Z).Distinct().Count());
                }
            }

            float diagH = MaxDiagH(patA, patB, hasB);
            float cardH = CardHeight(diagH);

            EnsureSpace(cardH + 8);

            float cx = MX, cy = _y;
            SKColor col = Palette[pi % Palette.Length];

            // Card shell
            DrawRoundRect(cx, cy, ContentW, cardH, 7, Fill(SKColors.White));
            DrawRoundRect(cx, cy, ContentW, cardH, 7, Stroke(C(0xDC, 0xE0, 0xE6), 0.8f));
            DrawRoundRect(cx, cy, AccentBar, cardH, 3, Fill(col));

            float ix = cx + TextOff;
            float iy = cy + CardPad;

            // Header
            DrawRoundRect(ix, iy + 2, 11, 11, 2, Fill(col));
            DrawText($"{row.Spec.Description} {row.Spec.Content}", ix + 14, iy, 12, col, bold: true);
            DrawTextRight($"{row.Spec.W:F0}×{row.Spec.L:F0}×{row.Spec.H:F0} cm",
                          cx + ContentW - CardPad, iy + 2, 8.5f, C(0x94, 0xA3, 0xB8));
            iy += 18;

            // Divider
            iy += 3;
            using (var dp = Fill(C(0xE8, 0xEC, 0xF0)))
                _canvas.DrawRect(ix - 4, iy, InnerW + 4, 0.7f, dp);
            iy += 0.7f + 4;

            // Alternation note — include layer count when available
            string altNote = layersPerStack > 0
                ? $"{layersPerStack} ชั้น/ต๊ง  ·  {stackCount} ต๊ง  ·  สลับ Pattern A / B ทุกชั้น"
                : "ชั้นเลขคี่ → Pattern A  ·  ชั้นเลขคู่ → Pattern B  (สลับกัน)";
            DrawText(altNote, ix, iy, 8, C(0x1E, 0x29, 0x3B));
            iy += 11;

            // Pattern labels — show specific layer numbers when known
            string LabelOdd()
            {
                if (layersPerStack <= 0) return "ชั้นคี่  (Pattern A)";
                var odds = Enumerable.Range(1, layersPerStack).Where(n => n % 2 != 0).ToList();
                string nums = odds.Count <= 5
                    ? string.Join(", ", odds)
                    : $"{odds[0]}, {odds[1]}...{odds[odds.Count - 1]}";
                return $"ชั้น {nums}  →  A";
            }
            string LabelEven()
            {
                if (layersPerStack <= 0) return "ชั้นคู่  (Pattern B)";
                var evens = Enumerable.Range(1, layersPerStack).Where(n => n % 2 == 0).ToList();
                if (evens.Count == 0) return "ชั้นคู่  (Pattern B)";
                string nums = evens.Count <= 5
                    ? string.Join(", ", evens)
                    : $"{evens[0]}, {evens[1]}...{evens[evens.Count - 1]}";
                return $"ชั้น {nums}  →  B";
            }

            DrawText(LabelOdd(), ix, iy, 8, C(0x4B, 0x55, 0x63));
            DrawText(LabelEven(),
                     ix + DiagW + DiagGap, iy, 8,
                     hasB ? C(0x4B, 0x55, 0x63) : C(0xB4, 0xBC, 0xC8));
            iy += 11;

            // Diagrams
            DrawDiagram(patA, pi, ix, iy, DiagW, diagH);
            float bx2 = ix + DiagW + DiagGap;
            if (hasB)
                DrawDiagram(patB, pi, bx2, iy, DiagW, diagH);
            else
                DrawPlaceholder(bx2, iy, DiagW, diagH, "เหมือน Pattern A");
            iy += diagH + 3;

            // Count labels
            DrawText($"{patA.Count} กล่อง/ชั้น", ix,  iy, 8, C(0x94, 0xA3, 0xB8));
            if (hasB)
                DrawText($"{patB.Count} กล่อง/ชั้น", bx2, iy, 8, C(0x94, 0xA3, 0xB8));
            else
                DrawText("เหมือน Pattern A",           bx2, iy, 8, C(0xB4, 0xBC, 0xC8));
            iy += 10;

            // Divider above stats
            iy += 4;
            using (var dp = Fill(C(0xEC, 0xEE, 0xF2)))
                _canvas.DrawRect(ix - 4, iy, InnerW + 4, 0.5f, dp);
            iy += 0.5f + 4;

            // Stats — row 1: counts
            int remainder = row.Requested - row.TotalPacked;
            bool full     = remainder <= 0;

            float sx = ix;
            sx = DrawStat(sx, iy, "รวม", $"{row.TotalPacked}/{row.Requested} ลัง",
                          full ? C(0x16, 0xA3, 0x4A) : C(0xD9, 0x77, 0x06));
            sx += 9;
            sx = DrawStat(sx, iy, "เต็มต๊ง", $"{row.FullStacks}");
            if (row.MixedPlaced   > 0) { sx += 9; sx = DrawStat(sx, iy, "ผสม",    $"{row.MixedPlaced}"); }
            if (row.CondoPlaced   > 0) { sx += 9; sx = DrawStat(sx, iy, "คอนโด",  $"{row.CondoPlaced} กล่อง"); }
            if (row.ScatterPlaced > 0) { sx += 9; sx = DrawStat(sx, iy, "กระจาย", $"{row.ScatterPlaced} กล่อง"); }
            if (remainder         > 0) { sx += 9; DrawStat(sx, iy, "เหลือ",       $"{remainder} ลัง", C(0xD9, 0x77, 0x06)); }
            iy += 13;

            // Stats — row 2: weight / CBM %
            double weight = row.Spec.WeightPerBoxKg * row.TotalPacked;
            double cbmPct = containerCbm > 0 ? row.TotalPacked * row.Spec.Cbm / containerCbm * 100 : 0;
            sx = ix;
            sx = DrawStat(sx, iy, "น้ำหนัก", $"{weight:F1} kg");
            sx += 9;
            DrawStat(sx, iy, "CBM", $"{cbmPct:F1}% ของตู้");
            // iy += 13 — consumed in CardHeight; do not advance here

            _y += cardH + 8;
        }

        // ── Box data helpers ──────────────────────────────────────────────────

        private (List<BoxPlacement> PatA, List<BoxPlacement> PatB,
                 List<BoxPlacement> Condo, List<int> StackLayerCounts)
            ExtractBoxes(int pi)
        {
            var primary = placements
                .Where(p => p.ProductIndex == pi && p.StackIndex < PackingEngine.CondoStackBase)
                .ToList();
            var condo = placements
                .Where(p => p.ProductIndex == pi
                         && p.StackIndex >= PackingEngine.CondoStackBase
                         && p.StackIndex <  PackingEngine.MixedStackBase)
                .ToList();

            var evenFirst = primary
                .GroupBy(p => p.StackIndex).OrderBy(g => g.Key)
                .FirstOrDefault(g => g.Key % 2 == 0);
            var patA = evenFirst?.Where(b => b.LayerIndex == 0).ToList() ?? [];
            var patB = evenFirst?.Where(b => b.LayerIndex == 1).ToList() ?? [];

            var layerCounts = primary
                .GroupBy(p => p.StackIndex)
                .Select(g => g.Max(b => b.LayerIndex) + 1)
                .ToList();

            return (patA, patB, condo, layerCounts);
        }

        private float BoxDiagH(List<BoxPlacement> boxes, float width)
        {
            if (boxes.Count == 0) return 0;
            float rangeY = (float)(boxes.Max(b => b.Y + b.BL) - boxes.Min(b => b.Y));
            return rangeY / container.InteriorW * width;
        }

        private float MaxDiagH(List<BoxPlacement> patA, List<BoxPlacement> patB, bool hasB)
        {
            float hA = BoxDiagH(patA, DiagW);
            float hB = hasB ? BoxDiagH(patB, DiagW) : 0;
            return Math.Max(Math.Max(hA, hB), 16f);
        }

        private static float CardHeight(float diagH)
        {
            return CardPad           // top padding
                 + 18                // header row
                 + 3 + 0.7f + 4     // divider gap
                 + 11               // alternation note
                 + 11               // pattern labels
                 + diagH            // diagrams
                 + 3                // gap after diagrams
                 + 10               // count labels
                 + 4 + 0.5f + 4    // divider gap
                 + 13               // stats row 1
                 + 13               // stats row 2
                 + CardPad;         // bottom padding
        }

        // ── Pattern diagram drawing ───────────────────────────────────────────

        private void DrawDiagram(List<BoxPlacement> boxes, int pi,
                                  float x, float y, float w, float h)
        {
            DrawRoundRect(x, y, w, h, 3, Fill(C(0xF8, 0xFA, 0xFC)));
            DrawRoundRect(x, y, w, h, 3, Stroke(C(0xCB, 0xD5, 0xE1), 0.8f));
            if (boxes.Count == 0) return;

            float scale = w / container.InteriorW;
            float minY  = (float)boxes.Min(b => b.Y);
            SKColor col = Palette[pi % Palette.Length];

            foreach (var box in boxes)
            {
                float rx = x + (float)box.X * scale;
                float ry = y + ((float)box.Y - minY) * scale;
                float rw = Math.Max((float)box.BW * scale, 0.5f);
                float rl = Math.Max((float)box.BL * scale, 0.5f);

                byte alpha = box.Rotated ? (byte)150 : (byte)210;
                DrawRoundRect(rx, ry, rw, rl, 1.5f, Fill(col.WithAlpha(alpha)));
                DrawRoundRect(rx, ry, rw, rl, 1.5f,
                    Stroke(new SKColor((byte)(col.Red * 0.45), (byte)(col.Green * 0.45),
                                      (byte)(col.Blue * 0.45)), 0.6f));

                if (box.Rotated && rw > 12 && rl > 12)
                {
                    float fs = Math.Max(Math.Min(rw, rl) * 0.32f, 5f);
                    using var tp = MkPaint(SKColors.White.WithAlpha(210), fs);
                    tp.GetFontMetrics(out var m);
                    _canvas.DrawText("R", rx + rw * 0.25f, ry + rl * 0.65f + (-m.Ascent) - fs, tp);
                }
            }
        }

        private void DrawPlaceholder(float x, float y, float w, float h, string label)
        {
            DrawRoundRect(x, y, w, h, 3, Fill(C(0xF4, 0xF5, 0xF7)));
            DrawRoundRect(x, y, w, h, 3, Stroke(C(0xDC, 0xE0, 0xE4), 0.5f));
            using var tp = MkPaint(C(0xB4, 0xBC, 0xC8), 9);
            tp.GetFontMetrics(out var m);
            float tw = tp.MeasureText(label);
            _canvas.DrawText(label, x + (w - tw) / 2f, y + h / 2f + (-m.Ascent) / 2f, tp);
        }
    }
}
