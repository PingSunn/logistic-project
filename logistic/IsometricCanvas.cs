using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace logistic;

public record BoxPlacement(
    double X, double Y, double Z,
    double BW, double BL, double BH,
    int ProductIndex,
    bool Rotated = false);

public class IsometricCanvas : Control
{
    private const string SansSerif = "sans-serif";
    private const string MutedHex  = "#94A3B8";

    private static readonly Color[] Palette =
    [
        Color.Parse("#3B82F6"),
        Color.Parse("#EF4444"),
        Color.Parse("#22C55E"),
        Color.Parse("#F59E0B"),
        Color.Parse("#A855F7"),
        Color.Parse("#EC4899"),
        Color.Parse("#14B8A6"),
        Color.Parse("#F97316"),
    ];

    // Rotated-box variant: blend each palette color 60% toward violet (#6D28D9)
    private static readonly Color[] PaletteAlt = Array.ConvertAll(Palette, c => Color.FromRgb(
        (byte)Math.Clamp(c.R * 0.4 + 109 * 0.6, 0, 255),
        (byte)Math.Clamp(c.G * 0.4 +  40 * 0.6, 0, 255),
        (byte)Math.Clamp(c.B * 0.4 + 217 * 0.6, 0, 255)));

    public List<BoxPlacement> Placements { get; private set; } = [];
    public ContainerSpec? Container { get; private set; }

    public IsometricCanvas() { Cursor = new Cursor(StandardCursorType.Hand); }

    // ── Rotation state ────────────────────────────────────────────────────────
    private double _azimuth   = Math.PI / 4;
    private double _elevation = 0.50;
    private bool   _dragging;
    private Point  _dragStart;
    private double _azimuthAtDrag;
    private double _elevationAtDrag;

    // ── Layer cut ─────────────────────────────────────────────────────────────
    private double _cutRatio = 1.0; // 0–1: fraction of container height to show

    public void SetCutRatio(double ratio)
    {
        _cutRatio = Math.Clamp(ratio, 0, 1);
        InvalidateVisual();
    }

    public void SetData(ContainerSpec container, List<BoxPlacement> placements)
    {
        Container  = container;
        Placements = placements;
        InvalidateVisual();
    }

