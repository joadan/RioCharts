using BenchmarkDotNet.Attributes;
using RioCharts.Core.Data;

namespace RioCharts.Benchmarks;

/// <summary>Turning items into columns (once per data change) and LTTB downsampling.</summary>
[MemoryDiagnoser]
public class DataBenchmarks
{
    private sealed record Reading(DateTime Time, double SupplyTemp);

    private Reading[] _items = null!;
    private double[] _x = null!;
    private double[] _y = null!;

    [Params(10_000, 1_000_000)]
    public int Points { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var hours = Data.Hours(Points);
        _items = hours.Select((t, i) => new Reading(t, Data.Supply(i))).ToArray();
        _x = hours.Select(TimeValue.FromDateTime).ToArray();
        _y = _items.Select(r => r.SupplyTemp).ToArray();
    }

    /// <summary>What a <c>&lt;LineSeries Items XValue YValue&gt;</c> does when its items change.</summary>
    [Benchmark]
    public SeriesData FromItems() => SeriesData.FromItems(_items, r => r.Time, r => r.SupplyTemp);

    /// <summary>To about two points per pixel of an 800 px wide chart.</summary>
    [Benchmark]
    public int[] Lttb1600() => Lttb.Downsample(_x, _y, 0, _x.Length, 1_600);
}
