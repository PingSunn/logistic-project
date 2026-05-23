using System.Collections.Generic;
using System.Linq;
using CargoFit;
using Xunit.Abstractions;

namespace CargoFit.Tests;

internal static class TestHelpers
{
    internal static ContainerSpec Container20ft()  => new("ตู้สั้น",     "20 ft",    244, 600,  259, Gap: 10);
    internal static ContainerSpec Container40ft()  => new("ตู้ยาว",     "40 ft",    244, 1209, 260, Gap: 10);
    internal static ContainerSpec Container40HC()  => new("ตู้ไฮคิวบ์", "40 ft HC", 244, 1203, 290, Gap: 10);

    internal static void DumpOutput(
        ContainerSpec container,
        IReadOnlyList<(ProductSpec Spec, int Qty)> requests,
        PackingOutput output,
        ITestOutputHelper log)
    {
        double iw = container.InteriorW;
        double il = container.InteriorL;
        double ih = container.InteriorH;

        double containerCbm = iw * il * ih / 1_000_000.0;
        double usedCbm      = output.Placements.Sum(p => p.BW * p.BL * p.BH / 1_000_000.0);
        double pct          = containerCbm > 0 ? usedCbm / containerCbm * 100.0 : 0;

        log.WriteLine($"=== {container.Name} {container.SizeLabel}  ({iw}×{il}×{ih} cm interior) ===");
        log.WriteLine($"    placements: {output.Placements.Count}  CBM: {usedCbm:F3}/{containerCbm:F3} ({pct:F1}%)");
        log.WriteLine("");

        log.WriteLine("  #  Product                            Req  Packed  Primary  Condo  Scatter  Pattern");
        log.WriteLine("  " + new string('-', 81));

        foreach (var info in output.PackInfos)
        {
            var s     = info.Spec;
            int pri   = output.Placements.Count(p =>
                            p.ProductIndex == info.ProductIndex &&
                            p.StackIndex   <  PackingEngine.CondoStackBase);
            int condo   = output.CondoMap.GetValueOrDefault(info.ProductIndex, 0);
            int scatter = output.ScatterMap.GetValueOrDefault(info.ProductIndex, 0);
            string name = $"{s.Description} {s.Content} {s.PackSize}";

            log.WriteLine($"  {info.ProductIndex,2}  {name,-34} {info.Requested,4}" +
                          $"  {info.Result.Packed,6}  {pri,7}  {condo,5}  {scatter,7}  {(info.HasPattern ? "yes" : "no")}");
        }

        log.WriteLine("");

        if (output.Placements.Count > 0)
        {
            double maxX = output.Placements.Max(p => p.X + p.BW);
            double maxY = output.Placements.Max(p => p.Y + p.BL);
            double maxZ = output.Placements.Max(p => p.Z + p.BH);
            log.WriteLine($"  Bounding box: X=[0,{maxX:F1}]  Y=[0,{maxY:F1}]  Z=[0,{maxZ:F1}]");
            log.WriteLine($"  Container   : X=[0,{iw}]  Y=[0,{il}]  Z=[0,{ih}]");
        }
    }
}
