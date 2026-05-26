namespace CargoFit.Cli;

/// <summary>
/// JSON model สำหรับ test fixture file (เช่น testdata/devpreset.json)
/// Format:
/// {
///   "container": 1,
///   "calculate": true,
///   "products": [
///     { "index": 0, "qty": 200 },
///     { "index": 11, "qty": 1100 }
///   ]
/// }
/// </summary>
internal sealed class InputFixture
{
    public int              Container { get; set; }
    public bool             Calculate { get; set; } = true;
    public FixtureProduct[] Products  { get; set; } = [];
}

internal sealed class FixtureProduct
{
    public int Index { get; set; }
    public int Qty   { get; set; }
}
