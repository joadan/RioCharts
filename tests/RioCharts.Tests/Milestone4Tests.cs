using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;

namespace RioCharts.Tests;

public class Milestone4Tests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };

    // ---------- Zoom ----------

    [Fact]
    public void Visible_range_sets_the_x_domain_and_the_y_axis_fits_the_visible_data()
    {
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From([0.0, 1, 2, 3, 4, 5], [1.0, 2, 3, 100, 200, 300]) };
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a" }] };

        var all = ChartLayout.Compute(spec, data, Options);
        var zoomed = ChartLayout.Compute(spec, data, Options with { XRange = (0, 2) });

        Assert.False(all.Zoomed);
        Assert.True(zoomed.Zoomed);
        Assert.Equal((0, 2), (zoomed.XScale!.DomainMin, zoomed.XScale.DomainMax));
        Assert.Equal(all.XExtent, zoomed.XExtent); // the full range stays known for reset and clamping
        Assert.True(zoomed.YScale.DomainMax < 10);
        Assert.True(all.YScale.DomainMax >= 300);
    }

    [Fact]
    public void Zooming_into_large_data_downsamples_only_the_visible_points()
    {
        const int n = 100_000;
        var x = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
        var y = x.Select(v => Math.Sin(v / 50)).ToArray();
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a" }] };
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From(x, y) };

        var zoomed = ChartLayout.Compute(spec, data, Options with { XRange = (1000, 1400) });
        var hits = zoomed.HitTest.Series[0];

        // 401 visible points fit under the threshold: all of them are drawn, none outside.
        Assert.InRange(hits.X.Length, 399, 402);
        Assert.All(hits.X, px => Assert.InRange(px, zoomed.Plot.X - 1, zoomed.Plot.Right + 1));
    }

    // ---------- Streaming ----------

    [Fact]
    public void Buffer_keeps_its_time_window()
    {
        var buffer = new SeriesBuffer(timeWindow: TimeSpan.FromMinutes(1));
        var start = new DateTime(2026, 1, 1);
        for (var s = 0; s < 300; s++)
        {
            buffer.Append(start.AddSeconds(s), s);
        }

        var data = buffer.ToSeriesData();

        Assert.Equal(61, data.Count); // 240 s .. 299 s
        Assert.Equal(TimeValue.FromDateTime(start.AddSeconds(239)), data.X[0]);
        Assert.Equal(XKind.Time, data.Kind);
    }

    [Fact]
    public void Buffer_drops_the_oldest_points_at_capacity_and_grows_until_then()
    {
        var buffer = new SeriesBuffer(capacity: 3000, kind: XKind.Numeric);
        var changes = 0;
        buffer.Changed += () => changes++;
        for (var i = 0; i < 5000; i++)
        {
            buffer.Append(i, i * 2);
        }

        var data = buffer.ToSeriesData();

        Assert.Equal(3000, data.Count);
        Assert.Equal(2000, data.X[0]);
        Assert.Equal(9998, data.Y[^1]);
        Assert.Equal(5000, changes);
    }

    [Fact]
    public void Buffer_snapshot_is_sorted_even_when_points_arrive_out_of_order()
    {
        var buffer = new SeriesBuffer(kind: XKind.Numeric);
        buffer.AppendRange([3.0, 1, 2], [30.0, 10, 20]);
        var version = buffer.Version;

        var data = buffer.ToSeriesData();

        Assert.Equal([1.0, 2, 3], data.X);
        Assert.Equal([10.0, 20, 30], data.Y);
        buffer.Clear();
        Assert.True(buffer.Version > version);
        Assert.Equal(0, buffer.ToSeriesData().Count);
    }

    [Fact]
    public async Task Buffer_consumes_an_async_stream()
    {
        var buffer = new SeriesBuffer(kind: XKind.Numeric);

        await buffer.ConsumeAsync(Values(), v => v, v => v * 10);

        Assert.Equal(5, buffer.Count);

        static async IAsyncEnumerable<double> Values()
        {
            for (var i = 0; i < 5; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }
    }

    [Fact]
    public void Visible_range_is_ignored_on_category_axes()
    {
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a", Type = SeriesType.Bar }] };
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.FromCategories(["A", "B"], [1.0, 2]) };

        var result = ChartLayout.Compute(spec, data, Options with { XRange = (0, 1) });

        Assert.False(result.Zoomed);
        Assert.Null(result.XExtent);
    }
}
