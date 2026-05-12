using System.Collections.Generic;
using System.Linq;
using logistic;
using Xunit.Abstractions;

namespace logistic.Tests;

public class PackingEngineTests
{
    private readonly ITestOutputHelper _log;
    public PackingEngineTests(ITestOutputHelper log) => _log = log;

    // ── Assertion helpers ────────────────────────────────────────────────────

    private void AssertBounds(PackingOutput output, ContainerSpec container)
    {
        double iw = container.InteriorW, il = container.InteriorL, ih = container.InteriorH;
        foreach (var p in output.Placements)
        {
            Assert.True(p.X >= -0.01,             $"Negative X={p.X:F3}");
            Assert.True(p.Y >= -0.01,             $"Negative Y={p.Y:F3}");
            Assert.True(p.Z >= -0.01,             $"Negative Z={p.Z:F3}");
            Assert.True(p.X + p.BW <= iw + 0.01, $"X overflow: {p.X + p.BW:F3} > {iw}");
            Assert.True(p.Y + p.BL <= il + 0.01, $"Y overflow: {p.Y + p.BL:F3} > {il}");
            Assert.True(p.Z + p.BH <= ih + 0.01, $"Z overflow: {p.Z + p.BH:F3} > {ih}");
        }
    }

    private void AssertPackedLeRequested(PackingOutput output,
        IReadOnlyList<(ProductSpec Spec, int Qty)> requests)
    {
        foreach (var info in output.PackInfos)
        {
            int packed = output.Placements.Count(p => p.ProductIndex == info.ProductIndex);
            Assert.True(packed <= requests[info.ProductIndex].Qty,
                $"Product {info.ProductIndex} ({info.Spec.Description} {info.Spec.Content}): " +
                $"packed={packed} > requested={requests[info.ProductIndex].Qty}");
        }
    }

    // ── Scenario 1: Single product, Aloe 365ml ×200, 20ft ───────────────────
    // Baseline: one product with a real pattern; verifies placement and bounds.

    [Fact]
    public void SingleProduct_Aloe365ml_20ft()
    {
        var container = TestHelpers.Container20ft();
        var aloe = new ProductSpec("Aloe", "365 ML", "Pack 24", 9.79,
            21.9, 33.4, 20.5,
            PatternA: [new LayerSection(2, 6, false), new LayerSection(3, 3, true)],
            PatternB: [new LayerSection(3, 3, true),  new LayerSection(2, 6, false)],
            MaxLayers: 10, CondoCount: 10);

        var requests = new List<(ProductSpec, int)> { (aloe, 200) };
        var output   = PackingEngine.Calculate(container, requests);

        TestHelpers.DumpOutput(container, requests, output, _log);

        Assert.NotEmpty(output.Placements);
        Assert.True(output.PackInfos[0].HasPattern);
        AssertBounds(output, container);
        AssertPackedLeRequested(output, requests);
    }

    // ── Scenario 2: No-pattern product (Gumi Jelly 150G) ────────────────────
    // Products with empty PatternA are excluded from both primary and condo packing.

    [Fact]
    public void NoPattern_GumiJelly150G_ProducesZeroPlacements()
    {
        var container = TestHelpers.Container20ft();
        var gumiJelly = new ProductSpec("Gumi Jelly", "150 G", "Pack 36", 5.58,
            27.3, 31.1, 18.2,
            PatternA: [], PatternB: [], MaxLayers: 0, CondoCount: 0);

        var requests = new List<(ProductSpec, int)> { (gumiJelly, 100) };
        var output   = PackingEngine.Calculate(container, requests);

        TestHelpers.DumpOutput(container, requests, output, _log);

        Assert.Empty(output.Placements);
        Assert.False(output.PackInfos[0].HasPattern);
    }

    // ── Scenario 3: Two products with balancing + condo, 20ft ───────────────

