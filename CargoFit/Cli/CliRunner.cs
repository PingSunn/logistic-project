using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CargoFit.Cli;

/// <summary>
/// Headless CLI runner — เรียกเมื่อ args มี --input
/// Usage: dotnet run --project CargoFit/CargoFit.csproj -- --input testdata/devpreset.json
/// หรือ:  make run-test FILE=testdata/devpreset.json
/// </summary>
internal static class CliRunner
{
    internal static int Run(string[] args)
    {
        // --- Parse --input <path> ---
        int idx = Array.IndexOf(args, "--input");
        if (idx < 0 || idx + 1 >= args.Length)
        {
            Console.Error.WriteLine("Usage: --input <path-to-fixture.json>");
            return 1;
        }

        var inputPath = args[idx + 1];
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"[CliRunner] ไม่พบไฟล์: {inputPath}");
            return 1;
        }

        // --- Load fixture ---
        InputFixture fixture;
        try
        {
            var json = File.ReadAllText(inputPath);
            fixture = JsonSerializer.Deserialize<InputFixture>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Deserialize คืน null");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CliRunner] อ่าน fixture ไม่ได้: {ex.Message}");
            return 1;
        }

        // --- Load containers + products (same path as GUI) ---
        ContainerSpec.Load();
        ProductSpec.Load();

        if (fixture.Container < 0 || fixture.Container >= ContainerSpec.All.Count)
        {
            Console.Error.WriteLine($"[CliRunner] container index {fixture.Container} out of range (0-{ContainerSpec.All.Count - 1})");
            return 1;
        }

        var container = ContainerSpec.All[fixture.Container];

        var requests = new List<(ProductSpec Spec, int Qty)>();
        foreach (var fp in fixture.Products)
        {
            if (fp.Index < 0 || fp.Index >= ProductSpec.All.Count)
            {
                Console.Error.WriteLine($"[CliRunner] product index {fp.Index} out of range");
                return 1;
            }
            requests.Add((ProductSpec.All[fp.Index], fp.Qty));
        }

        // --- Enable debug log (always in CLI mode) ---
        // Log file: packing-debug.log ใน directory เดียวกับ input file
        var logPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".",
            "packing-debug.log");
        PackingLog.Init(logPath: logPath, force: true);

        Console.WriteLine($"CargoFit CLI — {container.Name} {container.SizeLabel}  ({container.InteriorW}×{container.InteriorL}×{container.InteriorH} cm)");
        Console.WriteLine($"Input   : {inputPath}");
        Console.WriteLine($"Log     : {logPath}");
        Console.WriteLine();

        // --- Run packing ---
        var output = PackingEngine.Calculate(container, requests);
        PackingLog.Finish();

        // --- Print summary ---
        DumpToConsole(container, requests, output);
        return 0;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Summary dump (port ของ TestHelpers.DumpOutput ที่ใช้ Console แทน xUnit log)
    // ──────────────────────────────────────────────────────────────────────────
    private static void DumpToConsole(
        ContainerSpec container,
        IReadOnlyList<(ProductSpec Spec, int Qty)> requests,
        PackingOutput output)
    {
        double iw = container.InteriorW;
        double il = container.InteriorL;
        double ih = container.InteriorH;

        double containerCbm = iw * il * ih / 1_000_000.0;
        double usedCbm      = output.Placements.Sum(p => p.BW * p.BL * p.BH / 1_000_000.0);
        double pct          = containerCbm > 0 ? usedCbm / containerCbm * 100.0 : 0;

        Console.WriteLine($"=== {container.Name} {container.SizeLabel}  ({iw}×{il}×{ih} cm interior) ===");
        Console.WriteLine($"    placements: {output.Placements.Count}  CBM: {usedCbm:F3}/{containerCbm:F3} ({pct:F1}%)");
        Console.WriteLine();

        Console.WriteLine("  #  Product                            Req  Packed  Primary  Condo  Scatter  Pattern");
        Console.WriteLine("  " + new string('-', 81));

        foreach (var info in output.PackInfos)
        {
            var s       = info.Spec;
            int pri     = output.Placements.Count(p =>
                              p.ProductIndex == info.ProductIndex &&
                              p.StackIndex   <  PackingEngine.CondoStackBase);
            int condo   = output.CondoMap.GetValueOrDefault(info.ProductIndex, 0);
            int scatter = output.ScatterMap.GetValueOrDefault(info.ProductIndex, 0);
            string name = $"{s.Description} {s.Content} {s.PackSize}";

            Console.WriteLine($"  {info.ProductIndex,2}  {name,-34} {info.Requested,4}" +
                              $"  {info.Result.Packed,6}  {pri,7}  {condo,5}  {scatter,7}  {(info.HasPattern ? "yes" : "no")}");
        }

        Console.WriteLine();

        if (output.Placements.Count > 0)
        {
            double maxX = output.Placements.Max(p => p.X + p.BW);
            double maxY = output.Placements.Max(p => p.Y + p.BL);
            double maxZ = output.Placements.Max(p => p.Z + p.BH);
            Console.WriteLine($"  Bounding box: X=[0,{maxX:F1}]  Y=[0,{maxY:F1}]  Z=[0,{maxZ:F1}]");
            Console.WriteLine($"  Container   : X=[0,{iw}]  Y=[0,{il}]  Z=[0,{ih}]");
        }

        Console.WriteLine();
    }
}