    // ── Pointer input ─────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _dragging          = true;
        _dragStart         = e.GetPosition(this);
        _azimuthAtDrag     = _azimuth;
        _elevationAtDrag   = _elevation;
        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_dragging) return;
        var pos = e.GetPosition(this);
        _azimuth   = _azimuthAtDrag + (pos.X - _dragStart.X) * 0.008;
        _elevation = Math.Clamp(_elevationAtDrag - (pos.Y - _dragStart.Y) * 0.006, 0.08, 1.45);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _azimuth += e.Delta.X * 0.08 - e.Delta.Y * 0.08;
        InvalidateVisual();
    }

    // ── Render ────────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width < 10 || bounds.Height < 10) return;

        if (Container is null)
        {
            DrawHint(context, bounds, "กำลังโหลด...");
            return;
        }

        double cW = Container.InteriorW;
        double cL = Container.InteriorL;
        double cH = Container.InteriorH;

        double cosA = Math.Cos(_azimuth);
        double sinA = Math.Sin(_azimuth);
        double cosE = Math.Cos(_elevation);
        double sinE = Math.Sin(_elevation);

        // Project a world point to screen using azimuth + elevation (unscaled, unshifted)
        static (double sx, double sy) Project(double x, double y, double z,
            double ca, double sa, double ce, double se)
        {
            double rx = x * ca - y * sa;
            double ry = x * sa + y * ca;
            return (rx, ry * ce - z * se);
        }

        // Compute bounding box of all 8 container corners to determine scale & origin
        double minSx = double.MaxValue, maxSx = double.MinValue;
        double minSy = double.MaxValue, maxSy = double.MinValue;
        foreach (var (wx, wy, wz) in Corners(cW, cL, cH))
        {
            var (sx, sy) = Project(wx, wy, wz, cosA, sinA, cosE, sinE);
            if (sx < minSx) minSx = sx;
            if (sx > maxSx) maxSx = sx;
            if (sy < minSy) minSy = sy;
            if (sy > maxSy) maxSy = sy;
        }

        double spanX = maxSx - minSx;
        double spanY = maxSy - minSy;
        double maxW  = bounds.Width  * 0.85;
        double maxH  = bounds.Height * 0.85;
        double scale = Math.Min(maxW / Math.Max(spanX, 1), maxH / Math.Max(spanY, 1));

        double midSx = (minSx + maxSx) / 2;
        double midSy = (minSy + maxSy) / 2;
        double originX = bounds.Width  / 2 - midSx * scale;
        double originY = bounds.Height / 2 - midSy * scale;

        Point Iso(double x, double y, double z)
        {
            var (sx, sy) = Project(x, y, z, cosA, sinA, cosE, sinE);
            return new Point(originX + sx * scale, originY + sy * scale);
        }

        double cutZ = _cutRatio * cH;

        DrawContainerWireframe(context, cW, cL, cH, Iso);
        DrawInfoCard(context);

        if (Placements.Count == 0)
        {
            DrawFloorFill(context, cW, cL, Iso);
            DrawEdgeLabels(context, cW, cL, cH, Iso);
            DrawHint(context, bounds, "เลือกสินค้าแล้วกด คำนวณ");
            DrawHint2(context, bounds, "ลากเพื่อหมุน  ·  Scroll เพื่อหมุนซ้าย-ขวา");
            return;
        }

        var clipped = ClipToCutPlane(cutZ);

        // Painter's sort: viewer direction = (sinA*sinE, cosA*sinE, cosE) — back-to-front
        clipped.Sort((a, b) =>
        {
            double da = (a.X + a.BW * 0.5) * sinA * sinE + (a.Y + a.BL * 0.5) * cosA * sinE + (a.Z + a.BH * 0.5) * cosE;
            double db = (b.X + b.BW * 0.5) * sinA * sinE + (b.Y + b.BL * 0.5) * cosA * sinE + (b.Z + b.BH * 0.5) * cosE;
            return da.CompareTo(db);
        });

        foreach (var box in clipped)
            DrawBox(context, box, Iso, cosA, sinA);

        DrawLayerLabels(context, clipped, Iso);

        if (_cutRatio < 0.999 && clipped.Count > 0)
            DrawCutPlane(context, cW, cL, cutZ, Iso);
    }

    private static (double, double, double)[] Corners(double w, double l, double h) =>
    [
        (0,0,0),(w,0,0),(0,l,0),(w,l,0),
        (0,0,h),(w,0,h),(0,l,h),(w,l,h)
    ];

    private List<BoxPlacement> ClipToCutPlane(double cutZ)
    {
        var result = new List<BoxPlacement>(Placements.Count);
        foreach (var b in Placements)
        {
            if (b.Z >= cutZ) continue;
            result.Add(b.Z + b.BH > cutZ ? b with { BH = cutZ - b.Z } : b);
        }
        return result;
    }

    // ── Drawing helpers ───────────────────────────────────────────────────────

    private static void DrawContainerWireframe(DrawingContext dc, double cW, double cL, double cH,
        Func<double, double, double, Point> iso)
    {
        var pen = new Pen(new SolidColorBrush(Color.Parse(MutedHex)), 1.5);

        var p000 = iso(0,   0,   0);   var p100 = iso(cW, 0,   0);
        var p010 = iso(0,   cL,  0);   var p110 = iso(cW, cL,  0);
        var p001 = iso(0,   0,   cH);  var p101 = iso(cW, 0,   cH);
        var p011 = iso(0,   cL,  cH);  var p111 = iso(cW, cL,  cH);

        dc.DrawLine(pen, p000, p100); dc.DrawLine(pen, p000, p010);
        dc.DrawLine(pen, p100, p110); dc.DrawLine(pen, p010, p110);
        dc.DrawLine(pen, p000, p001); dc.DrawLine(pen, p100, p101);
        dc.DrawLine(pen, p010, p011); dc.DrawLine(pen, p110, p111);
        dc.DrawLine(pen, p001, p101); dc.DrawLine(pen, p001, p011);
        dc.DrawLine(pen, p101, p111); dc.DrawLine(pen, p011, p111);
    }

    private static void DrawBox(DrawingContext dc, BoxPlacement box,
        Func<double, double, double, Point> iso, double cosAz, double sinAz)
    {
        var pal = box.Rotated ? PaletteAlt : Palette;
        var baseColor = pal[box.ProductIndex % pal.Length];
        double x = box.X, y = box.Y, z = box.Z;
        double w = box.BW, l = box.BL, h = box.BH;

        var p000 = iso(x,   y,   z);   var p100 = iso(x+w, y,   z);
        var p010 = iso(x,   y+l, z);   var p110 = iso(x+w, y+l, z);
        var p001 = iso(x,   y,   z+h); var p101 = iso(x+w, y,   z+h);
        var p011 = iso(x,   y+l, z+h); var p111 = iso(x+w, y+l, z+h);

        // Solid silhouette fill first — zero see-through regardless of painter-sort ties or AA gaps.
        // Silhouette is the 6-corner convex hull of the 3 visible faces (varies by viewer quadrant).
        Point[] sil;
        if      (sinAz >= 0 && cosAz >= 0) sil = [p001, p101, p100, p110, p010, p011]; // +X +Y
        else if (sinAz >= 0)               sil = [p001, p011, p111, p110, p100, p000]; // +X -Y
        else if (cosAz >= 0)               sil = [p001, p101, p111, p110, p010, p000]; // -X +Y
        else                               sil = [p101, p111, p011, p010, p000, p100]; // -X -Y
        FillFace(dc, sil, baseColor);

        // Shaded faces on top for 3-D depth
        FillFace(dc, [p001, p101, p111, p011], baseColor);

        if (cosAz >= 0)
            FillFace(dc, [p010, p110, p111, p011], Darken(baseColor, 0.22));
        else
            FillFace(dc, [p000, p100, p101, p001], Darken(baseColor, 0.22));

        if (sinAz >= 0)
            FillFace(dc, [p100, p110, p111, p101], Darken(baseColor, 0.40));
        else
            FillFace(dc, [p000, p010, p011, p001], Darken(baseColor, 0.40));

        var edge = new Pen(new SolidColorBrush(Darken(baseColor, 0.55)), 0.8);
        // Top edges (always same)
        dc.DrawLine(edge, p001, p101); dc.DrawLine(edge, p101, p111);
        dc.DrawLine(edge, p111, p011); dc.DrawLine(edge, p011, p001);
        // Y-side edges
        if (cosAz >= 0) { dc.DrawLine(edge, p010, p011); dc.DrawLine(edge, p110, p111); dc.DrawLine(edge, p010, p110); }
        else            { dc.DrawLine(edge, p000, p001); dc.DrawLine(edge, p100, p101); dc.DrawLine(edge, p000, p100); }
        // X-side edges
        if (sinAz >= 0) { dc.DrawLine(edge, p100, p101); dc.DrawLine(edge, p110, p111); dc.DrawLine(edge, p100, p110); }
        else            { dc.DrawLine(edge, p000, p001); dc.DrawLine(edge, p010, p011); dc.DrawLine(edge, p000, p010); }
    }

    private static void DrawCutPlane(DrawingContext dc, double cW, double cL, double cutZ,
        Func<double, double, double, Point> iso)
    {
        var pts = new[]
        {
            iso(0,  0,  cutZ), iso(cW, 0,  cutZ),
            iso(cW, cL, cutZ), iso(0,  cL, cutZ)
        };
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(pts[0], true);
            for (int i = 1; i < pts.Length; i++) ctx.LineTo(pts[i]);
            ctx.EndFigure(true);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(55, 59, 130, 246)), null, geo);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(180, 59, 130, 246)), 1.5)
        {
            DashStyle = new DashStyle([6, 4], 0)
        };
        dc.DrawLine(pen, pts[0], pts[1]);
        dc.DrawLine(pen, pts[1], pts[2]);
        dc.DrawLine(pen, pts[2], pts[3]);
        dc.DrawLine(pen, pts[3], pts[0]);
    }

    private static void DrawLayerLabels(DrawingContext dc, List<BoxPlacement> clipped,
        Func<double, double, double, Point> iso)
    {
        if (clipped.Count == 0) return;

        var levels = new System.Collections.Generic.SortedSet<double>();
        foreach (var b in clipped) levels.Add(b.Z);
        if (levels.Count < 2 || levels.Count > 20) return;

        var tf    = new Typeface(SansSerif);
        var brush = new SolidColorBrush(Color.Parse("#64748B"));
        var tick  = new Pen(new SolidColorBrush(Color.Parse(MutedHex)), 0.8);

        int n = 1;
        Point? prev = null;
        foreach (double z in levels)
        {
            var p = iso(0, 0, z);
            if (prev.HasValue && Math.Abs(prev.Value.Y - p.Y) < 12) { n++; continue; }

            var ft = new FormattedText($"ชั้นที่ {n}",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, tf, 10, brush);

            dc.DrawLine(tick, p, new Point(p.X - 8, p.Y));
            dc.DrawText(ft, new Point(p.X - ft.Width - 12, p.Y - ft.Height / 2));

            prev = p;
            n++;
        }
    }

    private static void FillFace(DrawingContext dc, Point[] pts, Color color)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(pts[0], true);
            for (int i = 1; i < pts.Length; i++) ctx.LineTo(pts[i]);
            ctx.EndFigure(true);
        }
        dc.DrawGeometry(new SolidColorBrush(color), null, geo);
    }

    private static void DrawFloorFill(DrawingContext dc, double cW, double cL,
        Func<double, double, double, Point> iso)
    {
        var pts = new[] { iso(0, 0, 0), iso(cW, 0, 0), iso(cW, cL, 0), iso(0, cL, 0) };
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(pts[0], true);
            for (int i = 1; i < pts.Length; i++) ctx.LineTo(pts[i]);
            ctx.EndFigure(true);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(18, 148, 163, 184)), null, geo);
    }

    private static void DrawEdgeLabels(DrawingContext dc, double cW, double cL, double cH,
        Func<double, double, double, Point> iso)
    {
        var tf    = new Typeface(SansSerif);
        var brush = new SolidColorBrush(Color.Parse(MutedHex));

        FormattedText Ft(string t) => new(t,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, tf, 11, brush);

        // Width — centered below front-bottom edge
        var ft = Ft($"{(int)cW} ซม.");
        var p  = iso(cW / 2, 0, 0);
        dc.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y + 8));

        // Length — right of right-bottom edge midpoint
        ft = Ft($"{(int)cL} ซม.");
        p  = iso(cW, cL / 2, 0);
        dc.DrawText(ft, new Point(p.X + 8, p.Y - ft.Height / 2));

        // Height — left of left-front vertical edge midpoint
        ft = Ft($"{(int)cH} ซม.");
        p  = iso(0, 0, cH / 2);
        dc.DrawText(ft, new Point(p.X - ft.Width - 8, p.Y - ft.Height / 2));
    }

    private void DrawInfoCard(DrawingContext dc)
    {
        if (Container is null) return;
        var ft = new FormattedText(
            $"{Container.Name}  {Container.SizeLabel}",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(SansSerif, FontStyle.Normal, FontWeight.SemiBold),
            13,
            new SolidColorBrush(Color.Parse("#475569")));

        double x = 14, y = 12;
        dc.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
            new Pen(new SolidColorBrush(Color.Parse("#E2E8F0")), 1),
            new Rect(x - 6, y - 5, ft.Width + 12, ft.Height + 10),
            6, 6);
        dc.DrawText(ft, new Point(x, y));
    }

    private static void DrawHint(DrawingContext dc, Rect bounds, string text)
    {
        var ft = new FormattedText(text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(SansSerif), 16,
            new SolidColorBrush(Color.Parse(MutedHex)));
        dc.DrawText(ft, new Point((bounds.Width - ft.Width) / 2, (bounds.Height - ft.Height) / 2));
    }

    private static void DrawHint2(DrawingContext dc, Rect bounds, string text)
    {
        var ft = new FormattedText(text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(SansSerif), 12,
            new SolidColorBrush(Color.Parse("#CBD5E1")));
        dc.DrawText(ft, new Point((bounds.Width - ft.Width) / 2, bounds.Height / 2 + 28));
    }

    private static Color Darken(Color c, double t) =>
        Color.FromRgb(Clamp(c.R * (1 - t)), Clamp(c.G * (1 - t)), Clamp(c.B * (1 - t)));

    private static byte Clamp(double v) => (byte)Math.Clamp(v, 0, 255);
}