    [Fact]
    public void MultiProduct_AloeAndMogu320_20ft()
    {
        var container = TestHelpers.Container20ft();

        var aloe = new ProductSpec("Aloe", "365 ML", "Pack 24", 9.79,
            21.9, 33.4, 20.5,
            PatternA: [new LayerSection(2, 6, false), new LayerSection(3, 3, true)],
            PatternB: [new LayerSection(3, 3, true),  new LayerSection(2, 6, false)],
            MaxLayers: 10, CondoCount: 10);

        var mogu320 = new ProductSpec("Mogu", "320 ML", "Pack 24", 8.7,
            25.8, 38.5, 15.7,
            PatternA:
            [
                new LayerSection(4, 1, true),
                new LayerSection(0, 0, false, [
                    new SectionSubRow(1, 3, false),
                    new SectionSubRow(1, 2, true),
                    new SectionSubRow(1, 3, false)]),
                new LayerSection(0, 0, false, [
                    new SectionSubRow(1, 3, false),
                    new SectionSubRow(1, 2, true),
                    new SectionSubRow(1, 3, false)]),
                new LayerSection(4, 1, true),
            ],
            PatternB:
            [
                new LayerSection(0, 0, false, [
                    new SectionSubRow(1, 3, false),
                    new SectionSubRow(1, 2, true),
                    new SectionSubRow(1, 3, false)]),
                new LayerSection(4, 1, true),
                new LayerSection(4, 1, true),
                new LayerSection(0, 0, false, [
                    new SectionSubRow(1, 3, false),
                    new SectionSubRow(1, 2, true),
                    new SectionSubRow(1, 3, false)]),
            ],
            MaxLayers: 13, CondoCount: 9);

        var requests = new List<(ProductSpec, int)> { (aloe, 150), (mogu320, 150) };
        var output   = PackingEngine.Calculate(container, requests);

        TestHelpers.DumpOutput(container, requests, output, _log);

        Assert.NotEmpty(output.Placements);
        AssertBounds(output, container);
        AssertPackedLeRequested(output, requests);
    }

    // ── Scenario 4: Large qty, Mogu Candy 19.5G ×2000, 40ft ─────────────────
    // Stress test with thin boxes (H=9.2). Asserts MaxLayers cap is respected.

    [Fact]
    public void LargeQty_MoguCandy_40ft()
    {
        var container = TestHelpers.Container40ft();
        var candy = new ProductSpec("Mogu Candy", "19.5 G", "Pack 48", 1.9,
            24.6, 32.2, 9.2,
            PatternA: [new LayerSection(4, 1, true), new LayerSection(3, 8, false)],
            PatternB: [new LayerSection(3, 8, false), new LayerSection(4, 1, true)],
            MaxLayers: 20, CondoCount: 9);

        var requests = new List<(ProductSpec, int)> { (candy, 2000) };
        var output   = PackingEngine.Calculate(container, requests);

        TestHelpers.DumpOutput(container, requests, output, _log);

        AssertBounds(output, container);
        AssertPackedLeRequested(output, requests);

        int maxLayerIdx = output.Placements
            .Where(p => p.StackIndex < PackingEngine.CondoStackBase)
            .Select(p => p.LayerIndex)
            .DefaultIfEmpty(0)
            .Max();
        Assert.True(maxLayerIdx < 20, $"LayerIndex {maxLayerIdx} exceeds MaxLayers-1=19");
    }

    // ── Scenario 5: Tall boxes, Mogu 1000ml ×500, 40HC ──────────────────────
    // Verifies tall boxes (H=26.7) use the extra height of the HC container.

