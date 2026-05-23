using System.Collections.Generic;
using System.Linq;

namespace CargoFit;

internal static class StatsCalculator
{
    internal record PackStatRow(
        ProductSpec Spec,
        int ProductIndex,
        bool HasPattern,
        int TotalPacked,
        int Requested,
        int FullStacks,
        int MixedPlaced,
        int CondoPlaced,
        int ScatterPlaced);

    internal static double ContainerCbm(ContainerSpec c) =>
        (double)c.InteriorW * c.InteriorL * c.InteriorH / 1_000_000.0;

    internal static double UsedCbm(IReadOnlyList<BoxPlacement> placements) =>
        placements.Sum(p => p.BW * p.BL * p.BH / 1_000_000.0);

    /// <summary>
    /// Free contiguous length (cm) along the door↔back axis, given current placements.
    /// Equals the empty span at the door side after packing (boxes are pushed to the back wall).
    /// </summary>
    internal static double RemainingDoorLengthCm(ContainerSpec c, IReadOnlyList<BoxPlacement> placements)
    {
        if (placements.Count == 0) return c.InteriorL;
        double minY = placements.Min(p => p.Y);
        double maxY = placements.Max(p => p.Y + p.BL);
        double used = maxY - minY;
        double remain = c.InteriorL - used;
        return remain < 0 ? 0 : remain;
    }

    internal static List<PackStatRow> ComputeRows(
        IReadOnlyList<PackInfo> packInfos,
        IReadOnlyList<BoxPlacement> placements,
        IReadOnlyDictionary<int,int> mixedMap,
        IReadOnlyDictionary<int,int> condoMap,
        IReadOnlyDictionary<int,int> scatterMap)
    {
        var rows = new List<PackStatRow>();
        foreach (var info in packInfos)
        {
            if (!info.HasPattern)
            {
                rows.Add(new PackStatRow(info.Spec, info.ProductIndex, false, 0, info.Requested, 0, 0, 0, 0));
                continue;
            }

            int totalPacked = placements.Count(p => p.ProductIndex == info.ProductIndex);
            int fullStacks  = placements
                .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < PackingEngine.CondoStackBase)
                .Select(p => p.StackIndex).Distinct().Count();

            rows.Add(new PackStatRow(
                info.Spec, info.ProductIndex, true, totalPacked, info.Requested,
                fullStacks,
                mixedMap.GetValueOrDefault(info.ProductIndex, 0),
                condoMap.GetValueOrDefault(info.ProductIndex, 0),
                scatterMap.GetValueOrDefault(info.ProductIndex, 0)));
        }
        return rows;
    }
}
