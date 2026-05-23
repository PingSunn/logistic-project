using Avalonia.Media;

namespace CargoFit;

internal static class ThemeColors
{
    internal static readonly SolidColorBrush Surface     = new(Color.Parse("#FFFFFF"));
    internal static readonly SolidColorBrush SurfaceSub  = new(Color.Parse("#F8FAFC"));
    internal static readonly SolidColorBrush BorderLight = new(Color.Parse("#E2E8F0"));

    internal static readonly SolidColorBrush Ink      = new(Color.Parse("#1E293B"));
    internal static readonly SolidColorBrush InkMuted = new(Color.Parse("#64748B"));
    internal static readonly SolidColorBrush InkFaint = new(Color.Parse("#94A3B8"));

    internal static readonly SolidColorBrush AccentBg     = new(Color.Parse("#EFF6FF"));
    internal static readonly SolidColorBrush AccentBorder = new(Color.Parse("#93C5FD"));
    internal static readonly SolidColorBrush AccentText   = new(Color.Parse("#1D4ED8"));

    internal static readonly SolidColorBrush Success = new(Color.Parse("#16A34A"));
    internal static readonly SolidColorBrush Danger  = new(Color.Parse("#EF4444"));

    internal static readonly SolidColorBrush BoxNormal  = new(Color.Parse("#3B82F6"));
    internal static readonly SolidColorBrush BoxRotated = new(Color.Parse("#F97316"));
}