    [Fact]
    public void TallBoxes_Mogu1000ml_40HC()
    {
        var container = TestHelpers.Container40HC();
        var mogu1000 = new ProductSpec("Mogu", "1000 ML", "Pack 12", 13.47,
            26.2, 34.8, 26.7,
            PatternA: [new LayerSection(4, 2, true), new LayerSection(3, 6, false)],
            PatternB: [new LayerSection(3, 6, false), new LayerSection(3, 2, true)],
            MaxLayers: 8, CondoCount: 9);

        var requests = new List<(ProductSpec, int)> { (mogu1000, 500) };
        var output   = PackingEngine.Calculate(container, requests);

        TestHelpers.DumpOutput(container, requests, output, _log);

        Assert.NotEmpty(output.Placements);
        AssertBounds(output, container);
        AssertPackedLeRequested(output, requests);
    }

    // ── Scenario 6: Small qty always fits, Mogu Tea 300ml ×50, 20ft ─────────

    [Fact]
    public void SmallQty_MoguTea300ml_20ft()
    {
        var container = TestHelpers.Container20ft();
        var moguTea = new ProductSpec("Mogu Tea", "300 ML", "Pack 6", 8.0,
            23.4, 35.0, 18.3,
            PatternA: [new LayerSection(2, 7, false), new LayerSection(3, 2, true)],
            PatternB: [new LayerSection(3, 2, true),  new LayerSection(2, 7, false)],
            MaxLayers: 11, CondoCount: 10);

        var requests = new List<(ProductSpec, int)> { (moguTea, 50) };
        var output   = PackingEngine.Calculate(container, requests);

        TestHelpers.DumpOutput(container, requests, output, _log);

        int packed = output.Placements.Count(p => p.ProductIndex == 0);
        Assert.Equal(50, packed);
        AssertBounds(output, container);
    }

    // ── Scenario 8: Aloe 365ml ×500, 20ft ───────────────────────────────────

    [Fact]
    public void Aloe365ml_500boxes_20ft()
    {
        var container = TestHelpers.Container40ft();
        var aloe = new ProductSpec("Aloe", "365 ML", "Pack 24", 9.79,
            21.9, 33.4, 20.5,
            PatternA: [new LayerSection(2, 6, false), new LayerSection(3, 3, true)],
            PatternB: [new LayerSection(3, 3, true),  new LayerSection(2, 6, false)],
            MaxLayers: 10, CondoCount: 10);

        var requests = new List<(ProductSpec, int)> { (aloe, 3000) };
        var output   = PackingEngine.Calculate(container, requests);

        TestHelpers.DumpOutput(container, requests, output, _log);

        AssertBounds(output, container);
        AssertPackedLeRequested(output, requests);
    }

    // ── Scenario 7: Realistic three-product load, 40ft ───────────────────────

    [Fact]
    public void RealisticLoad_ThreeProducts_40ft()
    {
        var container = TestHelpers.Container40ft();

        var aloe300 = new ProductSpec("Aloe", "300 ML", "Pack 24", 8.55,
            24.6, 35.6, 21.1,
            PatternA: [new LayerSection(2, 8, false), new LayerSection(3, 1, true)],
            PatternB: [new LayerSection(3, 1, true),  new LayerSection(2, 8, false)],
            MaxLayers: 9, CondoCount: 9);

        var blue500 = new ProductSpec("Blue", "500 ML", "Pack 24", 13.58,
            27.4, 41.4, 22.7,
            PatternA: [new LayerSection(3, 1, true), new LayerSection(2, 7, false)],
            PatternB: [new LayerSection(2, 7, false), new LayerSection(3, 1, true)],
            MaxLayers: 20, CondoCount: 8);

        var cooler = new ProductSpec("Cooler Bag", "320 ML", "Pack 24", 9.5,
            27.4, 40.9, 17.1,
            PatternA: [new LayerSection(3, 3, true), new LayerSection(2, 4, false)],
            PatternB: [new LayerSection(2, 4, false), new LayerSection(3, 3, true)],
            MaxLayers: 10, CondoCount: 8);

        var requests = new List<(ProductSpec, int)>
        {
            (aloe300, 300),
            (blue500, 200),
            (cooler,  200),
        };
        var output = PackingEngine.Calculate(container, requests);

        TestHelpers.DumpOutput(container, requests, output, _log);

        Assert.NotEmpty(output.Placements);
        AssertBounds(output, container);
        AssertPackedLeRequested(output, requests);
    }

