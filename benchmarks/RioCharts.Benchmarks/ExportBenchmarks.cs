using System.Globalization;
using BenchmarkDotNet.Attributes;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Skia;

namespace RioCharts.Benchmarks;

/// <summary>
/// Server-side export (<see cref="ChartExport"/>): writing a laid-out scene as SVG, and the whole way
/// from spec to SVG and PNG.
/// </summary>
[MemoryDiagnoser]
public class ExportBenchmarks
{
    private ChartDefinition _spec = null!;
    private Dictionary<string, SeriesData> _data = null!;
    private ExportOptions _options = null!;
    private ChartLayoutResult _layout = null!;

    [Params(1_000, 100_000)]
    public int Points { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _spec = Data.TwoLines();
        _data = Data.TwoLinesData(Points);
        _options = new ExportOptions { Width = 800, Height = 400, Scale = 1, Culture = CultureInfo.InvariantCulture };
        _layout = ChartExport.Layout(_spec, _data, _options);
    }

    /// <summary>Only the SVG writer, from a scene laid out once.</summary>
    [Benchmark]
    public string SceneToSvg() => ChartExport.ToSvg(_layout.Scene, _options);

    [Benchmark]
    public string SpecToSvg() => ChartExport.ToSvg(_spec, _data, _options);

    [Benchmark]
    public byte[] SpecToPng() => ChartExport.ToPng(_spec, _data, _options);
}
