using System;
using System.Collections.Generic;
using System.Linq;

namespace logistic;

internal record struct ContainerDims(double W, double L, double H);
internal record struct PlaceResult(int Packed, double EndY, int FullStacks, int PartialBoxes);
internal record struct PackInfo(ProductSpec Spec, int ProductIndex, int Requested, double StartY, PlaceResult Result, bool HasPattern);

internal sealed class PackingOutput
{
    internal List<BoxPlacement>  Placements { get; }
    internal List<PackInfo>      PackInfos  { get; }
    internal Dictionary<int,int> MixedMap   { get; }
    internal Dictionary<int,int> CondoMap   { get; }

    internal PackingOutput(
        List<BoxPlacement> placements, List<PackInfo> packInfos,
        Dictionary<int,int> mixedMap, Dictionary<int,int> condoMap)
    {
        Placements = placements;
        PackInfos  = packInfos;
        MixedMap   = mixedMap;
        CondoMap   = condoMap;
    }
}

internal static class PackingEngine
{
    internal const int    CondoStackBase = 1000;
    internal const int    MixedStackBase = 2000;

    internal static PackingOutput Calculate(
        ContainerSpec container,
        IReadOnlyList<(ProductSpec Spec, int Qty)> requests)
    {
        var dims = new ContainerDims(container.InteriorW, container.InteriorL, container.InteriorH);
        var placements = new List<BoxPlacement>();

        var packInfos      = RunPrimaryPacking(dims, requests, placements, out double currentY);
        RunBalancing(packInfos, dims, placements);
        var partialRemoved = RunPartialRemoval(packInfos, dims, placements, ref currentY);
        var (mixedMap, condoAreaStart) = RunMixedPlacement(packInfos, dims, partialRemoved, placements, ref currentY);
        var condoMap       = RunCondoPlacement(packInfos, dims, partialRemoved, mixedMap, condoAreaStart, placements);

        return new PackingOutput(placements, packInfos, mixedMap, condoMap);
    }

    private static List<PackInfo> RunPrimaryPacking(
        ContainerDims dims, IReadOnlyList<(ProductSpec Spec, int Qty)> requests,
        List<BoxPlacement> placements, out double currentY)
    {
        var packInfos = new List<PackInfo>();
        currentY = 0;

        for (int i = 0; i < requests.Count; i++)
        {
            var (spec, requested) = requests[i];
            bool hasPattern = spec.PatternA is { Length: > 0 };
            double productStartY = currentY;

            PlaceResult r;
            if (hasPattern)
            {
                r = PlaceProduct(dims, spec, requested, currentY, i, placements);
                currentY = r.EndY;
            }
            else
            {
                r = new PlaceResult(0, currentY, 0, 0);
            }
            packInfos.Add(new PackInfo(spec, i, requested, productStartY, r, hasPattern));
        }

        return packInfos;
    }

    private static void RunBalancing(
        List<PackInfo> packInfos, ContainerDims dims, List<BoxPlacement> placements)
    {
        foreach (var info in packInfos)
        {
            if (!info.HasPattern || info.Result.Packed <= 0) continue;
            double stackDepth = LayerDepth(info.Spec.PatternA!, info.Spec.W, info.Spec.L);
            int layerLimit = CalcLayerLimit(info.Spec, dims);
            BalanceAllStacks(placements, info.ProductIndex, info.Spec, dims,
                             info.StartY, stackDepth, layerLimit);
        }
    }

    private static Dictionary<int,int> RunPartialRemoval(
        List<PackInfo> packInfos, ContainerDims dims,
        List<BoxPlacement> placements, ref double currentY)
    {
        var removed = new Dictionary<int,int>();
        if (dims.L - currentY >= 50.0) return removed;

        foreach (var info in packInfos)
        {
            if (!info.HasPattern) continue;
            var stacks = placements
                .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase)
                .GroupBy(p => p.StackIndex)
                .OrderBy(g => g.Key)
                .Select(g => (g.Key, Layers: g.Max(p => p.LayerIndex) + 1))
                .ToList();
            if (stacks.Count < 2 || stacks[^1].Layers >= stacks[^2].Layers - 1) continue;

            int maxSI = stacks[^1].Key;
            int n = placements.RemoveAll(p => p.ProductIndex == info.ProductIndex && p.StackIndex == maxSI);
            if (n > 0) removed[info.ProductIndex] = n;
        }

