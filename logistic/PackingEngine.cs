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

        var effectiveLayers = ComputeEffectiveLayers(dims, requests);
        var packInfos = RunPrimaryPacking(dims, requests, placements, out double currentY, effectiveLayers);
        RunBalancing(packInfos, dims, placements, ref currentY, effectiveLayers);
        RunLayerBalancing(packInfos, dims, placements);
        RunPartialRemoval(packInfos, dims, placements, ref currentY);
        var condoMap  = RunCondoPlacement(packInfos, dims, currentY, placements);

        return new PackingOutput(placements, packInfos, new Dictionary<int,int>(), condoMap);
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
        double condoY = condoAreaStart;
        var condoMap  = new Dictionary<int,int>();

        // Sort H descending: tallest product is the base layer (Z=0).
        var withRem = packInfos
            .Where(info => info.HasPattern && info.Requested - info.Result.Packed > 0)
            .Select(info => (info, rem: info.Requested - info.Result.Packed))
            .OrderByDescending(x => x.info.Spec.H)
            .ToList();

        if (withRem.Count == 0) return condoMap;

        // Assign cumulative Z offsets: tallest at Z=0, next on top, etc.
        var zBases  = new double[packInfos.Count];
        var colsMap = new int[packInfos.Count];
        double accumZ = 0;
        foreach (var (info, _) in withRem)
        {
            zBases[info.ProductIndex]  = accumZ;
            colsMap[info.ProductIndex] = CondoCols(info.Spec, dims);
            accumZ += info.Spec.H;
        }

        // Track full rows placed per product (for partial calculation).
        var rowsPlaced = new int[packInfos.Count];

        // Phase 1: combined full rows — all products share the same Y positions,
        // each placed at its Z offset. Advance condoY once per combined row.
        int maxRows = withRem.Max(x => colsMap[x.info.ProductIndex] > 0
            ? x.rem / colsMap[x.info.ProductIndex] : 0);

        var towerYs = new List<double>();

        for (int row = 0; row < maxRows; row++)
        {
            if (condoY >= dims.L) break;
            double rowY      = condoY;
            double rowAdvance = 0;
            bool   anyPlaced = false;

            foreach (var (info, rem) in withRem)
            {
                int cols = colsMap[info.ProductIndex];
                if (cols <= 0) continue;
                if (row >= rem / cols) continue;         // this product has no row here

                double z = zBases[info.ProductIndex];
                if (rowY + info.Spec.L > dims.L + 0.01) break;
                if (z + info.Spec.H > dims.H + 0.01) continue;

                for (int col = 0; col < cols; col++)
                    placements.Add(new BoxPlacement(
                        col * info.Spec.W, rowY, z,
                        info.Spec.W, info.Spec.L, info.Spec.H,
                        info.ProductIndex, false, CondoStackBase + info.ProductIndex, 0));

                condoMap[info.ProductIndex] = condoMap.GetValueOrDefault(info.ProductIndex) + cols;
                rowsPlaced[info.ProductIndex]++;
                rowAdvance = Math.Max(rowAdvance, info.Spec.L);
                anyPlaced  = true;
            }

            if (anyPlaced)
            {
                towerYs.Add(rowY);
                condoY += rowAdvance;
            }
        }

        // Phase 2: remaining partial boxes.
        // Prefer stacking at an existing tower Y position where this product hasn't placed yet.
        // Fallback: new row at current condoY at this product's Z base.
        foreach (var (info, rem) in withRem)
        {
            int cols = colsMap[info.ProductIndex];
            if (cols <= 0) continue;

            int partial = rem - rowsPlaced[info.ProductIndex] * cols;
            if (partial <= 0) continue;

            int    condoSI = CondoStackBase + info.ProductIndex;
            double z       = zBases[info.ProductIndex];
            if (z + info.Spec.H > dims.H + 0.01) continue;

            var productYs = placements
                .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex == condoSI)
                .Select(p => p.Y).ToHashSet();

            int placed = 0;
            foreach (double towerY in towerYs)
            {
                if (placed >= partial) break;
                if (productYs.Contains(towerY)) continue;
                if (towerY + info.Spec.L > dims.L + 0.01) continue;

                int canPlace = Math.Min(cols, partial - placed);
                for (int col = 0; col < canPlace; col++)
                    placements.Add(new BoxPlacement(
                        col * info.Spec.W, towerY, z,
                        info.Spec.W, info.Spec.L, info.Spec.H,
                        info.ProductIndex, false, condoSI, 0));
                placed += canPlace;
            }

            // Fallback: new row at condoY
            if (placed < partial && condoY + info.Spec.L <= dims.L + 0.01)
            {
                int canPlace = Math.Min(partial - placed, cols);
                for (int col = 0; col < canPlace; col++)
                    placements.Add(new BoxPlacement(
                        col * info.Spec.W, condoY, z,
                        info.Spec.W, info.Spec.L, info.Spec.H,
                        info.ProductIndex, false, condoSI, 0));
                placed   += canPlace;
                condoY   += info.Spec.L;
            }

            if (placed > 0)
                condoMap[info.ProductIndex] = condoMap.GetValueOrDefault(info.ProductIndex) + placed;
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
            var totalLayers = new int[n];
            for (int i = 0; i < n; i++)
                if (data[i].bpl > 0)
                    totalLayers[i] = (int)Math.Ceiling((double)requests[i].Qty / data[i].bpl);

            int maxT = eff.Max();
            for (int T = maxT; T >= 1; T--)
            {
                bool feasible = true;
                double requiredY = 0;
                for (int i = 0; i < n; i++)
                {
                    if (data[i].bpl <= 0 || data[i].sd <= 0) continue;
                    if (T > eff[i]) { feasible = false; break; }
                    int nMin = (int)Math.Ceiling((double)totalLayers[i] / (T + 1));
                    int nMax = (int)Math.Floor((double)totalLayers[i] / T);
                    if (nMin > nMax) { feasible = false; break; }
                    requiredY += nMin * data[i].sd;
                }
                if (feasible && requiredY <= targetY)
                {
                    for (int i = 0; i < n; i++)
                        if (data[i].bpl > 0) eff[i] = Math.Min(T + 1, eff[i]);
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


    private static int CondoCols(ProductSpec spec, ContainerDims dims)
    {
        int condoCount = spec.CondoCount > 0
            ? spec.CondoCount
            : (int)Math.Floor(dims.W / spec.W);
        return Math.Min(condoCount, (int)Math.Floor(dims.W / spec.W));
    }
}
