using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CargoFit;

/// <summary>
/// Represents a shipping container type.
/// Nominal = real physical size shown to the user.
/// Interior = usable space for calculation (nominal − Gap).
/// </summary>
public record ContainerSpec(
    string Name,
    string SizeLabel,
    int NominalW,
    int NominalL,
    int NominalH,
    int Gap = 5)
{
    [JsonIgnore] public int InteriorW => NominalW - Gap;
    [JsonIgnore] public int InteriorL => NominalL - Gap;
    [JsonIgnore] public int InteriorH => NominalH - Gap;

    public static readonly List<ContainerSpec> All =
    [
        new("ตู้สั้น",     "20 ft",    244, 600,  259, Gap: 5),
        new("ตู้ยาว",     "40 ft",    244, 1209, 260, Gap: 5),
        new("ตู้ไฮคิวบ์", "40 ft HC", 244, 1203, 290, Gap: 5),
    ];

    private static readonly string FilePath = AppPaths.ContainersFile;

    public static void Load()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            var json = File.ReadAllText(FilePath);
            var specs = JsonSerializer.Deserialize<ContainerSpec[]>(json, JsonOptions.WriteIndented);
            if (specs is null || specs.Length == 0) return;
            All.Clear();
            All.AddRange(specs);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ContainerSpec.Load] {ex}"); /* keep defaults */ }
    }

    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(All, JsonOptions.WriteIndented));
    }
}
