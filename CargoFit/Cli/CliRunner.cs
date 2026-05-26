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
        var logPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".",
            "packing-debug.log");
        PackingLog.Init(logPath: logPath, force: true);

        // --- Run packing ---
        var output = PackingEngine.Calculate(container, requests);
        PackingLog.Finish();

        // --- Print detailed report ---
        DumpToConsole(container, requests, output, inputPath, logPath);
        return 0;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Debug report — แสดงข้อมูลละเอียดสำหรับ debug algorithm
    //
    //  Sections:
    //    1. Header + overall summary
    //    2. Products summary table (columns ตรงกับ GUI)
    //    3. Stack detail per product (StackIndex + LayerIndex breakdown)
    //    4. Geometry bounds check
    // ══════════════════════════════════════════════════════════════════════════
    private static void DumpToConsole(
        ContainerSpec container,
        IReadOnlyList<(ProductSpec Spec, int Qty)> requests,
        PackingOutput output,
        string inputPath,
        string logPath)
    {
        const int W = 80;
        string Bar(char c = '─') => new string(c, W);

        double iw = container.InteriorW;
        double il = container.InteriorL;
        double ih = container.InteriorH;
        double containerCbm = iw * il * ih / 1_000_000.0;
        double usedCbm      = output.Placements.Sum(p => p.BW * p.BL * p.BH / 1_000_000.0);
        double pct          = containerCbm > 0 ? usedCbm / containerCbm * 100.0 : 0;

        // ── 1. Header ────────────────────────────────────────────────────────
        Console.WriteLine(Bar('═'));
        Console.WriteLine($" CargoFit CLI Debug Report");
        Console.WriteLine($" Container : {container.Name} {container.SizeLabel}  ({iw}×{il}×{ih} cm interior)");
        Console.WriteLine($" Input     : {inputPath}");
        Console.WriteLine($" Log       : {logPath}");
        Console.WriteLine(Bar('═'));
        Console.WriteLine();

        // ── Overall numbers ──────────────────────────────────────────────────
        Console.WriteLine($" Placements  : {output.Placements.Count} boxes");
        Console.WriteLine($" Used CBM    : {usedCbm:F3} / {containerCbm:F3} m³  ({pct:F1}%)");

        if (output.Placements.Count > 0)
        {
            double maxX = output.Placements.Max(p => p.X + p.BW);
            double maxY = output.Placements.Max(p => p.Y + p.BL);
            double maxZ = output.Placements.Max(p => p.Z + p.BH);
            Console.WriteLine($" Bounding box: X=[0.0, {maxX,7:F1}]  Y=[0.0, {maxY,7:F1}]  Z=[0.0, {maxZ,7:F1}]");
        }
        Console.WriteLine($" Container   : X=[0,   {iw,7:F1}]  Y=[0,   {il,7:F1}]  Z=[0,   {ih,7:F1}]");
        Console.WriteLine();

        // ── 2. Products summary ───────────────────────────────────────────────
        Console.WriteLine($"── PRODUCTS SUMMARY {Bar().Substring(19)}");
        Console.WriteLine();

        // Column header
        // Primary = pre-scatter phase only (ไม่รวม scatter top-up)
        // Total   = Primary + Condo + Scatter  (ตรงกับ GUI packed/req)
        const string colHdr = "  #  Product                            Req   Total  Primary  Condo  Scatter";
        Console.WriteLine(colHdr);
        Console.WriteLine("  " + Bar().Substring(2));

        foreach (var info in output.PackInfos)
        {
            var s       = info.Spec;
            // info.Result.Packed = primary-phase boxes (ก่อน scatter phase เข้ามา)
            int primary = info.Result.Packed;
            int condo   = output.CondoMap.GetValueOrDefault(info.ProductIndex, 0);
            int scatter = output.ScatterMap.GetValueOrDefault(info.ProductIndex, 0);
            int total   = primary + condo + scatter;
            bool ok     = total == info.Requested;
            string name = $"{s.Description} {s.Content} {s.PackSize}";
            string status = ok ? "✓" : $"✗ short {info.Requested - total}";

            Console.WriteLine($"  {info.ProductIndex,2}  {name,-34} {info.Requested,4}  {total,6}  {primary,7}  {condo,5}  {scatter,7}  {status}");
        }

        Console.WriteLine();

        // ── 3. Stack detail ───────────────────────────────────────────────────
        Console.WriteLine($"── STACK DETAIL (StackIndex + LayerIndex) {Bar().Substring(41)}");

        foreach (var info in output.PackInfos)
        {
            var s       = info.Spec;
            int primary = info.Result.Packed;
            int condo   = output.CondoMap.GetValueOrDefault(info.ProductIndex, 0);
            int scatter = output.ScatterMap.GetValueOrDefault(info.ProductIndex, 0);
            int total   = primary + condo + scatter;
            bool ok     = total == info.Requested;

            bool hasBothPatterns = s.PatternB is { Length: > 0 };
            string patternStr    = s.PatternA is { Length: > 0 }
                ? (hasBothPatterns ? "A+B" : "A only")
                : "none";

            string name = $"{s.Description} {s.Content} {s.PackSize}";

            Console.WriteLine();
            Console.WriteLine(Bar('─'));
            Console.WriteLine($" [{info.ProductIndex}] {name}");
            Console.WriteLine($"     Box: {s.W:F1}×{s.L:F1}×{s.H:F1} cm  |  Pattern: {patternStr}");
            Console.WriteLine($"     Req={info.Requested}  Primary={primary}  Condo={condo}  Scatter={scatter}  Total={total} {(ok ? "✓" : "✗")}");

            var productBoxes = output.Placements
                .Where(p => p.ProductIndex == info.ProductIndex)
                .ToList();

            if (productBoxes.Count == 0)
            {
                Console.WriteLine("     (no placements)");
                continue;
            }

            // ── กำหนด scatter cutoff ────────────────────────────────────────
            // scatter phase วางกล่องบน primary stack (StackIndex < CondoStackBase)
            // โดยเติมจาก layer ต่ำสุด (shortest stack first) ขึ้นไป
            // จำนวน primary-only boxes = info.Result.Packed
            // → เราสามารถ mark scatter ได้โดยเรียง primary boxes ตาม (StackIndex, LayerIndex)
            //   แล้ว N box สุดท้าย (นับจากบน) คือ scatter
            var primaryBoxes = productBoxes
                .Where(p => p.StackIndex < PackingEngine.CondoStackBase)
                .OrderBy(p => p.StackIndex)
                .ThenBy(p => p.LayerIndex)
                .ToList();

            // หา (StackIndex, LayerIndex) ที่เป็น scatter
            // scatter วางที่ top layer ของแต่ละ stack → boxes อยู่ท้ายสุดของ sorted list
            var scatterBoxKeys = new HashSet<(int si, int li)>();
            if (scatter > 0 && primaryBoxes.Count > 0)
            {
                // จัดกลุ่มตาม stack แล้วหา max layer ของแต่ละ stack
                // scatter เพิ่ม layer ใหม่บนสุด → maxLayer ของแต่ละ stack มาจาก scatter
                // กระจาย scatter ให้แต่ละ stack ตาม scatterMap logic (shortest first)
                var stackLayerGroups = primaryBoxes
                    .GroupBy(p => p.StackIndex)
                    .ToDictionary(
                        g => g.Key,
                        g => g.GroupBy(p => p.LayerIndex)
                               .OrderBy(lg => lg.Key)
                               .Select(lg => (Layer: lg.Key, Count: lg.Count()))
                               .ToList());

                // นับ scatter ลงไปจาก top ของแต่ละ stack
                int remaining = scatter;
                // เรียง stack จาก max layer สูงสุดก่อน (scatter วางที่ top ของ shortest stack ก่อน
                // แต่หลังจาก scatter หมดแล้ว top layer ของแต่ละ stack จะสูงขึ้น)
                // วิธีที่แม่นยำกว่า: ใช้ total boxes ใน primary phase = info.Result.Packed
                // boxes ใน sorted list ที่ index >= info.Result.Packed คือ scatter
                for (int i = info.Result.Packed; i < primaryBoxes.Count; i++)
                {
                    var b = primaryBoxes[i];
                    scatterBoxKeys.Add((b.StackIndex, b.LayerIndex));
                }
            }

            // ── Stack summary table ──────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine($"  {"StackIdx",8}  {"Type",-8}  {"Layers",-10}  {"Boxes",5}" +
                              $"  {"Y=[start,   end ]",-20}  {"Z=[0,     top ]",-18}");
            Console.WriteLine("  " + Bar('·').Substring(2));

            var byStack = productBoxes
                .GroupBy(p => p.StackIndex)
                .OrderBy(g => g.Key)
                .ToList();

            foreach (var sg in byStack)
            {
                int    si      = sg.Key;
                string sType   = si >= PackingEngine.MixedStackBase ? "mixed"
                               : si >= PackingEngine.CondoStackBase  ? "condo"
                               : "primary";
                var    boxes   = sg.ToList();
                int    minL    = boxes.Min(p => p.LayerIndex);
                int    maxL    = boxes.Max(p => p.LayerIndex);
                int    cnt     = boxes.Count;
                double yStart  = boxes.Min(p => p.Y);
                double yEnd    = boxes.Max(p => p.Y + p.BL);
                double zTop    = boxes.Max(p => p.Z + p.BH);
                string layers  = minL == maxL ? $"L{minL}" : $"L{minL}–L{maxL}";

                Console.WriteLine($"  {si,8}  {sType,-8}  {layers,-10}  {cnt,5}" +
                                  $"  Y=[{yStart,6:F1}, {yEnd,6:F1}]  Z=[{0,6:F1}, {zTop,6:F1}]");
            }

            // ── Per-stack layer breakdown ────────────────────────────────────
            foreach (var sg in byStack)
            {
                int    si    = sg.Key;
                string sType = si >= PackingEngine.MixedStackBase ? "mixed"
                             : si >= PackingEngine.CondoStackBase  ? "condo"
                             : "primary";

                var byLayer = sg
                    .GroupBy(p => p.LayerIndex)
                    .OrderBy(g => g.Key)
                    .ToList();

                Console.WriteLine();
                Console.WriteLine($"  Stack {si} ({sType})  —  {byLayer.Count} layers, {sg.Count()} boxes");
                Console.WriteLine($"    {"Layer",5}  {"Boxes",5}  {"Z=[  start,    end ]",-22}  {"Rotated",-9}  Phase");
                Console.WriteLine("    " + Bar('·').Substring(4));

                foreach (var lg in byLayer)
                {
                    int    li      = lg.Key;
                    var    lBoxes  = lg.ToList();
                    int    lCnt    = lBoxes.Count;
                    double zStart  = lBoxes.Min(p => p.Z);
                    double zEnd    = lBoxes.Max(p => p.Z + p.BH);
                    int    rotCnt  = lBoxes.Count(p => p.Rotated);
                    string rotStr  = rotCnt == 0 ? "no"
                                   : rotCnt == lCnt ? "yes"
                                   : $"{rotCnt}/{lCnt}";

                    bool isScatter = scatterBoxKeys.Contains((si, li));
                    string phase   = isScatter ? "scatter ↑" : "primary";

                    Console.WriteLine($"    L{li,4}  {lCnt,5}  Z=[{zStart,7:F1}, {zEnd,7:F1}]  {rotStr,-9}  {phase}");
                }
            }
        }

        Console.WriteLine();

        // ── 4. Geometry bounds check ──────────────────────────────────────────
        Console.WriteLine($"── GEOMETRY CHECK {Bar().Substring(17)}");
        Console.WriteLine();

        int oobCount = 0;

        foreach (var p in output.Placements)
        {
            bool outX = p.X < -0.1 || p.X + p.BW > iw + 0.1;
            bool outY = p.Y < -0.1 || p.Y + p.BL > il + 0.1;
            bool outZ = p.Z < -0.1 || p.Z + p.BH > ih + 0.1;

            if (outX || outY || outZ)
            {
                oobCount++;
                if (oobCount <= 5)
                {
                    Console.WriteLine($"  OUT-OF-BOUNDS  prod={p.ProductIndex}  stack={p.StackIndex}  layer={p.LayerIndex}");
                    Console.WriteLine($"    pos=({p.X:F1},{p.Y:F1},{p.Z:F1})  size=({p.BW:F1},{p.BL:F1},{p.BH:F1})");
                    if (outX) Console.WriteLine($"    X: [{p.X:F1}, {p.X+p.BW:F1}] exceeds [0, {iw}]");
                    if (outY) Console.WriteLine($"    Y: [{p.Y:F1}, {p.Y+p.BL:F1}] exceeds [0, {il}]");
                    if (outZ) Console.WriteLine($"    Z: [{p.Z:F1}, {p.Z+p.BH:F1}] exceeds [0, {ih}]");
                }
            }
        }

        if (oobCount == 0)
            Console.WriteLine("  Out-of-bounds  : 0 ✓");
        else
            Console.WriteLine($"  Out-of-bounds  : {oobCount} ✗  (showing first 5 above)");

        // Stack Y-separation check:
        // primary stacks ของ product ต่างกันไม่ควร overlap กันใน Y axis
        // (StackIndex เป็น per-product จึงไม่นับ global — ต้อง group ด้วย product ก่อน)
        int yOverlapCount = 0;
        // หา Y range ของแต่ละ (product, primaryStack)
        var stackRanges = output.Placements
            .Where(p => p.StackIndex < PackingEngine.CondoStackBase)
            .GroupBy(p => (p.ProductIndex, p.StackIndex))
            .Select(g => (
                Prod: g.Key.ProductIndex,
                SI:   g.Key.StackIndex,
                YMin: g.Min(p => p.Y),
                YMax: g.Max(p => p.Y + p.BL)))
            .OrderBy(r => r.YMin)
            .ToList();

        for (int i = 0; i < stackRanges.Count; i++)
        {
            for (int j = i + 1; j < stackRanges.Count; j++)
            {
                var a = stackRanges[i];
                var b = stackRanges[j];
                if (b.YMin >= a.YMax - 0.1) break;       // sorted by YMin → ไม่ต้อง scan ต่อ
                if (a.Prod == b.Prod) continue;           // product เดียวกัน ข้ามได้
                // overlap!
                yOverlapCount++;
                if (yOverlapCount <= 3)
                    Console.WriteLine($"  Y-OVERLAP  prod{a.Prod}·stack{a.SI} Y=[{a.YMin:F1},{a.YMax:F1}]" +
                                      $"  ↔  prod{b.Prod}·stack{b.SI} Y=[{b.YMin:F1},{b.YMax:F1}]");
            }
        }

        if (yOverlapCount == 0)
            Console.WriteLine("  Y-separation   : OK — no primary stacks of different products overlap ✓");
        else
            Console.WriteLine($"  Y-separation   : {yOverlapCount} cross-product Y overlaps ✗");

        // Layer continuity check: ในแต่ละ (product, stack) LayerIndex ควรต่อเนื่องจาก 0
        int gapCount = 0;
        var byProductStack = output.Placements
            .GroupBy(p => (p.ProductIndex, p.StackIndex))
            .ToList();

        foreach (var g in byProductStack)
        {
            var layers = g.Select(p => p.LayerIndex).Distinct().OrderBy(x => x).ToList();
            for (int i = 1; i < layers.Count; i++)
            {
                if (layers[i] != layers[i - 1] + 1)
                {
                    gapCount++;
                    if (gapCount <= 3)
                        Console.WriteLine($"  LAYER GAP  prod={g.Key.ProductIndex}  stack={g.Key.StackIndex}: L{layers[i - 1]}→L{layers[i]} (gap!)");
                }
            }
        }

        if (gapCount == 0)
            Console.WriteLine("  Layer continuity: OK — no gaps in layer sequence ✓");
        else
            Console.WriteLine($"  Layer continuity: {gapCount} gap(s) found ✗");

        Console.WriteLine();
        Console.WriteLine(Bar('═'));
        Console.WriteLine();
    }
}
