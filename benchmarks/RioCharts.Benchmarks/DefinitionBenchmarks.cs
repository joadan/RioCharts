using BenchmarkDotNet.Attributes;
using RioCharts.Core.Definitions;

namespace RioCharts.Benchmarks;

/// <summary>
/// JSON chart definitions (the AI path): parsing a definition, reading a CSV dataset and binding the definition's
/// fields, filter and time bucket to it.
/// </summary>
[MemoryDiagnoser]
public class SpecBenchmarks
{
    private const string Spec = """
        {
          "title": "Air temperature",
          "dataset": "weather",
          "y": { "title": "°C", "format": "0.0" },
          "series": [
            { "type": "line", "x": "timestamp", "y": "airTemp", "groupBy": "station", "timeBucket": "day", "aggregate": "average" }
          ]
        }
        """;

    private string _csv = null!;
    private Dictionary<string, ChartDataset> _datasets = null!;
    private DefinitionParseResult _parsed = null!;

    /// <summary>Rows in the dataset: hourly readings from two weather stations.</summary>
    [Params(1_000, 50_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var lines = Enumerable.Range(0, Rows).Select(i =>
            FormattableString.Invariant($"{Data.Start.AddHours(i / 2):yyyy-MM-ddTHH:mm:ss},Station {i % 2 + 1},{Data.London(i / 2):0.00}"));
        _csv = "timestamp,station,airTemp\n" + string.Join('\n', lines);
        _datasets = new() { ["weather"] = ChartDataset.FromCsv("weather", _csv) };
        _parsed = ChartDefinitionJson.Parse(Spec);
        if (!_parsed.Success || !ChartDefinitionBinder.Bind(_parsed.Definition!, _datasets).Success)
        {
            throw new InvalidOperationException("The benchmark spec does not bind: " + string.Join("; ", _parsed.Errors));
        }
    }

    [Benchmark]
    public DefinitionParseResult Parse() => ChartDefinitionJson.Parse(Spec);

    [Benchmark]
    public ChartDataset ReadCsv() => ChartDataset.FromCsv("weather", _csv);

    [Benchmark]
    public DefinitionBindResult Bind() => ChartDefinitionBinder.Bind(_parsed.Definition!, _datasets);
}
