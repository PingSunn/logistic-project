using System;
using System.IO;

namespace CargoFit;

internal static class AppPaths
{
    // Repo root when running from dev environment; falls back to exe directory when deployed.
    internal static readonly string DataDir = FindDataDir();

    internal static readonly string ContainersFile = Path.Combine(DataDir, "containers.json");
    internal static readonly string ProductsFile   = Path.Combine(DataDir, "products.json");

    private static string FindDataDir()
    {
        // Dev: walk up to find repo root (.git folder)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }
        // Release: use a stable per-user directory that survives Velopack updates
        // Windows: %LocalAppData%\CargoFit\   macOS: ~/.local/share/CargoFit/
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CargoFit");
        Directory.CreateDirectory(appData);
        return appData;
    }
}
