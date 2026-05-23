using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace CargoFit;

internal static class CanvasLabelRenderer
{
    private const string SansSerif = "sans-serif";
    private const string MutedHex  = "#94A3B8";

    internal static void DrawLayerLabels(DrawingContext dc, List<BoxPlacement> clipped,
        double cL, IsometricProjection proj)
    {
        if (clipped.Count == 0) return;

        var levels = new SortedSet<double>();
        var layerHalfHeight = new Dictionary<double, double>();
        foreach (var b in clipped)
        {
            levels.Add(b.Z);
            if (!layerHalfHeight.TryGetValue(b.Z, out double h) || b.BH > h * 2)
                layerHalfHeight[b.Z] = b.BH * 0.5;
        }
        if (levels.Count < 2) return;

        var brush = new SolidColorBrush(Color.Parse("#64748B"));
        var tick  = new Pen(new SolidColorBrush(Color.Parse(MutedHex)), 0.8);

        int n = 1;
        Point? prev = null;
        foreach (double z in levels)
        {
            double halfH = layerHalfHeight.TryGetValue(z, out double hh) ? hh : 0;
            var p = proj.Project(0, cL, z + halfH);
            if (prev.HasValue && Math.Abs(prev.Value.Y - p.Y) < 10) { continue; }

            var ft = MakeText($"{n}", 10, brush);
            dc.DrawLine(tick, p, new Point(p.X + 8, p.Y));
            dc.DrawText(ft, new Point(p.X + 12, p.Y - ft.Height / 2));
            prev = p;
            n++;
        }
    }

    internal static void DrawStackWidthLabel(DrawingContext dc,
        IReadOnlyList<BoxPlacement> placements, IsometricProjection proj)
    {
        if (placements.Count == 0) return;

        var stacks = placements
            .GroupBy(p => (p.ProductIndex, p.StackIndex))
            .Select(g => (startY: g.Min(p => p.Y), endY: g.Max(p => p.Y + p.BL)))
            .OrderBy(s => s.startY)
            .ToList();

        var br  = new SolidColorBrush(Color.Parse("#475569"));
        var pen = new Pen(br, 2.0);

        foreach (var (startY, endY) in stacks)
        {
            var p0 = proj.Project(0, startY, 0);
            var p1 = proj.Project(0, endY,   0);

            dc.DrawLine(pen, p0, p1);
            dc.DrawLine(pen, new Point(p0.X - 10, p0.Y), new Point(p0.X + 3, p0.Y));
            dc.DrawLine(pen, new Point(p1.X - 10, p1.Y), new Point(p1.X + 3, p1.Y));

            var ft  = MakeText($"{(int)Math.Round(endY - startY)} ซม.", 10, br);
            var mid = new Point((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2);
            dc.DrawText(ft, new Point(mid.X - ft.Width - 20, mid.Y - ft.Height / 2));
        }
    }

    internal static void DrawDirectionLabels(DrawingContext dc,
        double cW, double cL, double cH, IsometricProjection proj)
    {
        void DrawBadge(string text, double wx, double wy, double wz, Color color)
        {
            var br = new SolidColorBrush(color);
            var ft = MakeText(text, 11, br, FontWeight.SemiBold);
            var p  = proj.Project(wx, wy, wz);
            var bg = new Rect(p.X - ft.Width / 2 - 5, p.Y - ft.Height / 2 - 3,
                              ft.Width + 10, ft.Height + 6);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                new Pen(br, 1), bg, 4, 4);
            dc.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y - ft.Height / 2));
        }

        DrawBadge("ประตู", cW / 2, 0,  cH / 2, Color.Parse("#0EA5E9"));
        DrawBadge("ในสุด", cW / 2, cL, cH / 2, Color.Parse("#F97316"));
    }

    internal static void DrawEdgeLabels(DrawingContext dc,
        double cW, double cL, double cH, IsometricProjection proj)
    {
        var brush = new SolidColorBrush(Color.Parse(MutedHex));

        var ft = MakeText($"{(int)cW} ซม.", 11, brush);
        var p  = proj.Project(cW / 2, 0, 0);
        dc.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y + 8));

        ft = MakeText($"{(int)cL} ซม.", 11, brush);
        p  = proj.Project(cW, cL / 2, 0);
        dc.DrawText(ft, new Point(p.X + 8, p.Y - ft.Height / 2));

        ft = MakeText($"{(int)cH} ซม.", 11, brush);
        p  = proj.Project(cW, 0, cH / 2);
        dc.DrawText(ft, new Point(p.X + 8, p.Y - ft.Height / 2));
    }

    internal static void DrawInfoCard(DrawingContext dc, ContainerSpec container)
    {
        var ft = MakeText($"{container.Name}  {container.SizeLabel}", 13,
            new SolidColorBrush(Color.Parse("#475569")), FontWeight.SemiBold);

        double x = 14, y = 12;
        dc.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
            new Pen(new SolidColorBrush(Color.Parse("#E2E8F0")), 1),
            new Rect(x - 6, y - 5, ft.Width + 12, ft.Height + 10),
            6, 6);
        dc.DrawText(ft, new Point(x, y));
    }

    internal static void DrawHint(DrawingContext dc, Rect bounds, string text)
    {
        var ft = MakeText(text, 16, new SolidColorBrush(Color.Parse(MutedHex)));
        dc.DrawText(ft, new Point((bounds.Width - ft.Width) / 2, (bounds.Height - ft.Height) / 2));
    }

    internal static void DrawHint2(DrawingContext dc, Rect bounds, string text)
    {
        var ft = MakeText(text, 12, new SolidColorBrush(Color.Parse("#CBD5E1")));
        dc.DrawText(ft, new Point((bounds.Width - ft.Width) / 2, bounds.Height / 2 + 28));
    }

    private static FormattedText MakeText(string text, double size, IBrush brush,
        FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(SansSerif, FontStyle.Normal, weight), size, brush);
}