        currentY = placements.Count > 0 ? placements.Max(p => p.Y + p.BL) + 0.1 : 0;
        return removed;
    }

    private static (Dictionary<int,int> mixedMap, double condoAreaStart) RunMixedPlacement(
        List<PackInfo> packInfos, ContainerDims dims,
        Dictionary<int,int> partialRemoved,
        List<BoxPlacement> placements, ref double currentY)
    {
        var withRem = packInfos
            .Where(info => info.HasPattern)
            .Select(info => (info, Rem: info.Requested - (info.Result.Packed - partialRemoved.GetValueOrDefault(info.ProductIndex, 0))))
            .Where(x => x.Rem > 0)
            .OrderByDescending(x => x.info.Spec.Cbm)
            .ToList();

        double condoAreaStart = Math.Max(currentY, dims.L - withRem.Sum(x => x.info.Spec.L));
        var mixedMap = new Dictionary<int,int>();

        if (condoAreaStart - currentY >= 50.0 && withRem.Count > 0)
        {
            int totalRem = withRem.Sum(x => x.Rem);
            double xOffset = 0;

            foreach (var (info, rem) in withRem)
            {
                double xSlice = (double)rem / totalRem * dims.W;
                int placed = PlaceMixedSlice(dims, info.Spec, rem, currentY,
                                             xOffset, xSlice, condoAreaStart,
                                             info.ProductIndex, MixedStackBase + info.ProductIndex * 10, placements);
                if (placed > 0) mixedMap[info.ProductIndex] = placed;
                xOffset += xSlice;
            }

            var mixedBoxes = placements.Where(p => p.StackIndex >= MixedStackBase).ToList();
            if (mixedBoxes.Count > 0) currentY = mixedBoxes.Max(p => p.Y + p.BL) + 0.1;
        }

        return (mixedMap, condoAreaStart);
    }

    private static Dictionary<int,int> RunCondoPlacement(
        List<PackInfo> packInfos, ContainerDims dims,
        Dictionary<int,int> partialRemoved, Dictionary<int,int> mixedMap,
        double condoAreaStart, List<BoxPlacement> placements)
    {
        double condoY = condoAreaStart;
        var condoMap = new Dictionary<int,int>();

        foreach (var info in packInfos)
        {
            if (!info.HasPattern) continue;
            int primaryPacked = info.Result.Packed - partialRemoved.GetValueOrDefault(info.ProductIndex, 0);
            int rem = info.Requested - primaryPacked - mixedMap.GetValueOrDefault(info.ProductIndex, 0);
            if (rem <= 0) continue;

            int placed = PlaceCondoStack(dims, info.Spec, rem, ref condoY,
                                         info.ProductIndex, CondoStackBase + info.ProductIndex, placements);
            if (placed > 0) condoMap[info.ProductIndex] = placed;
        }

        return condoMap;
    }

    private static double LayerDepth(LayerSection[] sections, double W, double L)
    {
        double max = 0;
        foreach (var s in sections)
        {
            double depth = 0;
            foreach (var sub in s.GetSubRows())
                depth += sub.Rows * (sub.Rotated ? W : L);
            max = Math.Max(max, depth);
        }
        return max;
    }

    private static PlaceResult PlaceProduct(
        ContainerDims dims, ProductSpec spec, int requested,
        double startY, int productIndex, List<BoxPlacement> placements)
    {
        if (spec.PatternA is not { Length: > 0 }) return new(0, startY, 0, 0);

        double stackDepth = LayerDepth(spec.PatternA, spec.W, spec.L);
        if (stackDepth <= 0) return new(0, startY, 0, 0);

        int maxLayers   = spec.MaxLayers > 0 ? spec.MaxLayers : int.MaxValue;
        int maxHeight   = (int)Math.Floor(dims.H / spec.H);
        int layerLimit  = Math.Min(maxLayers, maxHeight);

        int packed       = 0;
        int stackIndex   = 0;
        int fullStacks   = 0;
        int partialBoxes = 0;

        while (packed < requested)
        {
            double stackY = startY + stackIndex * stackDepth;
            if (stackY >= dims.L) break;

            bool flipStart    = (stackIndex % 2 == 1) && spec.PatternB is { Length: > 0 };
            int  beforeStack  = packed;
            int  layersPlaced = 0;

            for (int layer = 0; layer < layerLimit && packed < requested; layer++)
            {
                double z     = layer * spec.H;
                bool   useA  = flipStart ? (layer % 2 == 1) : (layer % 2 == 0);
                var sections = useA ? spec.PatternA : (spec.PatternB ?? spec.PatternA);

                int n = PlaceLayerAt(sections, spec, dims, stackY, z, requested - packed, productIndex, placements, stackIndex, layer);
                if (n < 0) break;
                packed += n;
                layersPlaced++;
            }

            if (layerLimit > 0 && layersPlaced == layerLimit)
                fullStacks++;
            else if (layersPlaced > 0)
                partialBoxes = packed - beforeStack;

            stackIndex++;
        }

        // Remove a partial last layer whose box count is less than the preceding layer.
        if (stackIndex > 0)
        {
            int lastSI = stackIndex - 1;
            var lastGroups = placements
                .Where(p => p.ProductIndex == productIndex && p.StackIndex == lastSI)
                .GroupBy(p => p.LayerIndex)
                .OrderBy(g => g.Key)
                .ToList();
            if (lastGroups.Count >= 2)
            {
                int lastCount = lastGroups[^1].Count();
                int prevCount = lastGroups[^2].Count();
                if (lastCount < prevCount)
                {
                    int layerKey = lastGroups[^1].Key;
                    int removed = placements.RemoveAll(p =>
                        p.ProductIndex == productIndex &&
                        p.StackIndex   == lastSI &&
                        p.LayerIndex   == layerKey);
                    packed -= removed;
                }
            }
        }

        return new(packed, startY + stackIndex * stackDepth, fullStacks, partialBoxes);
    }

    private static int PlaceLayerAt(
        LayerSection[] sections, ProductSpec spec, ContainerDims dims,
        double stackY, double z, int limit, int productIndex,
        List<BoxPlacement> placements, int stackIndex, int layerIndex)
    {
        if (z + spec.H > dims.H + 0.01) return -1;

        static double SectionWidth(LayerSection s, double w, double l) =>
            s.GetSubRows().Max(sub => sub.Cols * (sub.Rotated ? l : w));

        double tierW = 0;
        foreach (var s in sections)
            tierW += SectionWidth(s, spec.W, spec.L);
        if (tierW <= 0) return -1;

        int numTiers = Math.Max(1, (int)Math.Floor(dims.W / tierW));
        int packed   = 0;

        for (int tier = 0; tier < numTiers && packed < limit; tier++)
        {
            double sectionX = tier * tierW;
            foreach (var section in sections)
            {
                double subY = stackY;
                foreach (var sub in section.GetSubRows())
                {
                    double bw = sub.Rotated ? spec.L : spec.W;
                    double bl = sub.Rotated ? spec.W : spec.L;

                    for (int c = 0; c < sub.Cols && packed < limit; c++)
                    {
                        for (int r = 0; r < sub.Rows && packed < limit; r++)
                        {
                            double px = sectionX + c * bw;
                            double py = subY + r * bl;
                            if (px + bw > dims.W + 0.01 || py + bl > dims.L + 0.01) continue;
                            placements.Add(new BoxPlacement(px, py, z, bw, bl, spec.H, productIndex, sub.Rotated, stackIndex, layerIndex));
                            packed++;
                        }
                    }
                    subY += sub.Rows * bl;
                }
                sectionX += SectionWidth(section, spec.W, spec.L);
            }
        }

        return packed > 0 ? packed : -1;
    }

    private static int CalcLayerLimit(ProductSpec spec, ContainerDims dims) =>
        Math.Min(spec.MaxLayers > 0 ? spec.MaxLayers : int.MaxValue,
                 (int)Math.Floor(dims.H / spec.H));

    private static void BalanceAllStacks(
        List<BoxPlacement> placements, int productIndex,
        ProductSpec spec, ContainerDims dims,
        double startY, double stackDepth, int layerLimit)
    {
        if (spec.PatternA is not { Length: > 0 }) return;

        var stackIndices = placements
            .Where(p => p.ProductIndex == productIndex && p.StackIndex < CondoStackBase)
            .Select(p => p.StackIndex)
            .Distinct()
            .OrderBy(si => si)
            .ToList();

        if (stackIndices.Count < 2) return;

        var layerCounts = stackIndices.ToDictionary(si => si, si =>
            placements
                .Where(p => p.ProductIndex == productIndex && p.StackIndex == si)
                .Max(p => p.LayerIndex) + 1);

        if (layerCounts.Values.Max() - layerCounts.Values.Min() <= 1) return;

        // Redistribute evenly: each stack gets baseL or baseL+1 layers.
        int totalLayers = layerCounts.Values.Sum();
        int numStacks   = stackIndices.Count;
        int baseL       = Math.Min(totalLayers / numStacks, layerLimit);
        int tallL       = Math.Min(baseL + 1, layerLimit);
        int extra       = totalLayers % numStacks;

        for (int j = 0; j < stackIndices.Count; j++)
        {
            int si      = stackIndices[j];
            int target  = j < (numStacks - extra) ? baseL : tallL;
            int current = layerCounts[si];

            if (current > target)
            {
                placements.RemoveAll(p =>
                    p.ProductIndex == productIndex &&
                    p.StackIndex   == si &&
                    p.LayerIndex   >= target);
            }
            else if (current < target)
            {
                double stackY = startY + si * stackDepth;
                bool   flipIt = (si % 2 == 1) && spec.PatternB is { Length: > 0 };

                for (int layer = current; layer < target; layer++)
                {
                    double z     = layer * spec.H;
                    if (z + spec.H > dims.H + 0.01) break;
                    bool   useA  = flipIt ? (layer % 2 == 1) : (layer % 2 == 0);
                    var sections = useA ? spec.PatternA! : (spec.PatternB ?? spec.PatternA)!;
                    PlaceLayerAt(sections, spec, dims, stackY, z, int.MaxValue,
                                 productIndex, placements, si, layer);
                }
            }
        }
    }

    private static int PlaceMixedSlice(
        ContainerDims dims, ProductSpec spec, int remaining,
        double startY, double xOffset, double xWidth, double maxY,
        int productIndex, int stackIndexBase, List<BoxPlacement> placements)
    {
        int colsInSlice = (int)Math.Floor(xWidth / spec.W);
        if (colsInSlice <= 0) return 0;

        int maxLayers = CalcLayerLimit(spec, dims);
        if (maxLayers <= 0) return 0;

        int placed      = 0;
        int stackOffset = 0;

        while (placed < remaining)
        {
            double stackY = startY + stackOffset * spec.L;
            if (stackY + spec.L > maxY + 0.01) break;

            for (int layer = 0; layer < maxLayers && placed < remaining; layer++)
            {
                double z = layer * spec.H;
                if (z + spec.H > dims.H + 0.01) break;

                for (int col = 0; col < colsInSlice && placed < remaining; col++)
                {
                    double px = xOffset + col * spec.W;
                    if (px + spec.W > dims.W + 0.01) break;
                    placements.Add(new BoxPlacement(
                        px, stackY, z, spec.W, spec.L, spec.H,
                        productIndex, false, stackIndexBase + stackOffset, layer));
                    placed++;
                }
            }
            stackOffset++;
        }

        return placed;
    }

    private static int PlaceCondoStack(
        ContainerDims dims, ProductSpec spec, int remaining,
        ref double condoY, int productIndex, int condoStackIndex,
        List<BoxPlacement> placements)
    {
        if (remaining <= 0) return 0;

        int condoCount = spec.CondoCount > 0
            ? spec.CondoCount
            : (int)Math.Floor(dims.W / spec.W);
        int maxLayers = CalcLayerLimit(spec, dims);
        if (condoCount <= 0 || maxLayers <= 0 || condoY + spec.L > dims.L + 0.01) return 0;

        int cols   = Math.Min(condoCount, (int)Math.Floor(dims.W / spec.W));
        int placed = 0;
        for (int layer = 0; layer < maxLayers && placed < remaining; layer++)
        {
            for (int col = 0; col < cols && placed < remaining; col++)
            {
                placements.Add(new BoxPlacement(
                    col * spec.W, condoY, layer * spec.H, spec.W, spec.L, spec.H,
                    productIndex, false, condoStackIndex, layer));
                placed++;
            }
        }

        if (placed > 0) condoY += spec.L;
        return placed;
    }
}
