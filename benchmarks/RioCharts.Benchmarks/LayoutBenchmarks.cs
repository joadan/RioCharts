using System.Globalization;
using BenchmarkDotNet.Attributes;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;

namespace RioCharts.Benchmarks;

/// <summary>
/// <see cref="ChartLayout.Compute"/>: spec + data → scene, the work done on every data change,
/// resize and zoom. Lines above the downsampling threshold go through LTTB.
/// </summary>
[MemoryDiagnoser]
public class LineLayoutBenchmarks
{
    private ChartDefinition _spec = null!;
    private Dictionary<string, SeriesData> _data = null!;
    private LayoutOptions _options = null!;
    private LayoutOptions _zoomed = null!;

    /// <summary>Points per series (two series).</summary>
    [Params(1_000, 10_000, 100_000)]
    public int Points { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _spec = Data.TwoLines();
        _data = Data.TwoLinesData(Points);
        _options = new LayoutOptions { Width = 800, Height = 400, Culture = CultureInfo.InvariantCulture };
        var start = TimeValue.FromDateTime(Data.Start);
        _zoomed = _options with { XRange = (start + Points * 0.4 * 3_600_000, start + Points * 0.5 * 3_600_000) };
    }

    [Benchmark]
    public object Line() => ChartLayout.Compute(_spec, _data, _options);

    /// <summary>A tenth of the data visible: the y axis fits it and the line draws only that window.</summary>
    [Benchmark]
    public object LineZoomed() => ChartLayout.Compute(_spec, _data, _zoomed);
}

/// <summary>Layout of category and polar charts, which have few points but more labels.</summary>
[MemoryDiagnoser]
public class CategoryLayoutBenchmarks
{
    private static readonly LayoutOptions Options = new() { Width = 800, Height = 400, Culture = CultureInfo.InvariantCulture };

    private ChartDefinition _bars = null!;
    private Dictionary<string, SeriesData> _barData = null!;
    private ChartDefinition _stacked = null!;
    private ChartDefinition _pie = null!;
    private Dictionary<string, SeriesData> _pieData = null!;

    [GlobalSetup]
    public void Setup()
    {
        var names = Enumerable.Range(1, 30).Select(i => $"Store {i}").ToArray();
        SeriesData Values(int seed) => SeriesData.FromCategories(names, names.Select((_, i) => 50 + (i * 37 + seed * 11) % 400.0).ToArray());

        _bars = new ChartDefinition
        {
            Y = new AxisOptions { Title = "MWh" },
            Series = [new() { Id = "a", Name = "2025", Type = SeriesType.Bar }, new() { Id = "b", Name = "2026", Type = SeriesType.Bar }, new() { Id = "c", Name = "Target", Type = SeriesType.Bar }],
        };
        _stacked = _bars with { Series = _bars.Series.Select(s => s with { Stack = "all" }).ToList() };
        _barData = new() { ["a"] = Values(1), ["b"] = Values(2), ["c"] = Values(3) };

        var slices = new[] { "Industry", "Homes", "Transport", "Offices", "Shops", "Other" };
        _pie = new ChartDefinition { Series = [new() { Id = "p", Name = "Energy", Type = SeriesType.Pie, DataLabels = true }] };
        _pieData = new() { ["p"] = SeriesData.FromCategories(slices, [420, 310, 180, 95, 60, 12]) };
    }

    [Benchmark]
    public object GroupedBars() => ChartLayout.Compute(_bars, _barData, Options);

    [Benchmark]
    public object StackedBars() => ChartLayout.Compute(_stacked, _barData, Options);

    [Benchmark]
    public object Pie() => ChartLayout.Compute(_pie, _pieData, Options);
}
