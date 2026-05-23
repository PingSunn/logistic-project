using System;
using Avalonia;

namespace CargoFit;

internal readonly struct IsometricProjection
{
    private readonly double _cosA, _sinA, _cosE, _sinE;
    private readonly double _scale, _originX, _originY;

    internal double CosAzimuth   => _cosA;
    internal double SinAzimuth   => _sinA;
    internal double CosElevation => _cosE;
    internal double SinElevation => _sinE;

    internal IsometricProjection(
        double azimuth, double elevation, double zoom,
        double cW, double cL, double cH,
        double boundsW, double boundsH)
    {
        _cosA = Math.Cos(azimuth);
        _sinA = Math.Sin(azimuth);
        _cosE = Math.Cos(elevation);
        _sinE = Math.Sin(elevation);

        double minSx = double.MaxValue, maxSx = double.MinValue;
        double minSy = double.MaxValue, maxSy = double.MinValue;
        foreach (var (wx, wy, wz) in GetCorners(cW, cL, cH))
        {
            var (sx, sy) = Raw(wx, wy, wz);
            if (sx < minSx) minSx = sx;
            if (sx > maxSx) maxSx = sx;
            if (sy < minSy) minSy = sy;
            if (sy > maxSy) maxSy = sy;
        }

        double spanX = maxSx - minSx;
        double spanY = maxSy - minSy;
        _scale = Math.Min(boundsW * 0.85 * zoom / Math.Max(spanX, 1),
                          boundsH * 0.85 * zoom / Math.Max(spanY, 1));
        _originX = boundsW / 2 - (minSx + maxSx) / 2 * _scale;
        _originY = boundsH / 2 - (minSy + maxSy) / 2 * _scale;
    }

    internal Point Project(double x, double y, double z)
    {
        var (sx, sy) = Raw(x, y, z);
        return new Point(_originX + sx * _scale, _originY + sy * _scale);
    }

    private (double sx, double sy) Raw(double x, double y, double z)
    {
        double rx = x * _cosA - y * _sinA;
        double ry = x * _sinA + y * _cosA;
        return (rx, ry * _cosE - z * _sinE);
    }

    internal static (double, double, double)[] GetCorners(double w, double l, double h) =>
    [
        (0,0,0),(w,0,0),(0,l,0),(w,l,0),
        (0,0,h),(w,0,h),(0,l,h),(w,l,h)
    ];
}
