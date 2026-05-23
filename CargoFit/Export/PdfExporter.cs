using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace CargoFit;

internal static partial class PdfExporter
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

    // ── Renderer ──────────────────────────────────────────────────────────────
    // Stateful PDF renderer. Methods are split across partial-class files by
    // responsibility (Header, ContainerViews, LoadingSequence, ProductCard,
    // Drawing). This file owns construction, shared state, and orchestration.
    private sealed partial class Renderer(
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
    }
}