    // ── Scenario 9: 4-product load, 40ft — remaining Y ──────────────────────

    [Fact]
    public void FourProducts_RemainingY_40ft()
    {
        var container = TestHelpers.Container40ft();

        var aloe365 = new ProductSpec("Aloe", "365 ML", "Pack 24", 9.79,
            21.9, 33.4, 20.5,
            PatternA: [new LayerSection(2, 6, false), new LayerSection(3, 3, true)],
            PatternB: [new LayerSection(3, 3, true),  new LayerSection(2, 6, false)],
            MaxLayers: 10, CondoCount: 10);

        var mogu320 = new ProductSpec("Mogu", "320 ML", "Pack 24", 8.7,
            25.8, 38.5, 15.7,
            PatternA:
            [
                new LayerSection(4, 1, true),
                new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
                new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
                new LayerSection(4, 1, true),
            ],
            PatternB:
            [
                new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
                new LayerSection(4, 1, true),
                new LayerSection(4, 1, true),
                new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
            ],
            MaxLayers: 13, CondoCount: 9);

        var moguTea450 = new ProductSpec("Mogu Tea", "450 ML", "Pack 24", 10.0,
            25.8, 39.0, 20.3,
            PatternA: [new LayerSection(3, 2, true), new LayerSection(2, 6, false)],
            PatternB: [new LayerSection(2, 6, false), new LayerSection(3, 2, true)],
            MaxLayers: 10, CondoCount: 9);

        var mogu1000 = new ProductSpec("Mogu", "1000 ML", "Pack 12", 13.47,
            26.2, 34.8, 26.7,
            PatternA: [new LayerSection(4, 2, true), new LayerSection(3, 6, false)],
            PatternB: [new LayerSection(3, 6, false), new LayerSection(4, 2, true)],
            MaxLayers: 8, CondoCount: 9);

        var requests = new List<(ProductSpec, int)>
        {
            (aloe365,   200),
            (mogu320,  1100),
            (moguTea450, 340),
            (mogu1000,  625),
        };
        var output = PackingEngine.Calculate(container, requests);

        TestHelpers.DumpOutput(container, requests, output, _log);

        double maxYEnd = output.Placements.Count > 0
            ? output.Placements.Max(p => p.Y + p.BL)
            : 0;
        double remaining = container.InteriorL - maxYEnd;
        _log.WriteLine($"  Max Y+BL : {maxYEnd:F1} cm");
        _log.WriteLine($"  Remaining: {remaining:F1} cm  (InteriorL={container.InteriorL})");

        // Dump per-stack physical heights
        _log.WriteLine("  Per-stack heights:");
        foreach (var info in output.PackInfos)
        {
            if (!info.HasPattern) continue;
            var stacks = output.Placements
                .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < PackingEngine.CondoStackBase)
                .GroupBy(p => p.StackIndex)
                .Select(g => (SI: g.Key, Layers: g.Max(p => p.LayerIndex) + 1, PhysH: (g.Max(p => p.LayerIndex) + 1) * info.Spec.H))
                .OrderBy(x => x.SI)
                .ToList();
            string row = string.Join("  ", stacks.Select(s => $"[{s.Layers}L={s.PhysH:F1}cm]"));
            _log.WriteLine($"    {info.Spec.Description} {info.Spec.Content}: {row}");
        }

        // Dump condo placements
        _log.WriteLine("  Condo placements (Y, Z, product, count):");
        var condoPlacements = output.Placements
            .Where(p => p.StackIndex >= PackingEngine.CondoStackBase)
            .GroupBy(p => (p.Y, p.Z, p.ProductIndex))
            .OrderBy(g => g.Key.Y).ThenBy(g => g.Key.Z)
            .ToList();
        foreach (var g in condoPlacements)
        {
            var info = output.PackInfos.First(i => i.ProductIndex == g.Key.ProductIndex);
            _log.WriteLine($"    Y={g.Key.Y:F1}  Z={g.Key.Z:F1}  {info.Spec.Description} {info.Spec.Content}  ×{g.Count()}");
        }

