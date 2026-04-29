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
        var aloe = new ProductSpec("Aloe", "365 ML", "Pack 24", 9.79, false, false,
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
        var gumiJelly = new ProductSpec("Gumi Jelly", "150 G", "Pack 36", 5.58, false, false,
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

        var aloe = new ProductSpec("Aloe", "365 ML", "Pack 24", 9.79, false, false,
            21.9, 33.4, 20.5,
            PatternA: [new LayerSection(2, 6, false), new LayerSection(3, 3, true)],
            PatternB: [new LayerSection(3, 3, true),  new LayerSection(2, 6, false)],
            MaxLayers: 10, CondoCount: 10);

        var mogu320 = new ProductSpec("Mogu", "320 ML", "Pack 24", 8.7, false, false,
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
        var candy = new ProductSpec("Mogu Candy", "19.5 G", "Pack 48", 1.9, false, false,
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
        var mogu1000 = new ProductSpec("Mogu", "1000 ML", "Pack 12", 13.47, false, false,
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
        var moguTea = new ProductSpec("Mogu Tea", "300 ML", "Pack 6", 8.0, false, false,
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
        var aloe = new ProductSpec("Aloe", "365 ML", "Pack 24", 9.79, false, false,
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

        var aloe300 = new ProductSpec("Aloe", "300 ML", "Pack 24", 8.55, false, false,
            24.6, 35.6, 21.1,
            PatternA: [new LayerSection(2, 8, false), new LayerSection(3, 1, true)],
            PatternB: [new LayerSection(3, 1, true),  new LayerSection(2, 8, false)],
            MaxLayers: 9, CondoCount: 9);

        var blue500 = new ProductSpec("Blue", "500 ML", "Pack 24", 13.58, false, false,
            27.4, 41.4, 22.7,
            PatternA: [new LayerSection(3, 1, true), new LayerSection(2, 7, false)],
            PatternB: [new LayerSection(2, 7, false), new LayerSection(3, 1, true)],
            MaxLayers: 20, CondoCount: 8);

        var cooler = new ProductSpec("Cooler Bag", "320 ML", "Pack 24", 9.5, false, false,
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
}
