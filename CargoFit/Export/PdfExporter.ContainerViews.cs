using System;
using System.Linq;
using SkiaSharp;

namespace CargoFit;

internal static partial class PdfExporter
{
    private sealed partial class Renderer
    {
        // ── Container views (cover-page iso + door-facing) ────────────────────

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
            // Back wall (ในสุด) is at Y=0; door (ประตู) is at Y=cL; we look in the -Y direction.
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

            // Sort by Y ascending: far boxes (Y=0, back wall) first, door-side boxes (high Y) on top
            var sorted = placements.OrderBy(b => b.Y).ToList();

            foreach (var box in sorted)
            {
                float depthT  = cL > 0 ? (float)(1.0f - box.Y / cL) : 0f;  // 1 at back wall (Y=0), 0 at door (Y=cL)
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
    }
}
