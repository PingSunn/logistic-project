using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CargoFit;

public class IsometricCanvas : Control
{
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

    // ── Camera state ──────────────────────────────────────────────────────────
    private struct CameraState
    {
        public double Azimuth        = Math.PI / 4;
        public double Elevation      = 0.50;
        public double Zoom           = 1.0;
        public bool   Dragging;
        public Point  DragStart;
        public double AzimuthAtDrag;
        public double ElevationAtDrag;

        public CameraState() { }
    }
    private CameraState _cam = new();

    // ── Layer cut ─────────────────────────────────────────────────────────────
    private double _cutRatio = 1.0;

    // ── Canvas config ─────────────────────────────────────────────────────────
    private bool _wireframeMode     = false;
    private bool _colorByLayer      = false;
    private bool _colorByStackLayer = false;
    private bool _showDimensions    = true;
    private HashSet<int> _hiddenProducts = [];

    public void SetCutRatio(double ratio)               { _cutRatio          = Math.Clamp(ratio, 0, 1); InvalidateVisual(); }
    public void SetWireframeMode(bool v)                { _wireframeMode     = v; InvalidateVisual(); }
    public void SetColorByLayer(bool v)                 { _colorByLayer      = v; InvalidateVisual(); }
    public void SetColorByStackLayer(bool v)            { _colorByStackLayer = v; InvalidateVisual(); }
    public void SetShowDimensions(bool v)               { _showDimensions    = v; InvalidateVisual(); }
    public void SetHiddenProducts(HashSet<int> hidden)  { _hiddenProducts    = hidden; InvalidateVisual(); }

    public static Color GetProductColor(int productIndex) => Palette[productIndex % Palette.Length];