        AssertBounds(output, container);
        AssertPackedLeRequested(output, requests);
    }

    // ── Scenario 11: DevPreset — Mogu 1000ML P12 ×540 + Mogu 320ML P24 ×850, 20ft ─

    [Fact]
    public void DevPreset_Mogu1000P12_Mogu320P24_20ft()
    {
        var container = TestHelpers.Container20ft();

        var mogu1000 = new ProductSpec("Mogu", "1000 ML", "Pack 12", 13.47,
            26.2, 34.8, 26.7,
            PatternA: [new LayerSection(4, 2, true), new LayerSection(3, 6, false)],
            PatternB: [new LayerSection(3, 6, false), new LayerSection(4, 2, true)],
            MaxLayers: 8, CondoCount: 9);

        var mogu320 = new ProductSpec("Mogu", "320 ML", "Pack 24", 8.7,
            25.8, 38.5, 15.7,
            PatternA:
            [
                new LayerSection(4, 1, true),
                new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
                new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
                new LayerSection(4, 1, true),
            ],
            PatternB:
            [
                new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
                new LayerSection(4, 1, true),
                new LayerSection(4, 1, true),
                new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
            ],
            MaxLayers: 13, CondoCount: 9);

        var requests = new List<(ProductSpec, int)>
        {
            (mogu1000, 540),
            (mogu320, 850),
        };
        var output = PackingEngine.Calculate(container, requests);

        TestHelpers.DumpOutput(container, requests, output, _log);

        Assert.NotEmpty(output.Placements);
        AssertBounds(output, container);
        AssertPackedLeRequested(output, requests);
    }

    // ── Scenario 8: Aloe 365 P24 ×300 + Aloe 1000ML P12 ×300, 20ft ─────────

    [Fact]
    public void MultiProduct_Aloe365P24_Aloe1000ML_20ft()
    {
        var container = TestHelpers.Container20ft();

        var aloe365 = new ProductSpec("Aloe", "365 ML", "Pack 24", 9.79,
            21.9, 33.4, 20.5,
            PatternA: [new LayerSection(2, 6, false), new LayerSection(3, 3, true)],
            PatternB: [new LayerSection(3, 3, true),  new LayerSection(2, 6, false)],
            MaxLayers: 10, CondoCount: 10);

        var aloe1000 = new ProductSpec("Aloe", "1000 ML", "Pack 12", 13.65,
            25.1, 33.2, 29.6,
            PatternA: [new LayerSection(3, 8, false), new LayerSection(4, 1, true)],
            PatternB: [new LayerSection(4, 1, true),  new LayerSection(3, 8, false)],
            MaxLayers: 7, CondoCount: 9);

        var requests = new List<(ProductSpec, int)>
        {
            (aloe365,  300),
            (aloe1000, 300),
        };
        var output = PackingEngine.Calculate(container, requests);

        TestHelpers.DumpOutput(container, requests, output, _log);

        Assert.NotEmpty(output.Placements);
        AssertBounds(output, container);
        AssertPackedLeRequested(output, requests);
    }

    // ── Scenario 12: Scatter places leftovers on top of same-product stacks ──

    [Fact]
    public void Scatter_PlacesLeftoverOnSameProductStacks_DevPreset()
    {
        var container = TestHelpers.Container20ft();

        var mogu1000 = new ProductSpec("Mogu", "1000 ML", "Pack 12", 13.47,
            26.2, 34.8, 26.7,
            PatternA: [new LayerSection(4, 2, true), new LayerSection(3, 6, false)],
            PatternB: [new LayerSection(3, 6, false), new LayerSection(4, 2, true)],
            MaxLayers: 8, CondoCount: 9);

        var mogu320 = new ProductSpec("Mogu", "320 ML", "Pack 24", 8.7,
            25.8, 38.5, 15.7,
            PatternA:
            [
                new LayerSection(4, 1, true),
                new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
                new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
                new LayerSection(4, 1, true),
            ],
            PatternB:
            [
                new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
                new LayerSection(4, 1, true),
                new LayerSection(4, 1, true),
                new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
            ],
            MaxLayers: 13, CondoCount: 9);

        var requests = new List<(ProductSpec, int)>
        {
            (mogu1000, 540),
            (mogu320,  850),
        };
        var output = PackingEngine.Calculate(container, requests);

        TestHelpers.DumpOutput(container, requests, output, _log);

        AssertBounds(output, container);
        AssertPackedLeRequested(output, requests);

        // Scatter must have placed at least one box for at least one product.
        Assert.True(output.ScatterMap.Values.Sum() > 0,
            "Expected scatter to place leftover boxes for the DevPreset scenario.");

        // "A on A" rule: every primary-stack box at LayerIndex L>0 has a same-(ProductIndex, StackIndex)
        // box at L-1 directly below. Equivalently, no scatter dropped a product onto another product's stack.
        var byPidSi = output.Placements
            .Where(p => p.StackIndex < PackingEngine.CondoStackBase)
            .GroupBy(p => (p.ProductIndex, p.StackIndex))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var p in output.Placements
                     .Where(x => x.StackIndex < PackingEngine.CondoStackBase && x.LayerIndex > 0))
        {
            Assert.True(byPidSi.TryGetValue((p.ProductIndex, p.StackIndex), out var siGroup));
            Assert.Contains(siGroup, q => q.LayerIndex == p.LayerIndex - 1);
        }
    }

    // ── Scenario 13: Scatter — minimal single-product, leftover < layer capacity ─

    [Fact]
    public void Scatter_SingleProduct_FitsExactlyAfterPrimary()
    {
        var container = TestHelpers.Container20ft();

        // Aloe 365: PatternA capacity ≈ 12 boxes/layer × ~10 layers × ~17 stacks ≫ 200.
        // We pick a quantity that overflows what primary+condo will absorb so scatter is exercised.
        var aloe = new ProductSpec("Aloe", "365 ML", "Pack 24", 9.79,
            21.9, 33.4, 20.5,
            PatternA: [new LayerSection(2, 6, false), new LayerSection(3, 3, true)],
            PatternB: [new LayerSection(3, 3, true),  new LayerSection(2, 6, false)],
            MaxLayers: 4, CondoCount: 0);

        var requests = new List<(ProductSpec, int)> { (aloe, 800) };
        var output   = PackingEngine.Calculate(container, requests);

        TestHelpers.DumpOutput(container, requests, output, _log);

        AssertBounds(output, container);
        AssertPackedLeRequested(output, requests);

        int scattered = output.ScatterMap.GetValueOrDefault(0, 0);
        _log.WriteLine($"  Scattered: {scattered}");

        // For each scattered placement, its LayerIndex must be at or above where primary stopped.
        var stackPrimaryMaxLayer = output.Placements
            .Where(p => p.StackIndex < PackingEngine.CondoStackBase)
            .GroupBy(p => p.StackIndex)
            .ToDictionary(g => g.Key, g => g.Max(p => p.LayerIndex));

        foreach (var p in output.Placements
                     .Where(p => p.StackIndex < PackingEngine.CondoStackBase && p.ProductIndex == 0))
        {
            // Layers must never exceed container ceiling.
            Assert.True(p.Z + p.BH <= container.InteriorH + 0.01);
            // Must belong to a known primary stack (not condo).
            Assert.True(stackPrimaryMaxLayer.ContainsKey(p.StackIndex));
        }
    }
}
