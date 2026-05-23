using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace CargoFit;

internal static partial class PdfExporter
{
    private sealed partial class Renderer
    {
        // ── Loading sequence intro ────────────────────────────────────────────

        private void DrawLoadingSequenceHeader()
        {
            // Always start the loading sequence on its own page.
            if (_y > MT + 10)
            {
                DrawFooter();
                doc.EndPage();
                BeginPage();
            }
            DrawText("ลำดับการโหลดตู้ — เริ่มจากในสุดของตู้ไปประตู",
                     MX, _y, 12, C(0x1E, 0x29, 0x3B), bold: true);
            _y += 16;
            DrawText("แต่ละขั้นตอน: วางทีละชั้น (ล่าง→บน) จนเต็มก่อน แล้วค่อยทำขั้นต่อไป",
                     MX, _y, 9, C(0x64, 0x74, 0x8B));
            _y += 14;
        }

        // ── Loading unit data model ───────────────────────────────────────────

        private enum UnitKind { PrimaryStack, Condo }

        private sealed record LoadingUnit(
            UnitKind Kind,
            int? ProductIndex,                  // null for multi-product condo row
            int? StackIndex,                    // null for multi-product condo row
            int? StackOrdinal,
            int? StackTotalOfProduct,
            List<int> ProductIndices,           // distinct products in this unit
            double AvgY,
            double YMin,
            double YMax,
            double XMin,
            double XMax,
            int TotalBoxes,
            List<LayerEntry> Layers);

        private sealed record LayerEntry(
            int LayerNo,
            int Count,
            bool IsPartialRow,
            List<(int ProductIndex, int Count)> ByProduct);  // 1 entry for primary stack