    public void ResetView()
    {
        _cam.Azimuth   = Math.PI / 4;
        _cam.Elevation = 0.50;
        _cam.Zoom      = 1.0;
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
        _cam.Dragging        = true;
        _cam.DragStart       = e.GetPosition(this);
        _cam.AzimuthAtDrag   = _cam.Azimuth;
        _cam.ElevationAtDrag = _cam.Elevation;
        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_cam.Dragging) return;
        var pos = e.GetPosition(this);
        _cam.Azimuth   = _cam.AzimuthAtDrag - (pos.X - _cam.DragStart.X) * 0.008;
        _cam.Elevation = Math.Clamp(_cam.ElevationAtDrag - (pos.Y - _cam.DragStart.Y) * 0.006, 0.08, 1.45);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _cam.Dragging = false;
        e.Pointer.Capture(null);
        Cursor = new Cursor(StandardCursorType.Hand);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _cam.Azimuth -= e.Delta.X * 0.08;
        _cam.Zoom     = Math.Clamp(_cam.Zoom * Math.Pow(1.12, e.Delta.Y), 0.2, 8.0);
        InvalidateVisual();
    }

    // ── Render ────────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width < 10 || bounds.Height < 10) return;

        context.FillRectangle(Brushes.Transparent, new Rect(0, 0, bounds.Width, bounds.Height));

        if (Container is null)
        {
            CanvasLabelRenderer.DrawHint(context, bounds, "กำลังโหลด...");
            return;
        }

        double cW = Container.NominalW;
        double cL = Container.NominalL;
        double cH = Container.NominalH;

        var proj  = new IsometricProjection(_cam.Azimuth, _cam.Elevation, _cam.Zoom,
                                             cW, cL, cH, bounds.Width, bounds.Height);
        double cutZ = _cutRatio * cH;

        DrawContainerWireframe(context, cW, cL, cH, proj);
        CanvasLabelRenderer.DrawDirectionLabels(context, cW, cL, cH, proj);
        CanvasLabelRenderer.DrawInfoCard(context, Container);

        if (Placements.Count == 0)
        {
            DrawFloorFill(context, cW, cL, proj);
            if (_showDimensions) CanvasLabelRenderer.DrawEdgeLabels(context, cW, cL, cH, proj);
            CanvasLabelRenderer.DrawHint(context, bounds, "เลือกสินค้าแล้วกด คำนวณ");
            CanvasLabelRenderer.DrawHint2(context, bounds, "ลากเพื่อหมุน/เอียง  ·  Scroll ขึ้น-ลง = ซูม  ·  Scroll ซ้าย-ขวา = หมุน");
            return;
        }

        var clipped = ClipToCutPlane(cutZ);

        if (_cam.Dragging && clipped.Count > 300)
        {
            DrawContainerWireframe(context, cW, cL, cH, proj);
            CanvasLabelRenderer.DrawInfoCard(context, Container);
            if (_showDimensions) CanvasLabelRenderer.DrawEdgeLabels(context, cW, cL, cH, proj);
            CanvasLabelRenderer.DrawHint(context, bounds, $"หมุนดู… ({clipped.Count} กล่อง)");
            return;
        }

        // Painter's sort: back-to-front along viewer direction
        double sinA = proj.SinAzimuth, cosA = proj.CosAzimuth, sinE = proj.SinElevation, cosE = proj.CosElevation;
        clipped.Sort((a, b) =>
        {
            double da = (a.X + a.BW * 0.5) * sinA * sinE + (a.Y + a.BL * 0.5) * cosA * sinE + (a.Z + a.BH * 0.5) * cosE;
            double db = (b.X + b.BW * 0.5) * sinA * sinE + (b.Y + b.BL * 0.5) * cosA * sinE + (b.Z + b.BH * 0.5) * cosE;
            return da.CompareTo(db);
        });

        // Shift boxes by Gap/2 so the gap is split evenly between both walls on each axis
        double gapHalf = Container.Gap / 2.0;
        if (gapHalf > 0.001)
            for (int i = 0; i < clipped.Count; i++)
            {
                var b = clipped[i];
                clipped[i] = b with { X = b.X + gapHalf, Y = b.Y + gapHalf };
            }

        Dictionary<double, int>? zLayerMap = null;
        if (_colorByLayer)
        {
            var zLevels = clipped.Select(b => b.Z).Distinct().OrderBy(z => z).ToList();
            zLayerMap = new Dictionary<double, int>();
            for (int i = 0; i < zLevels.Count; i++)
                zLayerMap[zLevels[i]] = i;
        }

        foreach (var box in clipped)
        {
            Color? overrideColor = null;
            if (_colorByLayer && zLayerMap is not null && zLayerMap.TryGetValue(box.Z, out int li))
                overrideColor = Palette[li % Palette.Length];
            else if (_colorByStackLayer)
                overrideColor = box.LayerIndex % 2 == 0
                    ? Palette[box.ProductIndex % Palette.Length]
                    : Lighten(Palette[box.ProductIndex % Palette.Length], 0.45);

            if (_wireframeMode)
                DrawBoxWireframe(context, box, proj, overrideColor ?? Palette[box.ProductIndex % Palette.Length]);
            else
                DrawBox(context, box, proj, overrideColor);
        }


        if (_showDimensions)
        {
            CanvasLabelRenderer.DrawEdgeLabels(context, cW, cL, cH, proj);
            var labPlacements = gapHalf > 0.001
                ? Placements.Select(p => p with { Y = p.Y + gapHalf }).ToList()
                : (IReadOnlyList<BoxPlacement>)Placements;
            CanvasLabelRenderer.DrawStackWidthLabel(context, labPlacements, proj);
        }

        if (_cutRatio < 0.999 && clipped.Count > 0)
            DrawCutPlane(context, cW, cL, cutZ, proj);
    }

    private List<BoxPlacement> ClipToCutPlane(double cutZ)
    {
        var result = new List<BoxPlacement>(Placements.Count);
        foreach (var b in Placements)
        {
            if (_hiddenProducts.Contains(b.ProductIndex)) continue;
            if (b.Z >= cutZ) continue;
            result.Add(b.Z + b.BH > cutZ ? b with { BH = cutZ - b.Z } : b);
        }
        return result;
    }

    // ── Drawing helpers ───────────────────────────────────────────────────────

    private static void DrawContainerWireframe(DrawingContext dc, double cW, double cL, double cH,
        IsometricProjection proj)
    {
        var pen = new Pen(new SolidColorBrush(Color.Parse("#94A3B8")), 1.5);

        var p000 = proj.Project(0,   0,   0);   var p100 = proj.Project(cW, 0,   0);
        var p010 = proj.Project(0,   cL,  0);   var p110 = proj.Project(cW, cL,  0);
        var p001 = proj.Project(0,   0,   cH);  var p101 = proj.Project(cW, 0,   cH);
        var p011 = proj.Project(0,   cL,  cH);  var p111 = proj.Project(cW, cL,  cH);

        dc.DrawLine(pen, p000, p100); dc.DrawLine(pen, p000, p010);
        dc.DrawLine(pen, p100, p110); dc.DrawLine(pen, p010, p110);
        dc.DrawLine(pen, p000, p001); dc.DrawLine(pen, p100, p101);
        dc.DrawLine(pen, p010, p011); dc.DrawLine(pen, p110, p111);
        dc.DrawLine(pen, p001, p101); dc.DrawLine(pen, p001, p011);
        dc.DrawLine(pen, p101, p111); dc.DrawLine(pen, p011, p111);
    }

    private static void DrawBoxWireframe(DrawingContext dc, BoxPlacement box,
        IsometricProjection proj, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.2);
        double x = box.X, y = box.Y, z = box.Z;
        double w = box.BW, l = box.BL, h = box.BH;

        var p000 = proj.Project(x,   y,   z);   var p100 = proj.Project(x+w, y,   z);
        var p010 = proj.Project(x,   y+l, z);   var p110 = proj.Project(x+w, y+l, z);
        var p001 = proj.Project(x,   y,   z+h); var p101 = proj.Project(x+w, y,   z+h);
        var p011 = proj.Project(x,   y+l, z+h); var p111 = proj.Project(x+w, y+l, z+h);

        dc.DrawLine(pen, p000, p100); dc.DrawLine(pen, p100, p110);
        dc.DrawLine(pen, p110, p010); dc.DrawLine(pen, p010, p000);
        dc.DrawLine(pen, p001, p101); dc.DrawLine(pen, p101, p111);
        dc.DrawLine(pen, p111, p011); dc.DrawLine(pen, p011, p001);
        dc.DrawLine(pen, p000, p001); dc.DrawLine(pen, p100, p101);
        dc.DrawLine(pen, p110, p111); dc.DrawLine(pen, p010, p011);
    }

    private static void DrawBox(DrawingContext dc, BoxPlacement box,
        IsometricProjection proj, Color? overrideColor = null)
    {
        var pal       = box.Rotated ? PaletteAlt : Palette;
        var baseColor = overrideColor ?? pal[box.ProductIndex % pal.Length];
        double cosAz  = proj.CosAzimuth, sinAz = proj.SinAzimuth;
        double x = box.X, y = box.Y, z = box.Z;
        double w = box.BW, l = box.BL, h = box.BH;

        var p000 = proj.Project(x,   y,   z);   var p100 = proj.Project(x+w, y,   z);
        var p010 = proj.Project(x,   y+l, z);   var p110 = proj.Project(x+w, y+l, z);
        var p001 = proj.Project(x,   y,   z+h); var p101 = proj.Project(x+w, y,   z+h);
        var p011 = proj.Project(x,   y+l, z+h); var p111 = proj.Project(x+w, y+l, z+h);

        // Solid silhouette fill — prevents gaps at painter-sort ties
        Point[] sil;
        if      (sinAz >= 0 && cosAz >= 0) sil = [p001, p101, p100, p110, p010, p011];
        else if (sinAz >= 0)               sil = [p001, p011, p111, p110, p100, p000];
        else if (cosAz >= 0)               sil = [p001, p101, p111, p110, p010, p000];
        else                               sil = [p101, p111, p011, p010, p000, p100];
        FillFace(dc, sil, baseColor);

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
        dc.DrawLine(edge, p001, p101); dc.DrawLine(edge, p101, p111);
        dc.DrawLine(edge, p111, p011); dc.DrawLine(edge, p011, p001);
        if (cosAz >= 0) { dc.DrawLine(edge, p010, p011); dc.DrawLine(edge, p110, p111); dc.DrawLine(edge, p010, p110); }
        else            { dc.DrawLine(edge, p000, p001); dc.DrawLine(edge, p100, p101); dc.DrawLine(edge, p000, p100); }
        if (sinAz >= 0) { dc.DrawLine(edge, p100, p101); dc.DrawLine(edge, p110, p111); dc.DrawLine(edge, p100, p110); }
        else            { dc.DrawLine(edge, p000, p001); dc.DrawLine(edge, p010, p011); dc.DrawLine(edge, p000, p010); }
    }

    private static void DrawCutPlane(DrawingContext dc, double cW, double cL, double cutZ,
        IsometricProjection proj)
    {
        var pts = new[]
        {
            proj.Project(0,  0,  cutZ), proj.Project(cW, 0,  cutZ),
            proj.Project(cW, cL, cutZ), proj.Project(0,  cL, cutZ)
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

    private static void DrawFloorFill(DrawingContext dc, double cW, double cL,
        IsometricProjection proj)
    {
        var pts = new[]
        {
            proj.Project(0,  0,  0), proj.Project(cW, 0,  0),
            proj.Project(cW, cL, 0), proj.Project(0,  cL, 0)
        };
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(pts[0], true);
            for (int i = 1; i < pts.Length; i++) ctx.LineTo(pts[i]);
            ctx.EndFigure(true);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(18, 148, 163, 184)), null, geo);
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

    private static Color Darken(Color c, double t) =>
        Color.FromRgb(Clamp(c.R * (1 - t)), Clamp(c.G * (1 - t)), Clamp(c.B * (1 - t)));

    private static Color Lighten(Color c, double t) =>
        Color.FromRgb(Clamp(c.R + (255 - c.R) * t), Clamp(c.G + (255 - c.G) * t), Clamp(c.B + (255 - c.B) * t));

    private static byte Clamp(double v) => (byte)Math.Clamp(v, 0, 255);
}
