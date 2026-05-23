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
    internal Dictionary<int,int> ScatterMap { get; }

    internal PackingOutput(
        List<BoxPlacement> placements, List<PackInfo> packInfos,
        Dictionary<int,int> mixedMap, Dictionary<int,int> condoMap,
        Dictionary<int,int> scatterMap)
    {
        Placements = placements;
        PackInfos  = packInfos;
        MixedMap   = mixedMap;
        CondoMap   = condoMap;
        ScatterMap = scatterMap;
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

        var effectiveLayers = ComputeEffectiveLayers(dims, requests);
        var packInfos = RunPrimaryPacking(dims, requests, placements, out double currentY, effectiveLayers);
        RunBalancing(packInfos, dims, placements, ref currentY, effectiveLayers);
        RunLayerBalancing(packInfos, dims, placements);
        RunPartialRemoval(packInfos, dims, placements, ref currentY);
        SortStacksByHeight(packInfos, placements, dims);
        var condoMap   = RunCondoPlacement(packInfos, dims, currentY, placements);
        var scatterMap = RunScatteredTopPlacement(packInfos, dims, placements);

        return new PackingOutput(placements, packInfos, new Dictionary<int,int>(), condoMap, scatterMap);
    }

    private static List<PackInfo> RunPrimaryPacking(
        ContainerDims dims, IReadOnlyList<(ProductSpec Spec, int Qty)> requests,
        List<BoxPlacement> placements, out double currentY, int[] effectiveLayers)
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
                r = PlaceProduct(dims, spec, requested, currentY, i, placements,
                                 overrideMaxLayers: effectiveLayers[i]);
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
        List<PackInfo> packInfos, ContainerDims dims,
        List<BoxPlacement> placements, ref double currentY, int[] effectiveLayers)
    {
        // ── Step A: Fill free Y with more primary stacks ──────────────────────
        // Leave 50 cm for condo/mixed; keep adding stacks while space remains.
        var fillDims = dims with { L = dims.L - 50.0 };

        bool anyAdded = true;
        while (anyAdded && fillDims.L - currentY > 0)
        {
            anyAdded = false;

            var withRem = packInfos
                .Where(info => info.HasPattern)
                .Select(info => {
                    int primary = placements.Count(p =>
                        p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase);
                    return (info, rem: info.Requested - primary);
                })
                .Where(x => x.rem > 0)
                .OrderByDescending(x => x.info.Spec.Cbm)
                .ToList();

            foreach (var (info, rem) in withRem)
            {
                if (fillDims.L - currentY <= 0) break;

                // Skip if the next stack position has reduced capacity (truncated pattern at container edge).
                int fullBpl = CountLayerCapacity(info.Spec.PatternA!, info.Spec, fillDims, info.StartY);
                if (CountLayerCapacity(info.Spec.PatternA!, info.Spec, fillDims, currentY) < fullBpl) continue;

                int nextSI = placements
                    .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase)
                    .Select(p => p.StackIndex)
                    .DefaultIfEmpty(-1)
                    .Max() + 1;

                var r = PlaceProduct(fillDims, info.Spec, rem, currentY,
                                     info.ProductIndex, placements, nextSI,
                                     overrideMaxLayers: effectiveLayers[info.ProductIndex]);
                if (r.Packed > 0)
                {
                    currentY = Math.Max(currentY, r.EndY);
                    anyAdded = true;
                }
            }
        }

        // ── Step B: Global height balance (average target, ±1 layer) ──────────
        var active = packInfos
            .Where(info => info.HasPattern &&
                   placements.Any(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase))
            .ToList();

        if (active.Count > 0)
        {
            foreach (var info in active)
            {
                int targetLayers = effectiveLayers[info.ProductIndex];
                if (targetLayers <= 0) continue;

                var stackIndices = placements
                    .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase)
                    .Select(p => p.StackIndex).Distinct().OrderBy(si => si).ToList();

                int primaryPacked = placements.Count(p =>
                    p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase);

                foreach (int si in stackIndices)
                {
                    int current = placements
                        .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex == si)
                        .Select(p => p.LayerIndex).DefaultIfEmpty(-1).Max() + 1;

                    if (current > targetLayers)
                    {
                        int before = placements.Count(p =>
                            p.ProductIndex == info.ProductIndex && p.StackIndex == si);
                        placements.RemoveAll(p =>
                            p.ProductIndex == info.ProductIndex &&
                            p.StackIndex   == si &&
                            p.LayerIndex   >= targetLayers);
                        int after = placements.Count(p =>
                            p.ProductIndex == info.ProductIndex && p.StackIndex == si);
                        primaryPacked -= before - after;
                    }
                    else if (current < targetLayers)
                    {
                        double stackY = placements
                            .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex == si)
                            .Min(p => p.Y);
                        bool flipIt = (si % 2 == 1) && info.Spec.PatternB is { Length: > 0 };

                        for (int layer = current; layer < targetLayers; layer++)
                        {
                            double z = layer * info.Spec.H;
                            if (z + info.Spec.H > dims.H + 0.01) break;

                            bool useA    = flipIt ? (layer % 2 == 1) : (layer % 2 == 0);
                            var sections = useA ? info.Spec.PatternA! : (info.Spec.PatternB ?? info.Spec.PatternA)!;

                            int capacity = CountLayerCapacity(sections, info.Spec, dims, stackY);
                            if (capacity <= 0) break;
                            if (info.Requested - primaryPacked < capacity) break;

                            PlaceLayerAt(sections, info.Spec, dims, stackY, z, capacity,
                                         info.ProductIndex, placements, si, layer);
                            primaryPacked += capacity;
                        }
                    }
                }
            }
        }

        // ── Step C: Sync PackInfo.Result.Packed ───────────────────────────────
        for (int i = 0; i < packInfos.Count; i++)
        {
            var info = packInfos[i];
            if (!info.HasPattern) continue;
            int actual = placements.Count(p =>
                p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase);
            packInfos[i] = info with { Result = info.Result with { Packed = actual } };
        }
    }

    private static void RunPartialRemoval(
        List<PackInfo> packInfos, ContainerDims dims,
        List<BoxPlacement> placements, ref double currentY)
    {
        if (dims.L - currentY >= 50.0) return;

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
            placements.RemoveAll(p => p.ProductIndex == info.ProductIndex && p.StackIndex == maxSI);
        }

        currentY = placements.Count > 0 ? placements.Max(p => p.Y + p.BL) + 0.1 : 0;

        for (int i = 0; i < packInfos.Count; i++)
        {
            var info = packInfos[i];
            if (!info.HasPattern) continue;
            int actual = placements.Count(p =>
                p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase);
            packInfos[i] = info with { Result = info.Result with { Packed = actual } };
        }
    }

    private static Dictionary<int,int> RunCondoPlacement(
        List<PackInfo> packInfos, ContainerDims dims,
        double condoAreaStart, List<BoxPlacement> placements)
    {
        var condoMap = new Dictionary<int,int>();

        var withRem = packInfos
            .Where(info => info.HasPattern && info.Requested - info.Result.Packed > 0)
            .Select(info => (info, rem: info.Requested - info.Result.Packed))
            .OrderByDescending(x => x.rem * x.info.Spec.Cbm)
            .ThenByDescending(x => x.info.Spec.H)
            .ToList();

        if (withRem.Count == 0) return condoMap;

        // Target Z = the height the primary stack adjacent to the condo border
        // will reach after scatter top-up — i.e. min(MaxLayers, floor(H/spec.H)) × spec.H
        // of the adjacent product. Using "current TopZ" before scatter would
        // under-estimate, leaving condo columns >1 layer below primary.
        var adjacent = placements
            .Where(p => p.StackIndex < CondoStackBase)
            .GroupBy(p => p.StackIndex)
            .Select(g => new { EndY = g.Max(p => p.Y + p.BL), ProductIndex = g.First().ProductIndex })
            .OrderByDescending(s => s.EndY)
            .FirstOrDefault();

        if (adjacent is null) return condoMap;

        // Scatter doesn't honour MaxLayers (it tops up primary stacks until the
        // container ceiling), so target the same ceiling here. MaxLayers caps
        // only the primary phase before scatter — it doesn't bound final TopZ.
        var adjSpec = packInfos[adjacent.ProductIndex].Spec;
        int adjMaxLayers = (int)Math.Floor(dims.H / adjSpec.H);
        double targetCondoZ = adjMaxLayers * adjSpec.H;

        if (targetCondoZ <= 0) return condoMap;

        var colsMap = new int[packInfos.Count];
        foreach (var (info, _) in withRem)
            colsMap[info.ProductIndex] = CondoCols(info.Spec, dims);

        double colDepth = withRem.Max(x => x.info.Spec.L);
        double condoY   = condoAreaStart;

        bool HasY() => condoY + colDepth <= dims.L + 0.01;
        void NextColumn() { condoY += colDepth; }

        foreach (var (info, rem) in withRem)
        {
            int cols = colsMap[info.ProductIndex];
            if (cols <= 0) continue;

            int targetLayers = Math.Min(
                (int)Math.Floor(targetCondoZ / info.Spec.H),
                (int)Math.Floor(dims.H / info.Spec.H));
            if (targetLayers <= 0) continue;

            int condoCap = cols * targetLayers;
            if (rem < condoCap) continue;

            int fullColumns = rem / condoCap;
            int condoSI     = CondoStackBase + info.ProductIndex;

            for (int c = 0; c < fullColumns; c++)
            {
                if (!HasY()) break;

                for (int layer = 0; layer < targetLayers; layer++)
                {
                    double z = layer * info.Spec.H;
                    for (int col = 0; col < cols; col++)
                        placements.Add(new BoxPlacement(
                            col * info.Spec.W, condoY, z,
                            info.Spec.W, info.Spec.L, info.Spec.H,
                            info.ProductIndex, false, condoSI, layer));

                    condoMap[info.ProductIndex] = condoMap.GetValueOrDefault(info.ProductIndex) + cols;
                }

                NextColumn();
            }
        }

        return condoMap;
    }

    private static Dictionary<int,int> RunScatteredTopPlacement(
        List<PackInfo> packInfos, ContainerDims dims, List<BoxPlacement> placements)
    {
        var scatterMap = new Dictionary<int,int>();

        foreach (var info in packInfos)
        {
            if (!info.HasPattern) continue;

            int placedSoFar = placements.Count(p => p.ProductIndex == info.ProductIndex);
            int rem         = info.Requested - placedSoFar;
            if (rem <= 0) continue;

            var stacks = placements
                .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase)
                .GroupBy(p => p.StackIndex)
                .Select(g => (
                    SI:       g.Key,
                    StackY:   g.Min(p => p.Y),
                    TopLayer: g.Max(p => p.LayerIndex) + 1))
                .Where(s => s.TopLayer * info.Spec.H + info.Spec.H <= dims.H + 0.01)
                .Select(s => new ScatterStack(s.SI, s.StackY, s.TopLayer, s.TopLayer * info.Spec.H))
                .ToList();

            while (rem > 0 && stacks.Count > 0)
            {
                int pick = 0;
                for (int i = 1; i < stacks.Count; i++)
                {
                    double dz = stacks[i].TopZ - stacks[pick].TopZ;
                    if (dz < -0.001 || (dz < 0.001 && stacks[i].SI < stacks[pick].SI))
                        pick = i;
                }
                var s = stacks[pick];

                bool flipIt  = (s.SI % 2 == 1) && info.Spec.PatternB is { Length: > 0 };
                bool useA    = flipIt ? (s.TopLayer % 2 == 1) : (s.TopLayer % 2 == 0);
                var sections = useA ? info.Spec.PatternA! : (info.Spec.PatternB ?? info.Spec.PatternA)!;

                int cap = CountLayerCapacity(sections, info.Spec, dims, s.StackY);
                if (cap <= 0) { stacks.RemoveAt(pick); continue; }

                int take = Math.Min(cap, rem);
                int n = PlaceLayerAt(sections, info.Spec, dims, s.StackY, s.TopZ, take,
                                     info.ProductIndex, placements, s.SI, s.TopLayer);
                if (n <= 0) { stacks.RemoveAt(pick); continue; }

                rem -= n;
                scatterMap[info.ProductIndex] = scatterMap.GetValueOrDefault(info.ProductIndex) + n;

                int    nextLayer = s.TopLayer + 1;
                double nextZ     = s.TopZ + info.Spec.H;
                if (nextZ + info.Spec.H > dims.H + 0.01)
                    stacks.RemoveAt(pick);
                else
                    stacks[pick] = new ScatterStack(s.SI, s.StackY, nextLayer, nextZ);
            }
        }

        return scatterMap;
    }

    private record struct ScatterStack(int SI, double StackY, int TopLayer, double TopZ);

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

    private static int CountLayerCapacity(
        LayerSection[] sections, ProductSpec spec, ContainerDims dims, double stackY)
    {
        var scratch = new List<BoxPlacement>();
        int n = PlaceLayerAt(sections, spec, dims, stackY, 0.0, int.MaxValue, -1, scratch, -1, -1);
        return n < 0 ? 0 : n;
    }

    private static PlaceResult PlaceProduct(
        ContainerDims dims, ProductSpec spec, int requested,
        double startY, int productIndex, List<BoxPlacement> placements,
        int initialStackIndex = 0, int overrideMaxLayers = 0)
    {
        if (spec.PatternA is not { Length: > 0 }) return new(0, startY, 0, 0);

        double stackDepth = LayerDepth(spec.PatternA, spec.W, spec.L);
        if (stackDepth <= 0) return new(0, startY, 0, 0);

        int maxLayers   = overrideMaxLayers > 0
            ? overrideMaxLayers
            : (spec.MaxLayers > 0 ? spec.MaxLayers : int.MaxValue);
        int maxHeight   = (int)Math.Floor(dims.H / spec.H);
        int layerLimit  = Math.Min(maxLayers, maxHeight);

        int packed       = 0;
        int stackIndex   = initialStackIndex;
        int fullStacks   = 0;
        int partialBoxes = 0;

        int fullBpl = CountLayerCapacity(spec.PatternA, spec, dims, startY);

        while (packed < requested)
        {
            double stackY = startY + (stackIndex - initialStackIndex) * stackDepth;
            if (stackY >= dims.L) break;
            if (CountLayerCapacity(spec.PatternA, spec, dims, stackY) < fullBpl) break;

            bool flipStart    = (stackIndex % 2 == 1) && spec.PatternB is { Length: > 0 };
            int  beforeStack  = packed;
            int  layersPlaced = 0;

            for (int layer = 0; layer < layerLimit && packed < requested; layer++)
            {
                double z     = layer * spec.H;
                bool   useA  = flipStart ? (layer % 2 == 1) : (layer % 2 == 0);
                var sections = useA ? spec.PatternA : (spec.PatternB ?? spec.PatternA);

                int capacity = CountLayerCapacity(sections!, spec, dims, stackY);
                if (capacity <= 0) break;
                if (requested - packed < capacity) break; // not enough for a full layer

                int n = PlaceLayerAt(sections, spec, dims, stackY, z, capacity, productIndex, placements, stackIndex, layer);
                if (n < 0) break;
                packed += n;
                layersPlaced++;
            }

            if (layersPlaced == 0) break; // can't start any stack from here

            if (layerLimit > 0 && layersPlaced == layerLimit)
                fullStacks++;
            else if (layersPlaced > 0)
                partialBoxes = packed - beforeStack;

            stackIndex++;
        }

        return new(packed, startY + (stackIndex - initialStackIndex) * stackDepth, fullStacks, partialBoxes);
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

    private static int[] ComputeEffectiveLayers(
        ContainerDims dims, IReadOnlyList<(ProductSpec Spec, int Qty)> requests)
    {
        int n   = requests.Count;
        var eff  = new int[n];
        var data = new (int bpl, double sd, int stacks)[n];

        for (int i = 0; i < n; i++)
        {
            var (spec, qty) = requests[i];
            int maxL = spec.MaxLayers > 0 ? spec.MaxLayers : (int)Math.Floor(dims.H / spec.H);
            eff[i] = maxL;
            if (spec.PatternA is not { Length: > 0 }) continue;

            double sd  = LayerDepth(spec.PatternA, spec.W, spec.L);
            int    bpl = CountLayerCapacity(spec.PatternA, spec, dims, 0);
            if (sd <= 0 || bpl <= 0) continue;

            int stacks = (int)Math.Ceiling((double)qty / (bpl * maxL));
            data[i] = (bpl, sd, stacks);
        }

        double totalY  = data.Sum(d => d.stacks * d.sd);
        double targetY = dims.L - 50.0;
        if (totalY <= 0 || totalY >= targetY) return eff;
        int activeCount = data.Count(d => d.bpl > 0 && d.sd > 0 && d.stacks > 0);
        if (activeCount >= 2)
        {
            // Full layers placeable per product (floor: only complete layers count).
            var totalLayers = new int[n];
            for (int i = 0; i < n; i++)
                if (data[i].bpl > 0)
                    totalLayers[i] = requests[i].Qty / data[i].bpl;

            // Snap points = integer multiples of each product's physical height.
            // Iterating ascending finds the smallest T_h where requiredY ≤ targetY,
            // maximising Y fill while keeping all stack heights near the same physical target.
            var snapSet = new SortedSet<double>();
            for (int i = 0; i < n; i++)
            {
                if (data[i].bpl <= 0 || data[i].sd <= 0 || totalLayers[i] <= 0) continue;
                double h = requests[i].Spec.H;
                for (int k = 1; k <= eff[i]; k++)
                    snapSet.Add(Math.Round(k * h, 4));
            }

            foreach (double Th in snapSet)
            {
                double requiredY = 0;
                var trialEff = new int[n];
                for (int i = 0; i < n; i++)
                {
                    trialEff[i] = eff[i];
                    if (data[i].bpl <= 0 || data[i].sd <= 0 || totalLayers[i] <= 0) continue;
                    int ei = Math.Max(1, Math.Min(eff[i],
                        (int)Math.Floor(Th / requests[i].Spec.H)));
                    trialEff[i] = ei;
                    requiredY  += (int)Math.Ceiling((double)totalLayers[i] / ei) * data[i].sd;
                }
                if (requiredY <= targetY)
                {
                    Array.Copy(trialEff, eff, n);
                    return eff;
                }
            }

            return eff;
        }

        double scale = targetY / totalY;
        for (int i = 0; i < n; i++)
        {
            var (bpl, sd, stacks) = data[i];
            if (bpl <= 0 || sd <= 0 || stacks <= 0) continue;

            // Count stacks that fit without Y-clipping — same guard as PlaceProduct.
            int maxFull = 0;
            for (double y = 0; y < dims.L; y += sd)
            {
                if (CountLayerCapacity(requests[i].Spec.PatternA!, requests[i].Spec, dims, y) < bpl) break;
                maxFull++;
            }

            int newStacks = Math.Min((int)Math.Ceiling(stacks * scale), maxFull > 0 ? maxFull : stacks);
            int newL      = (int)Math.Ceiling((double)requests[i].Qty / (newStacks * bpl));
            eff[i] = Math.Max(1, Math.Min(newL, eff[i]));
        }
        return eff;
    }

    private static void RunLayerBalancing(
        List<PackInfo> packInfos, ContainerDims dims,
        List<BoxPlacement> placements)
    {
        foreach (var info in packInfos)
        {
            if (!info.HasPattern) continue;

            bool changed = true;
            while (changed)
            {
                changed = false;

                var stacks = placements
                    .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase)
                    .GroupBy(p => p.StackIndex)
                    .Select(g => (SI: g.Key,
                                  Height: g.Max(p => p.LayerIndex) + 1,
                                  StackY: g.Min(p => p.Y)))
                    .OrderBy(x => x.SI)
                    .ToList();

                if (stacks.Count < 2) break;

                int maxH = stacks.Max(x => x.Height);
                int minH = stacks.Min(x => x.Height);
                if (maxH - minH <= 1) break;

                var tall = stacks.Last(x => x.Height == maxH);
                var low  = stacks.First(x => x.Height == minH);

                int removeLayer = tall.Height - 1;
                int removedCount = placements.Count(p =>
                    p.ProductIndex == info.ProductIndex &&
                    p.StackIndex   == tall.SI &&
                    p.LayerIndex   == removeLayer);
                if (removedCount == 0) break;

                int nextLayer = low.Height;
                double z = nextLayer * info.Spec.H;
                if (z + info.Spec.H > dims.H + 0.01) break;

                bool flipIt  = (low.SI % 2 == 1) && info.Spec.PatternB is { Length: > 0 };
                bool useA    = flipIt ? (nextLayer % 2 == 1) : (nextLayer % 2 == 0);
                var sections = useA ? info.Spec.PatternA! : (info.Spec.PatternB ?? info.Spec.PatternA)!;

                int capacity = CountLayerCapacity(sections, info.Spec, dims, low.StackY);
                if (capacity != removedCount) break;

                placements.RemoveAll(p =>
                    p.ProductIndex == info.ProductIndex &&
                    p.StackIndex   == tall.SI &&
                    p.LayerIndex   == removeLayer);

                PlaceLayerAt(sections, info.Spec, dims, low.StackY, z, capacity,
                             info.ProductIndex, placements, low.SI, nextLayer);
                changed = true;
            }
        }
    }


    private static void SortStacksByHeight(
        List<PackInfo> packInfos, List<BoxPlacement> placements, ContainerDims dims)
    {
        foreach (var info in packInfos)
        {
            if (!info.HasPattern) continue;

            var stacks = placements
                .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase)
                .GroupBy(p => p.StackIndex)
                .Select(g => (SI: g.Key, Height: g.Max(p => p.LayerIndex) + 1, MinY: g.Min(p => p.Y)))
                .OrderBy(x => x.MinY)
                .ToList();

            if (stacks.Count < 2) continue;

            var ySlots  = stacks.Select(x => x.MinY).ToList();
            var caps    = ySlots.Select(y => CountLayerCapacity(info.Spec.PatternA!, info.Spec, dims, y)).ToList();
            var sorted  = stacks.OrderBy(x => x.Height).ToList(); // short → tall (tall goes to highest Y)

            if (stacks.Select(x => x.SI).SequenceEqual(sorted.Select(x => x.SI))) continue;

            // Only remap when every stack lands at a slot with the same layer capacity.
            var remapY = new Dictionary<int, double>();
            for (int i = 0; i < sorted.Count; i++)
            {
                int srcSI   = sorted[i].SI;
                double newY = ySlots[i];
                double oldY = stacks.First(x => x.SI == srcSI).MinY;
                int oldCap  = CountLayerCapacity(info.Spec.PatternA!, info.Spec, dims, oldY);
                if (caps[i] != oldCap) continue;
                if (Math.Abs(newY - oldY) > 0.001)
                    remapY[srcSI] = newY;
            }

            if (remapY.Count == 0) continue;

            for (int j = 0; j < placements.Count; j++)
            {
                var p = placements[j];
                if (p.ProductIndex != info.ProductIndex || p.StackIndex >= CondoStackBase) continue;
                if (!remapY.TryGetValue(p.StackIndex, out double newMinY)) continue;
                double oldMinY = stacks.First(x => x.SI == p.StackIndex).MinY;
                placements[j] = p with { Y = newMinY + (p.Y - oldMinY) };
            }
        }
    }

    // Returns a sortable magnitude from content strings like "1000 ML", "450 ML", "150 G", "19.5 G".
    private static int CondoCols(ProductSpec spec, ContainerDims dims)
    {
        int condoCount = spec.CondoCount > 0
            ? spec.CondoCount
            : (int)Math.Floor(dims.W / spec.W);
        return Math.Min(condoCount, (int)Math.Floor(dims.W / spec.W));
    }
}
