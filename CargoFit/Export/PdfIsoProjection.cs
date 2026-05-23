using System;
using SkiaSharp;

namespace CargoFit;

internal readonly struct PdfIsoProjection
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
