using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CargoFit;

// One horizontal strip of boxes within a section — all share the same rotation.
public record SectionSubRow(int Rows, int Cols, bool Rotated);

// One rectangular section within a layer, placed left-to-right with other sections.
// SubRows overrides Rows/Cols/Rotated when non-empty (multi-orientation section).
// Rows/Cols/Rotated are kept for backward-compatible JSON deserialization of legacy data.
public record LayerSection(int Rows, int Cols, bool Rotated, SectionSubRow[]? SubRows = null)
{
    public SectionSubRow[] GetSubRows() =>
        SubRows is { Length: > 0 }
            ? SubRows
            : [new SectionSubRow(Rows, Cols, Rotated)];
}

public record ProductSpec(
    string Description,
    string Content,
    string PackSize,
    double WeightPerBoxKg,
    double W,
    double L,
    double H,
    LayerSection[]? PatternA = null,
    LayerSection[]? PatternB = null,
    int MaxLayers = 0,   // layers per stack (0 = fill container height)
    int CondoCount = 0)  // boxes per condo row (0 = auto from container width)
{
    [JsonIgnore] public double Cbm => W * L * H / 1_000_000;

    public static readonly List<ProductSpec> Defaults =
    [
        new("Aloe",              "365 ML",  "Pack 24",  9.79, 21.9, 33.4, 20.5),
        new("Aloe",              "300 ML",  "Pack 24",  8.55, 24.6, 35.6, 21.1),
        new("Aloe",              "1000 ML", "Pack 12", 13.65, 25.1, 33.2, 29.6),
        new("Beauti drink",      "360 ML",  "Pack 24",  9.7,  24.0, 36.1, 21.5),
        new("Blue",              "500 ML",  "Pack 24", 13.58, 27.4, 41.4, 22.7),
        new("Cooler Bag",        "320 ML",  "Pack 24",  9.5,  27.4, 40.9, 17.1),
        new("Gumi",              "320 ML",  "Pack 24",  8.7,  25.8, 38.5, 16.3),
        new("Gumi Jelly",        "150 G",   "Pack 36",  5.58, 27.3, 31.1, 18.2),
        new("Gumi Jelly",        "135 G",   "Pack 36",  5.51, 27.3, 31.1, 18.2),
        new("Mogu",              "320 ML",  "Pack 6",   2.4,  25.8, 38.5, 15.7),
        new("Mogu",              "320 ML",  "Pack 12",  4.5,  25.8, 38.5, 15.7),
        new("Mogu",              "320 ML",  "Pack 24",  8.7,  25.8, 38.5, 15.7),
        new("Mogu Tea",          "300 ML",  "Pack 6",   8.0,  23.4, 35.0, 18.3),
        new("Mogu Tea",          "450 ML",  "Pack 24", 13.0,  25.8, 39.0, 20.3),
        new("Mogu",              "500 ML",  "Pack 12",  6.8,  28.6, 43.0, 19.6),
        new("Mogu",              "500 ML",  "Pack 24", 13.3,  28.6, 43.0, 19.6),
        new("Mogu",              "1000 ML", "Pack 6",   6.8,  17.8, 27.1, 28.8),
        new("Mogu",              "1000 ML", "Pack 12", 13.47, 26.2, 34.8, 26.7),
        new("Mogu",              "220 ML",  "Pack 24",  6.2,  23.1, 35.0, 13.8),
        new("Mogu",              "220 ML",  "Pack 12",  6.2,  22.7, 34.4, 13.8),
        new("Mogu Candy",        "19.5 G",  "Pack 48",  1.9,  24.6, 32.2,  9.2),
        new("Mogu Candy",        "30 G",    "Pack 48",  1.86, 24.6, 32.2,  9.2),
        new("Mogu Costco",       "320 ML",  "Pack 12",  4.44, 19.6, 26.1, 15.7),
        new("Mogu Cube",         "320 ML",  "Pack 24",  9.0,  25.8, 38.5, 16.3),
        new("Mogu Cut Case-Half Tray", "320 ML", "Pack 12", 4.41, 19.6, 26.1, 16.5),
        new("Mogu Ice",          "150 G",   "Pack 20",  3.6,  26.9, 23.7, 19.1),
        new("Mogu Ice",          "150 ML",  "Pack 36",  6.1,  27.5, 31.3, 19.6),
        new("Mogu Ice",          "150 ML",  "Pack 20",  3.6,  26.9, 23.7, 19.1),
        new("Mogu Ice",          "150 ML",  "Pack 12",  2.08, 14.1, 23.5, 18.4),
        new("Mogu Ice Burst",    "150 G",   "Pack 36",  6.12, 27.5, 31.3, 19.6),
        new("Mogu Ice Eskimo",   "150 G",   "Pack 12",  2.08, 14.1, 23.5, 18.4),
        new("Mogu Jelly",        "150 G",   "Pack 36",  6.05, 27.5, 31.3, 18.8),
        new("Mogu Jelly Korea",  "150 G",   "Pack 36",  6.4,  24.8, 44.0, 20.2),
    ];

    public static readonly List<ProductSpec> All = [];

    private static readonly string FilePath = AppPaths.ProductsFile;

    public static void Load()
    {
        if (!File.Exists(FilePath))
        {
            All.AddRange(Defaults);
            return;
        }
        try
        {
            var json = File.ReadAllText(FilePath);
            var specs = JsonSerializer.Deserialize<ProductSpec[]>(json, JsonOptions.WriteIndented);
            if (specs is null || specs.Length == 0) { All.AddRange(Defaults); return; }
            All.Clear();
            All.AddRange(specs);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ProductSpec.Load] {ex}"); All.AddRange(Defaults); }
    }

    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(All, JsonOptions.WriteIndented));
    }
}
