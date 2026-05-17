using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace logistic;

internal static class PdfExporter
{
    // ── Palette ───────────────────────────────────────────────────────────────
    private static readonly SKColor[] Palette =
    [
        new(0x3B, 0x82, 0xF6), new(0xEF, 0x44, 0x44), new(0x22, 0xC5, 0x5E),
        new(0xF5, 0x9E, 0x0B), new(0xA8, 0x55, 0xF7), new(0xEC, 0x48, 0x99),
        new(0x14, 0xB8, 0xA6), new(0xF9, 0x73, 0x16),
    ];

    // ── Page geometry ─────────────────────────────────────────────────────────
    private const float PageW    = 595f;
    private const float PageH    = 842f;
    private const float MX       = 42f;
    private const float MT       = 40f;
    private const float MB       = 40f;
    private const float ContentW = PageW - 2 * MX; // 511

    // ── Card geometry ─────────────────────────────────────────────────────────
    private const float CardPad   = 8f;
    private const float AccentBar = 4f;
    private const float TextOff   = AccentBar + 2 + CardPad;        // 16
    private const float InnerW    = ContentW - TextOff - CardPad;   // 485
    private const float DiagGap   = 12f;
    private const float DiagW     = 185f;  // clamped so cards fit 2-per-page

    // ── Entry point ───────────────────────────────────────────────────────────
    public static byte[] Generate(
        ContainerSpec container,
        IReadOnlyList<(ProductSpec Spec, int Qty)> requests,
        PackingOutput output)
    {
        var placements = output.Placements;
        var statsRows  = StatsCalculator.ComputeRows(
            output.PackInfos, placements, output.MixedMap, output.CondoMap, output.ScatterMap);

        double containerCbm = StatsCalculator.ContainerCbm(container);
        double usedCbm      = StatsCalculator.UsedCbm(placements);

        using var tf  = LoadTypeface();
        using var ms  = new MemoryStream();
        using var doc = SKDocument.CreatePdf(ms);

        new Renderer(doc, tf, container, placements.ToList(), statsRows, containerCbm, usedCbm)
            .Render();

        doc.Close();
        return ms.ToArray();
    }

