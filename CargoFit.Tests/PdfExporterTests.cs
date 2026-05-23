using System.Collections.Generic;
using System.IO;
using CargoFit;
using Xunit.Abstractions;

namespace CargoFit.Tests;

public class PdfExporterTests
{
    private readonly ITestOutputHelper _log;
    public PdfExporterTests(ITestOutputHelper log) => _log = log;

    [Fact]
    public void Generate_FourProducts40HC_ProducesNonEmptyPdf()
    {
        var container = TestHelpers.Container40HC();

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

        var requests = new List<(ProductSpec Spec, int Qty)>
        {
            (aloe365,   200),
            (mogu320,  1100),
            (moguTea450, 340),
            (mogu1000,  625),
        };
        var output = PackingEngine.Calculate(container, requests);

        byte[] pdf = PdfExporter.Generate(container, requests, output);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 1000, $"PDF too small: {pdf.Length} bytes");
        Assert.Equal((byte)'%', pdf[0]);
        Assert.Equal((byte)'P', pdf[1]);
        Assert.Equal((byte)'D', pdf[2]);
        Assert.Equal((byte)'F', pdf[3]);

        string outPath = Path.Combine(Path.GetTempPath(), "logistic_layercard_smoke.pdf");
        File.WriteAllBytes(outPath, pdf);
        _log.WriteLine($"PDF written to {outPath} ({pdf.Length} bytes)");
    }
}
