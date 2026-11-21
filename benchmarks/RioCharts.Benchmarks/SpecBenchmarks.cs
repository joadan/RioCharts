using BenchmarkDotNet.Attributes;
using RioCharts.Core.Specs;

namespace RioCharts.Benchmarks;

/// <summary>
/// JSON chart specs (the AI path): parsing a spec, reading a CSV dataset and binding the spec's
/// fields, filter and time bucket to it.
/// </summary>
[MemoryDiagnoser]
public class SpecBenchmarks
{
    private const string Spec = """
        {
          "title": "Supply air temperature",
          "dataset": "ahu",
          "y": { "title": "°C", "format": "0.0" },
          "series": [
            { "type": "line", "x": "timestamp", "y": "supplyTemp", "groupBy": "unit", "timeBucket": "day", "aggregate": "average" }
          ]
        }
        """;

    private string _csv = null!;
    private Dictionary<string, ChartDataset> _datasets = null!;
    private SpecParseResult _parsed = null!;

    /// <summary>Rows in the dataset: hourly readings from two air handling units.</summary>
    [Params(1_000, 50_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var lines = Enumerable.Range(0, Rows).Select(i =>
            FormattableString.Invariant($"{Data.Start.AddHours(i / 2):yyyy-MM-ddTHH:mm:ss},AHU {i % 2 + 1},{Data.Supply(i / 2):0.00}"));
        _csv = "timestamp,unit,supplyTemp\n" + string.Join('\n', lines);
        _datasets = new() { ["ahu"] = ChartDataset.FromCsv("ahu", _csv) };
        _parsed = ChartSpecJson.Parse(Spec);
        if (!_parsed.Success || !ChartSpecBinder.Bind(_parsed.Spec!, _datasets).Success)
        {
            throw new InvalidOperationException("The benchmark spec does not bind: " + string.Join("; ", _parsed.Errors));
        }
    }

    [Benchmark]
    public SpecParseResult Parse() => ChartSpecJson.Parse(Spec);

    [Benchmark]
    public ChartDataset ReadCsv() => ChartDataset.FromCsv("ahu", _csv);

    [Benchmark]
    public SpecBindResult Bind() => ChartSpecBinder.Bind(_parsed.Spec!, _datasets);
}
