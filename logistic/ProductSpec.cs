using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace logistic;

public record ProductSpec(
    string Description,
    string Content,
    string PackSize,
    double WeightPerBoxKg,
    bool BoxTypeRsc,
    bool BoxTypeAuto,
    double W,
    double L,
    double H)
{
    [JsonIgnore] public double Cbm => W * L * H / 1_000_000;

    public static readonly List<ProductSpec> All = [];

    private static readonly string FilePath = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "logistic", "products.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static void Load()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            var json = File.ReadAllText(FilePath);
            var specs = JsonSerializer.Deserialize<ProductSpec[]>(json, JsonOpts);
            if (specs is null || specs.Length == 0) return;
            All.Clear();
            All.AddRange(specs);
        }
        catch { /* keep defaults on corrupt file */ }
    }

    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(All, JsonOpts));
    }
}