    private static SKTypeface LoadTypeface()
    {
        string[] candidates =
        [
            "/Library/Fonts/Arial Unicode.ttf",
            "/Library/Fonts/Arial Unicode MS.ttf",
            "/System/Library/Fonts/Supplemental/Arial Unicode MS.ttf",
            "C:\\Windows\\Fonts\\arialuni.ttf",
        ];
        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            try { return SKTypeface.FromFile(path); } catch { /* try next */ }
        }
        return SKTypeface.Default;
    }

    // ── Iso projection (SkiaSharp-native, no Avalonia dependency) ─────────────
    private readonly struct PdfIsoProjection
    {
        private readonly float _cosA, _sinA, _cosE, _sinE;
        private readonly float _scale, _ox, _oy;

        internal float CosAzimuth => _cosA;
        internal float SinAzimuth => _sinA;

        internal PdfIsoProjection(
            double azimuth, double elevation, double zoom,
            double cW, double cL, double cH,
            float bw, float bh)
        {
            _cosA = (float)Math.Cos(azimuth);
            _sinA = (float)Math.Sin(azimuth);
            _cosE = (float)Math.Cos(elevation);
            _sinE = (float)Math.Sin(elevation);

            float minSx = float.MaxValue, maxSx = float.MinValue;
            float minSy = float.MaxValue, maxSy = float.MinValue;
            foreach (var (wx, wy, wz) in BoxCorners(cW, cL, cH))
            {
                var (sx, sy) = RawProject((float)wx, (float)wy, (float)wz, _cosA, _sinA, _cosE, _sinE);
                if (sx < minSx) minSx = sx; if (sx > maxSx) maxSx = sx;
                if (sy < minSy) minSy = sy; if (sy > maxSy) maxSy = sy;
            }
            float spanX = maxSx - minSx, spanY = maxSy - minSy;
            _scale = Math.Min(bw * 0.85f * (float)zoom / Math.Max(spanX, 1f),
                              bh * 0.85f * (float)zoom / Math.Max(spanY, 1f));
            _ox = bw / 2f - (minSx + maxSx) / 2f * _scale;
            _oy = bh / 2f - (minSy + maxSy) / 2f * _scale;
        }

        internal SKPoint Project(double x, double y, double z)
        {
            var (sx, sy) = RawProject((float)x, (float)y, (float)z, _cosA, _sinA, _cosE, _sinE);
            return new SKPoint(_ox + sx * _scale, _oy + sy * _scale);
        }

        private static (float sx, float sy) RawProject(
            float x, float y, float z,
            float cosA, float sinA, float cosE, float sinE)
        {
            float rx = x * cosA - y * sinA;
            float ry = x * sinA + y * cosA;
            return (rx, ry * cosE - z * sinE);
        }

        internal static (double, double, double)[] BoxCorners(double w, double l, double h) =>
        [
            (0,0,0),(w,0,0),(0,l,0),(w,l,0),
            (0,0,h),(w,0,h),(0,l,h),(w,l,h)
        ];
    }

    // ── Renderer ──────────────────────────────────────────────────────────────
    private sealed class Renderer(
        SKDocument doc,
        SKTypeface tf,
        ContainerSpec container,
        List<BoxPlacement> placements,
        List<StatsCalculator.PackStatRow> statsRows,
        double containerCbm,
        double usedCbm)
    {
        private SKCanvas _canvas = null!;
        private float    _y;
        private int      _page;

        // ── Orchestration ─────────────────────────────────────────────────────

        public void Render()
        {
            BeginPage();
            DrawPageHeader();
            DrawSummaryTable();
            DrawContainerViews();

            DrawLoadingSequenceHeader();

            var units = BuildLoadingUnits();
            for (int i = 0; i < units.Count; i++)
                DrawLoadingUnitCard(i + 1, units.Count, units[i]);

            // Appendix: Pattern A/B reference cards per product
            DrawAppendixHeader();
            foreach (var row in statsRows
                                 .Where(r => r.HasPattern)
                                 .OrderBy(r => r.ProductIndex))
                DrawProductCard(row);

            DrawFooter();
            doc.EndPage();
        }

        // ── Page management ───────────────────────────────────────────────────

        private void BeginPage()
        {
            _canvas = doc.BeginPage(PageW, PageH);
            _page++;
            _y = MT;
        }

        private void EnsureSpace(float needed)
        {
            if (_y + needed <= PageH - MB) return;
            if (_y <= MT + 10) return;
            DrawFooter();
            doc.EndPage();
            BeginPage();
        }

        private void DrawFooter()
        {
            string text = $"หน้า {_page}";
            using var p = MkPaint(C(0x94, 0xA3, 0xB8), 8);
            float tw = p.MeasureText(text);
            _canvas.DrawText(text, PageW / 2f - tw / 2f, PageH - 14f, p);
        }

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

        // ── Container views ───────────────────────────────────────────────────

        private void DrawContainerViews()
        {
            const float captionH = 14f;
            const float viewsH   = 190f;
            const float viewGap  = 12f;
            float viewW = (ContentW - viewGap) / 2f;

            EnsureSpace(captionH + viewsH + 12);

            float vx1 = MX;
            float vx2 = MX + viewW + viewGap;

            DrawText("มุมมอง 3D",       vx1, _y, 8f, C(0x64, 0x74, 0x8B));
            DrawText("มุมมองจากประตู",  vx2, _y, 8f, C(0x64, 0x74, 0x8B));
            _y += captionH;

            var isoRect   = new SKRect(vx1, _y, vx1 + viewW, _y + viewsH);
            var frontRect = new SKRect(vx2, _y, vx2 + viewW, _y + viewsH);

            // Backgrounds
            using (var bgP = Fill(C(0xF8, 0xFA, 0xFC)))
            {
                _canvas.DrawRoundRect(isoRect,   4, 4, bgP);
                _canvas.DrawRoundRect(frontRect, 4, 4, bgP);
            }
            using (var brdP = Stroke(C(0xCB, 0xD5, 0xE1), 0.8f))
            {
                _canvas.DrawRoundRect(isoRect,   4, 4, brdP);
                _canvas.DrawRoundRect(frontRect, 4, 4, brdP);
            }

            DrawIsoView(isoRect);
            DrawFrontView(frontRect);

            _y += viewsH + 12;
        }

        // ── 3D isometric view ─────────────────────────────────────────────────

        private void DrawIsoView(SKRect bounds)
        {
            double cW = container.InteriorW, cL = container.InteriorL, cH = container.InteriorH;
            var proj = new PdfIsoProjection(
                Math.PI / 4, Math.PI / 6, 1.0,
                cW, cL, cH, bounds.Width, bounds.Height);

            _canvas.Save();
            _canvas.ClipRect(bounds);

            SKPoint P(double x, double y, double z)
            {
                var p = proj.Project(x, y, z);
                return new SKPoint(bounds.Left + p.X, bounds.Top + p.Y);
            }

            // Container wireframe
            var p000 = P(0,0,0);   var p100 = P(cW,0,0);
            var p010 = P(0,cL,0);  var p110 = P(cW,cL,0);
            var p001 = P(0,0,cH);  var p101 = P(cW,0,cH);
            var p011 = P(0,cL,cH); var p111 = P(cW,cL,cH);

            void WireLine(SKPoint a, SKPoint b)
            {
                using var lp = Stroke(C(0x94, 0xA3, 0xB8), 0.8f);
                _canvas.DrawLine(a, b, lp);
            }

            WireLine(p000, p100); WireLine(p000, p010);
            WireLine(p100, p110); WireLine(p010, p110);
            WireLine(p000, p001); WireLine(p100, p101);
            WireLine(p010, p011); WireLine(p110, p111);
            WireLine(p001, p101); WireLine(p001, p011);
            WireLine(p101, p111); WireLine(p011, p111);

            if (placements.Count > 0)
            {
                // Painter sort: back-to-front along view direction
                float sinA = proj.SinAzimuth, cosA = proj.CosAzimuth;
                float sinE = (float)Math.Sin(Math.PI / 6);
                float cosE = (float)Math.Cos(Math.PI / 6);
                var sorted = placements
                    .OrderBy(b =>
                        (b.X + b.BW * 0.5) * sinA * sinE +
                        (b.Y + b.BL * 0.5) * cosA * sinE +
                        (b.Z + b.BH * 0.5) * cosE)
                    .ToList();

                foreach (var box in sorted)
                    DrawIsoBox(box, proj, bounds);
            }

            _canvas.Restore();
        }

        private void DrawIsoBox(BoxPlacement box, PdfIsoProjection proj, SKRect bounds)
        {
            SKColor baseColor = Palette[box.ProductIndex % Palette.Length];
            float cosA = proj.CosAzimuth, sinA = proj.SinAzimuth;

            SKPoint P(double x, double y, double z)
            {
                var p = proj.Project(x, y, z);
                return new SKPoint(bounds.Left + p.X, bounds.Top + p.Y);
            }

            double bx = box.X, by = box.Y, bz = box.Z;
            double bw = box.BW, bl = box.BL, bh = box.BH;

            var p000 = P(bx,    by,    bz);     var p100 = P(bx+bw, by,    bz);
            var p010 = P(bx,    by+bl, bz);     var p110 = P(bx+bw, by+bl, bz);
            var p001 = P(bx,    by,    bz+bh);  var p101 = P(bx+bw, by,    bz+bh);
            var p011 = P(bx,    by+bl, bz+bh);  var p111 = P(bx+bw, by+bl, bz+bh);

            // Silhouette fill (prevents painter-sort gaps)
            SKPoint[] sil;
            if      (sinA >= 0 && cosA >= 0) sil = [p001, p101, p100, p110, p010, p011];
            else if (sinA >= 0)              sil = [p001, p011, p111, p110, p100, p000];
            else if (cosA >= 0)              sil = [p001, p101, p111, p110, p010, p000];
            else                             sil = [p101, p111, p011, p010, p000, p100];
            FillPolyPath(sil, baseColor);

            // Top face
            FillPolyPath([p001, p101, p111, p011], baseColor);

            // Side faces — which faces are visible depends on azimuth quadrant
            if (cosA >= 0)
                FillPolyPath([p010, p110, p111, p011], DarkenColor(baseColor, 0.22f));
            else
                FillPolyPath([p000, p100, p101, p001], DarkenColor(baseColor, 0.22f));

            if (sinA >= 0)
                FillPolyPath([p100, p110, p111, p101], DarkenColor(baseColor, 0.40f));
            else
                FillPolyPath([p000, p010, p011, p001], DarkenColor(baseColor, 0.40f));

            // Visible edges
            SKColor edgeCol = DarkenColor(baseColor, 0.55f);
            DrawEdgeLine(p001, p101, edgeCol); DrawEdgeLine(p101, p111, edgeCol);
            DrawEdgeLine(p111, p011, edgeCol); DrawEdgeLine(p011, p001, edgeCol);
            if (cosA >= 0) { DrawEdgeLine(p010, p011, edgeCol); DrawEdgeLine(p110, p111, edgeCol); DrawEdgeLine(p010, p110, edgeCol); }
            else           { DrawEdgeLine(p000, p001, edgeCol); DrawEdgeLine(p100, p101, edgeCol); DrawEdgeLine(p000, p100, edgeCol); }
            if (sinA >= 0) { DrawEdgeLine(p100, p101, edgeCol); DrawEdgeLine(p110, p111, edgeCol); DrawEdgeLine(p100, p110, edgeCol); }
            else           { DrawEdgeLine(p000, p001, edgeCol); DrawEdgeLine(p010, p011, edgeCol); DrawEdgeLine(p000, p010, edgeCol); }
        }

        private void FillPolyPath(SKPoint[] pts, SKColor color)
        {
            using var path = new SKPath();
            path.MoveTo(pts[0]);
            for (int i = 1; i < pts.Length; i++) path.LineTo(pts[i]);
            path.Close();
            using var p = Fill(color);
            _canvas.DrawPath(path, p);
        }

        private void DrawEdgeLine(SKPoint a, SKPoint b, SKColor color)
        {
            using var p = Stroke(color, 0.5f);
            _canvas.DrawLine(a, b, p);
        }

        // ── Front view (door-facing orthographic) ─────────────────────────────

        private void DrawFrontView(SKRect bounds)
        {
            // Door (ประตู) is at Y=0; we look in the +Y direction.
            // Project (X, Z) onto the page — Z flipped so up is visually up.
            double cW = container.InteriorW, cH = container.InteriorH, cL = container.InteriorL;

            const float padding = 0.10f;
            float innerW  = bounds.Width  * (1f - 2f * padding);
            float innerH  = bounds.Height * (1f - 2f * padding);
            float scale   = Math.Min(innerW / (float)cW, innerH / (float)cH);
            float ox      = bounds.Left + (bounds.Width  - (float)cW * scale) / 2f;
            float oy      = bounds.Top  + (bounds.Height - (float)cH * scale) / 2f;

            // Container frame
            using (var frameP = Stroke(C(0x94, 0xA3, 0xB8), 1f))
                _canvas.DrawRect(ox, oy, (float)cW * scale, (float)cH * scale, frameP);

            if (placements.Count == 0) return;

            _canvas.Save();
            _canvas.ClipRect(new SKRect(ox, oy, ox + (float)cW * scale, oy + (float)cH * scale));

            // Sort by Y descending: far boxes first, door-side boxes on top
            var sorted = placements.OrderByDescending(b => b.Y).ToList();

            foreach (var box in sorted)
            {
                float depthT  = cL > 0 ? (float)(box.Y / cL) : 0f;
                SKColor baseC = Palette[box.ProductIndex % Palette.Length];
                SKColor drawC = DarkenColor(baseC, depthT * 0.15f);

                float rx = ox + (float)box.X        * scale;
                float ry = oy + (float)(cH - box.Z - box.BH) * scale;
                float rw = Math.Max((float)box.BW   * scale, 0.5f);
                float rh = Math.Max((float)box.BH   * scale, 0.5f);

                using (var fillP = Fill(drawC))
                    _canvas.DrawRect(rx, ry, rw, rh, fillP);
                using (var edgeP = Stroke(DarkenColor(drawC, 0.35f), 0.4f))
                    _canvas.DrawRect(rx, ry, rw, rh, edgeP);
            }

            _canvas.Restore();
        }

        // ── Loading sequence intro ────────────────────────────────────────────

        private void DrawLoadingSequenceHeader()
        {
            EnsureSpace(36);
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

        private float MeasureText(string text, float size, bool bold = false)
        {
            using var p = MkPaint(SKColors.Black, size, bold);
            return p.MeasureText(text);
        }

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

        // ── Drawing helpers ───────────────────────────────────────────────────

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

        private void DrawRoundRect(float x, float y, float w, float h, float r, SKPaint paint)
        {
            _canvas.DrawRoundRect(x, y, w, h, r, r, paint);
            paint.Dispose();
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