        private List<LoadingUnit> BuildLoadingUnits()
        {
            var raw = new List<LoadingUnit>();

            // Primary stacks — group by (ProductIndex, StackIndex) since StackIndex
            // restarts at 0 for each product.
            foreach (var g in placements
                                 .Where(p => p.StackIndex < PackingEngine.CondoStackBase)
                                 .GroupBy(p => (p.ProductIndex, p.StackIndex)))
            {
                var boxes = g.ToList();
                int pi = g.Key.ProductIndex;
                int si = g.Key.StackIndex;

                var layers = boxes
                    .GroupBy(b => b.LayerIndex)
                    .OrderBy(lg => lg.Key)
                    .Select(lg => new LayerEntry(
                        LayerNo: lg.Key + 1,
                        Count: lg.Count(),
                        IsPartialRow: false,
                        ByProduct: new List<(int, int)> { (pi, lg.Count()) }))
                    .ToList();

                raw.Add(new LoadingUnit(
                    UnitKind.PrimaryStack,
                    ProductIndex: pi,
                    StackIndex: si,
                    StackOrdinal: null,
                    StackTotalOfProduct: null,
                    ProductIndices: new List<int> { pi },
                    AvgY: boxes.Average(b => b.Y + b.BL * 0.5),
                    YMin: boxes.Min(b => b.Y),
                    YMax: boxes.Max(b => b.Y + b.BL),
                    XMin: boxes.Min(b => b.X),
                    XMax: boxes.Max(b => b.X + b.BW),
                    TotalBoxes: boxes.Count,
                    Layers: layers));
            }

            // Condos — group by Y-row only (mix products in the same column).
            // Each Y-row gets one card with unified Z-layer numbering across products.
            foreach (var g in placements
                                 .Where(p => p.StackIndex >= PackingEngine.CondoStackBase
                                          && p.StackIndex <  PackingEngine.MixedStackBase)
                                 .GroupBy(p => Math.Round(p.Y, 1)))
            {
                var boxes = g.ToList();
                var productIndices = boxes
                    .Select(b => b.ProductIndex)
                    .Distinct()
                    .OrderBy(pi => pi)
                    .ToList();

                var distinctZ = boxes
                    .Select(b => Math.Round(b.Z, 1))
                    .Distinct()
                    .OrderBy(z => z)
                    .ToList();

                var layers = distinctZ.Select((z, idx) =>
                {
                    var layerBoxes = boxes.Where(b => Math.Abs(Math.Round(b.Z, 1) - z) < 0.01).ToList();
                    var byProduct = layerBoxes
                        .GroupBy(b => b.ProductIndex)
                        .OrderBy(grp => grp.Key)
                        .Select(grp => (ProductIndex: grp.Key, Count: grp.Count()))
                        .ToList();
                    // Phase 1 always places full single-product rows. Multiple products
                    // at the same Z means Phase 2 partial-row placement.
                    return new LayerEntry(
                        LayerNo: idx + 1,
                        Count: layerBoxes.Count,
                        IsPartialRow: byProduct.Count > 1,
                        ByProduct: byProduct);
                }).ToList();

                raw.Add(new LoadingUnit(
                    UnitKind.Condo,
                    ProductIndex: null,
                    StackIndex: null,
                    StackOrdinal: null,
                    StackTotalOfProduct: null,
                    ProductIndices: productIndices,
                    AvgY: boxes.Average(b => b.Y + b.BL * 0.5),
                    YMin: boxes.Min(b => b.Y),
                    YMax: boxes.Max(b => b.Y + b.BL),
                    XMin: boxes.Min(b => b.X),
                    XMax: boxes.Max(b => b.X + b.BW),
                    TotalBoxes: boxes.Count,
                    Layers: layers));
            }

            // Innermost first (highest avg Y)
            var sorted = raw.OrderByDescending(u => u.AvgY).ToList();

            // Assign per-product primary-stack ordinals (innermost = #1).
            var ordinalByKey = new Dictionary<(int Pi, int Si), (int Ord, int Total)>();
            foreach (var byProduct in sorted
                                        .Where(u => u.Kind == UnitKind.PrimaryStack)
                                        .GroupBy(u => u.ProductIndex!.Value))
            {
                var stacksInner = byProduct.OrderByDescending(u => u.AvgY).ToList();
                int total = stacksInner.Count;
                for (int i = 0; i < total; i++)
                    ordinalByKey[(stacksInner[i].ProductIndex!.Value, stacksInner[i].StackIndex!.Value)] = (i + 1, total);
            }

            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].Kind == UnitKind.PrimaryStack
                    && ordinalByKey.TryGetValue((sorted[i].ProductIndex!.Value, sorted[i].StackIndex!.Value), out var ot))
                {
                    sorted[i] = sorted[i] with
                    {
                        StackOrdinal = ot.Ord,
                        StackTotalOfProduct = ot.Total,
                    };
                }
            }

            return sorted;
        }

        // ── Loading-unit card ─────────────────────────────────────────────────

        private void DrawLoadingUnitCard(int stepNo, int totalSteps, LoadingUnit unit)
        {
            bool isCondo = unit.Kind == UnitKind.Condo;

            // Header content depends on unit kind.
            // Primary stack: single product → color the accent bar + product name in header.
            // Condo Y-row: multiple products → neutral accent + list product swatches.
            SKColor accentCol = isCondo
                ? C(0x64, 0x74, 0x8B)
                : Palette[unit.ProductIndex!.Value % Palette.Length];

            string typeLabel = isCondo
                ? "คอนโด"
                : $"ต๊งที่ {unit.StackOrdinal}/{unit.StackTotalOfProduct}";
            SKColor typeBg = isCondo ? C(0xF3, 0xE8, 0xFF) : C(0xDC, 0xFC, 0xE7);
            SKColor typeFg = isCondo ? C(0x7C, 0x3A, 0xED) : C(0x15, 0x80, 0x3D);

            // Two zoomed-in diagrams: just this unit's footprint (X×Y) and section (axis × Z).
            // Condo cards also get a 3D isometric view for spatial clarity.
            float diagW = InnerW;
            const float topH  = 100f;
            const float sideH = 140f;
            const float isoH  = 130f;

            // Auto-orient: if the unit is wider (X) than deep (Y), draw with X horizontal
            // so a wide-thin condo column reads naturally; otherwise keep Y (door→inner) horizontal.
            double xRange = unit.XMax - unit.XMin;
            double yRange = unit.YMax - unit.YMin;
            bool xHorizontal = xRange > yRange;

            var unitBoxes = placements.Where(BuildHighlightPredicate(unit)).ToList();

            // Layer rows: layout in 2 columns when many layers.
            int layerCount = unit.Layers.Count;
            int layerCols  = layerCount > 6 ? 2 : 1;
            int layerRows  = (int)Math.Ceiling(layerCount / (double)layerCols);
            const float layerRowH = 12f;
            float layerListH = layerRows * layerRowH;

            const float captionH = 10f;
            float cardH = CardPad
                        + 22                          // header
                        + 3 + 0.7f + 4               // divider
                        + captionH                    // "จากด้านบน" caption
                        + topH                        // top view
                        + 3                           // gap
                        + captionH                    // "จากด้านข้าง" caption
                        + sideH                       // side view
                        + (isCondo ? 3 + captionH + isoH : 0)  // iso view (condo only)
                        + 8                           // gap
                        + layerListH                  // layer rows
                        + 6                           // gap
                        + 11                          // footer
                        + CardPad;

            EnsureSpace(cardH + 8);

            float cx = MX, cy = _y;

            DrawRoundRect(cx, cy, ContentW, cardH, 7, Fill(SKColors.White));
            DrawRoundRect(cx, cy, ContentW, cardH, 7, Stroke(C(0xDC, 0xE0, 0xE6), 0.8f));
            DrawRoundRect(cx, cy, AccentBar, cardH, 3, Fill(accentCol));

            float ix = cx + TextOff;
            float iy = cy + CardPad;

            // ── Header row ─────────────────────────────────────────────────
            DrawText($"{stepNo}", ix, iy + 4, 14, C(0x1E, 0x29, 0x3B), bold: true);
            float stepW = MeasureText($"{stepNo}", 14, bold: true);

            // Type pill
            float pillX = ix + stepW + 10;
            float pillW = MeasureText(typeLabel, 9, bold: true) + 12;
            DrawRoundRect(pillX, iy + 5, pillW, 14, 7, Fill(typeBg));
            DrawText(typeLabel, pillX + 6, iy + 7, 9, typeFg, bold: true);

            if (isCondo)
            {
                // Multi-product condo: show product swatches + name list
                float swX = pillX + pillW + 10;
                foreach (var pi in unit.ProductIndices)
                {
                    SKColor c = Palette[pi % Palette.Length];
                    DrawRoundRect(swX, iy + 7, 10, 10, 2, Fill(c));
                    swX += 14;
                }
                DrawText("ผสม " + unit.ProductIndices.Count + " ชนิด", swX, iy + 5, 11,
                         C(0x33, 0x41, 0x55), bold: true);
            }
            else
            {
                int pi = unit.ProductIndex!.Value;
                var row = statsRows.FirstOrDefault(r => r.ProductIndex == pi);
                string productName = row != null
                    ? $"{row.Spec.Description} {row.Spec.Content}"
                    : $"#{pi}";
                float nameX = pillX + pillW + 10;
                DrawRoundRect(nameX, iy + 7, 10, 10, 2, Fill(accentCol));
                DrawText(productName, nameX + 14, iy + 5, 11, accentCol, bold: true);
            }

            // Right: total + position
            DrawTextRight($"{unit.TotalBoxes} กล่อง  ·  Y {unit.YMin:F0}–{unit.YMax:F0} cm",
                          cx + ContentW - CardPad, iy + 7, 8.5f, C(0x4B, 0x55, 0x63));
            iy += 22;

            // Divider
            iy += 3;
            using (var dp = Fill(C(0xE8, 0xEC, 0xF0)))
                _canvas.DrawRect(ix - 4, iy, InnerW + 4, 0.7f, dp);
            iy += 0.7f + 4;

            // Top view — only this unit, zoomed to its own footprint.
            string topCaption  = xHorizontal
                ? "จากด้านบน (กว้างตู้ ←→)"
                : "จากด้านบน (ประตู ←→ ในสุด)";
            string sideCaption = xHorizontal
                ? "จากด้านข้าง (มองจากประตู — เห็นทุกชั้น)"
                : "จากด้านข้าง (มองจากด้านข้างตู้ — เห็นทุกชั้น)";

            DrawText(topCaption, ix, iy, 7.5f, C(0x64, 0x74, 0x8B));
            iy += captionH;
            DrawUnitTopView(unitBoxes, ix, iy, diagW, topH, xHorizontal);
            iy += topH + 3;

            DrawText(sideCaption, ix, iy, 7.5f, C(0x64, 0x74, 0x8B));
            iy += captionH;
            DrawUnitSideView(unitBoxes, ix, iy, diagW, sideH, xHorizontal);
            iy += sideH;

            if (isCondo)
            {
                iy += 3;
                DrawText("มุมมองเฉียง (เห็นทุกชนิดที่ผสมในแถว)", ix, iy, 7.5f, C(0x64, 0x74, 0x8B));
                iy += captionH;
                DrawUnitIsoView(unitBoxes, ix, iy, diagW, isoH);
                iy += isoH;
            }

            iy += 8;

            // Per-layer list
            float colW = InnerW / layerCols;
            for (int i = 0; i < unit.Layers.Count; i++)
            {
                int rIdx = i / layerCols;
                int cIdx = i % layerCols;
                float ex = ix + cIdx * colW;
                float ey = iy + rIdx * layerRowH;
                DrawLayerRow(unit.Layers[i], isCondo, accentCol, ex, ey);
            }
            iy += layerListH + 6;

            DrawText(BuildUnitFooter(unit), ix, iy, 8, C(0x94, 0xA3, 0xB8));

            _y += cardH + 8;
        }

        private static Func<BoxPlacement, bool> BuildHighlightPredicate(LoadingUnit unit)
        {
            // For condo: highlight all condo boxes at this Y-row regardless of product.
            // For primary stack: highlight matching (ProductIndex, StackIndex) boxes.
            if (unit.Kind == UnitKind.Condo)
            {
                double targetY = Math.Round(unit.YMin, 1);
                return b => b.StackIndex >= PackingEngine.CondoStackBase
                         && b.StackIndex <  PackingEngine.MixedStackBase
                         && Math.Abs(Math.Round(b.Y, 1) - targetY) < 0.5;
            }
            int pi = unit.ProductIndex!.Value;
            int si = unit.StackIndex!.Value;
            return b => b.ProductIndex == pi && b.StackIndex == si;
        }

        private string BuildUnitFooter(LoadingUnit unit)
        {
            if (unit.Kind == UnitKind.Condo)
                return $"รวม {unit.TotalBoxes} กล่อง  ·  {unit.ProductIndices.Count} ชนิดผสมในแถวเดียว";

            int pi = unit.ProductIndex!.Value;
            var row = statsRows.FirstOrDefault(r => r.ProductIndex == pi);
            string dims = row != null
                ? $"{row.Spec.W:F0}×{row.Spec.L:F0}×{row.Spec.H:F0} cm"
                : "";
            return $"รวม {unit.TotalBoxes} กล่อง  ·  {dims}";
        }

        private void DrawLayerRow(LayerEntry layer, bool isCondo, SKColor accentCol,
                                   float ex, float ey)
        {
            string layerText = $"ชั้น {layer.LayerNo}";
            DrawText(layerText, ex, ey, 9, accentCol, bold: true);
            float tx = ex + MeasureText(layerText, 9, bold: true);

            string head = $"  ·  {layer.Count} ลัง" + (layer.ByProduct.Count > 1 ? ":  " : "");
            DrawText(head, tx, ey, 9, C(0x4B, 0x55, 0x63));
            tx += MeasureText(head, 9);

            if (isCondo && layer.ByProduct.Count > 1)
            {
                tx = DrawLayerProductBreakdown(layer.ByProduct, tx, ey);
            }
            else if (isCondo && layer.ByProduct.Count == 1)
            {
                tx = DrawLayerSingleProductTag(layer.ByProduct[0].ProductIndex, tx, ey);
            }

            if (layer.IsPartialRow)
                DrawText("  (แถวบางส่วน)", tx, ey, 9, C(0x94, 0xA3, 0xB8));
        }

        private float DrawLayerProductBreakdown(
            List<(int ProductIndex, int Count)> byProduct, float tx, float ey)
        {
            for (int k = 0; k < byProduct.Count; k++)
            {
                var (pi, count) = byProduct[k];
                SKColor col = Palette[pi % Palette.Length];
                DrawRoundRect(tx, ey + 2, 6, 6, 1, Fill(col));
                tx += 9;
                string txt = count.ToString() + (k < byProduct.Count - 1 ? "  +  " : "");
                DrawText(txt, tx, ey, 9, col, bold: true);
                tx += MeasureText(txt, 9, bold: true);
            }
            return tx;
        }

        private float DrawLayerSingleProductTag(int pi, float tx, float ey)
        {
            var row = statsRows.FirstOrDefault(r => r.ProductIndex == pi);
            if (row == null) return tx;
            SKColor col = Palette[pi % Palette.Length];
            tx += MeasureText("  ", 9);
            DrawRoundRect(tx, ey + 2, 6, 6, 1, Fill(col));
            tx += 9;
            string name = $"{row.Spec.Description} {row.Spec.Content}";
            DrawText(name, tx, ey, 9, col, bold: true);
            return tx + MeasureText(name, 9, bold: true);
        }

        // ── Unit cross-section diagrams (zoomed to the unit's own footprint) ──
        // Both views: horizontal axis = world X (if xHorizontal) OR world Y (door→inner).
        // Top view: vertical = the other planar axis.
        // Side view: vertical = world Z (flipped so ground sits at the bottom).

        private void DrawUnitTopView(List<BoxPlacement> unitBoxes,
                                      float x, float y, float maxW, float maxH,
                                      bool xHorizontal)
        {
            DrawRoundRect(x, y, maxW, maxH, 3, Fill(C(0xF8, 0xFA, 0xFC)));
            DrawRoundRect(x, y, maxW, maxH, 3, Stroke(C(0xCB, 0xD5, 0xE1), 0.8f));
            if (unitBoxes.Count == 0) return;

            double xMin = unitBoxes.Min(b => b.X);
            double xMax = unitBoxes.Max(b => b.X + b.BW);
            double yMin = unitBoxes.Min(b => b.Y);
            double yMax = unitBoxes.Max(b => b.Y + b.BL);
            double horizRange = Math.Max(xHorizontal ? xMax - xMin : yMax - yMin, 0.1);
            double vertRange  = Math.Max(xHorizontal ? yMax - yMin : xMax - xMin, 0.1);
            double horizMin   = xHorizontal ? xMin : yMin;
            double vertMin    = xHorizontal ? yMin : xMin;

            const float pad = 4f;
            float scale = (float)Math.Min((maxW - 2 * pad) / horizRange,
                                          (maxH - 2 * pad) / vertRange);
            float viewW = (float)(horizRange * scale);
            float viewH = (float)(vertRange * scale);
            float ox = x + (maxW - viewW) / 2f;
            float oy = y + (maxH - viewH) / 2f;

            foreach (var box in unitBoxes)
                DrawPlanarBox(box, ox, oy, scale, horizMin, vertMin, xHorizontal);
        }

        private void DrawUnitSideView(List<BoxPlacement> unitBoxes,
                                       float x, float y, float maxW, float maxH,
                                       bool xHorizontal)
        {
            DrawRoundRect(x, y, maxW, maxH, 3, Fill(C(0xF8, 0xFA, 0xFC)));
            DrawRoundRect(x, y, maxW, maxH, 3, Stroke(C(0xCB, 0xD5, 0xE1), 0.8f));
            if (unitBoxes.Count == 0) return;

            double xMin = unitBoxes.Min(b => b.X);
            double xMax = unitBoxes.Max(b => b.X + b.BW);
            double yMin = unitBoxes.Min(b => b.Y);
            double yMax = unitBoxes.Max(b => b.Y + b.BL);
            double zMax = unitBoxes.Max(b => b.Z + b.BH);
            double horizRange = Math.Max(xHorizontal ? xMax - xMin : yMax - yMin, 0.1);
            double zRange     = Math.Max(zMax, 0.1);
            double horizMin   = xHorizontal ? xMin : yMin;

            const float pad = 4f;
            float scale = (float)Math.Min((maxW - 2 * pad) / horizRange,
                                          (maxH - 2 * pad) / zRange);
            float viewW = (float)(horizRange * scale);
            float viewH = (float)(zRange * scale);
            float ox = x + (maxW - viewW) / 2f;
            float oy = y + (maxH - viewH) / 2f;

            // Painter: sort by perpendicular planar axis so closer boxes overlay farther ones.
            var ordered = (xHorizontal
                ? unitBoxes.OrderBy(b => b.Y)
                : unitBoxes.OrderBy(b => b.X)).ToList();

            foreach (var box in ordered)
            {
                double bHoriz     = xHorizontal ? box.X : box.Y;
                double bHorizSize = xHorizontal ? box.BW : box.BL;
                float rx = ox + (float)((bHoriz - horizMin) * scale);
                float ry = oy + (float)((zRange - box.Z - box.BH) * scale);
                float rw = Math.Max((float)(bHorizSize * scale), 0.5f);
                float rh = Math.Max((float)box.BH * scale, 0.5f);
                DrawBoxRect(box, rx, ry, rw, rh);
            }
        }

        private void DrawUnitIsoView(List<BoxPlacement> unitBoxes,
                                      float x, float y, float maxW, float maxH)
        {
            DrawRoundRect(x, y, maxW, maxH, 3, Fill(C(0xF8, 0xFA, 0xFC)));
            DrawRoundRect(x, y, maxW, maxH, 3, Stroke(C(0xCB, 0xD5, 0xE1), 0.8f));
            if (unitBoxes.Count == 0) return;

            double xMin = unitBoxes.Min(b => b.X);
            double xMax = unitBoxes.Max(b => b.X + b.BW);
            double yMin = unitBoxes.Min(b => b.Y);
            double yMax = unitBoxes.Max(b => b.Y + b.BL);
            double zMin = unitBoxes.Min(b => b.Z);
            double zMax = unitBoxes.Max(b => b.Z + b.BH);

            double localW = Math.Max(xMax - xMin, 0.1);
            double localL = Math.Max(yMax - yMin, 0.1);
            double localH = Math.Max(zMax - zMin, 0.1);

            // Camera angle: higher elevation = camera lower in space (more side-on view),
            // lower elevation = closer to top-down. Full container uses π/6 (30°); condos
            // get π/4 (45°) for a more side-on read that shows box stacking clearly.
            const double azimuth   = Math.PI / 4;
            const double elevation = Math.PI / 4;

            var proj = new PdfIsoProjection(
                azimuth, elevation, 1.0,
                localW, localL, localH, maxW, maxH);

            var bounds = new SKRect(x, y, x + maxW, y + maxH);
            _canvas.Save();
            _canvas.ClipRect(bounds);

            // Painter sort: back-to-front along view direction (matches DrawIsoView).
            float sinA = proj.SinAzimuth, cosA = proj.CosAzimuth;
            float sinE = (float)Math.Sin(elevation);
            float cosE = (float)Math.Cos(elevation);
            var sorted = unitBoxes
                .OrderBy(b =>
                    (b.X + b.BW * 0.5) * sinA * sinE +
                    (b.Y + b.BL * 0.5) * cosA * sinE +
                    (b.Z + b.BH * 0.5) * cosE)
                .ToList();

            // Translate each box to local origin so the projection's fit-to-frame matches the unit bbox.
            foreach (var box in sorted)
            {
                var shifted = box with
                {
                    X = box.X - xMin,
                    Y = box.Y - yMin,
                    Z = box.Z - zMin
                };
                DrawIsoBox(shifted, proj, bounds);
            }

            _canvas.Restore();
        }

        private void DrawPlanarBox(BoxPlacement box, float ox, float oy, float scale,
                                    double horizMin, double vertMin, bool xHorizontal)
        {
            double bHoriz     = xHorizontal ? box.X : box.Y;
            double bHorizSize = xHorizontal ? box.BW : box.BL;
            double bVert      = xHorizontal ? box.Y : box.X;
            double bVertSize  = xHorizontal ? box.BL : box.BW;
            float rx = ox + (float)((bHoriz - horizMin) * scale);
            float ry = oy + (float)((bVert - vertMin) * scale);
            float rw = Math.Max((float)(bHorizSize * scale), 0.5f);
            float rh = Math.Max((float)(bVertSize * scale), 0.5f);
            DrawBoxRect(box, rx, ry, rw, rh);
        }

        private void DrawBoxRect(BoxPlacement box, float rx, float ry, float rw, float rh)
        {
            SKColor col = Palette[box.ProductIndex % Palette.Length];
            byte alpha  = box.Rotated ? (byte)170 : (byte)225;
            DrawRoundRect(rx, ry, rw, rh, 1.2f, Fill(col.WithAlpha(alpha)));
            DrawRoundRect(rx, ry, rw, rh, 1.2f,
                Stroke(new SKColor((byte)(col.Red * 0.45), (byte)(col.Green * 0.45),
                                   (byte)(col.Blue * 0.45)), 0.5f));
        }
    }
}
