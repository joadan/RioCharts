using System.Diagnostics;
using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

/// <summary>
/// Guards for the large-data paths found in the performance check (docs/implementation-notes.md,
/// "Performance"). The time limits are loose: they catch a return to quadratic work, not small
/// slowdowns.
/// </summary>
public class LargeDataTests
{
    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static readonly LayoutOptions Options = new() { Width = 640, Height = 320, Culture = CultureInfo.InvariantCulture };

    [Fact]
    public void A_pie_with_thousands_of_slices_labels_only_what_fits_down_the_sides()
    {
        var names = Enumerable.Range(0, 3000).Select(i => $"Product {i}").ToArray();
        var values = Enumerable.Range(0, 3000).Select(i => i < 3 ? 5000.0 : 1 + i % 7).ToArray();
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "p", Type = SeriesType.Pie }] };

        var watch = Stopwatch.StartNew();
        var layout = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["p"] = SeriesData.FromCategories(names, values) }, Options);
        watch.Stop();

        var outside = All(layout.Scene.Series).OfType<TextNode>().Where(t => t.Style.Name == "slice-label").ToList();
        Assert.InRange(outside.Count, 1, 2 * (int)(layout.Plot.Height / 12));
        Assert.Equal(3000, layout.Tooltip.Slices.Count);
        Assert.True(watch.ElapsedMilliseconds < 2000, $"{watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Stacks_with_shared_x_values_match_stacking_by_x_value()
    {
        const int n = 50;
        var x = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
        var a = x.Select(v => 10 + Math.Sin(v)).ToArray();
        var b = x.Select(v => v % 3 == 0 ? -2 : 5 + Math.Cos(v)).ToArray();
        b[7] = double.NaN;
        var c = x.Select(v => 3 + v % 4).ToArray();
        var spec = new ChartSpec
        {
            Series = [new SeriesSpec { Id = "a", Type = SeriesType.Area, Stack = "s" }, new SeriesSpec { Id = "b", Type = SeriesType.Area, Stack = "s" },
                new SeriesSpec { Id = "c", Type = SeriesType.Area, Stack = "s" }],
        };

        // Shared x: stacked per index. An extra point (value 0) on b only: stacked per x value.
        var shared = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["a"] = SeriesData.From(x, a), ["b"] = SeriesData.From(x, b), ["c"] = SeriesData.From(x, c) },
            Options with { XRange = (0, n - 1) });
        var byValue = ChartLayout.Compute(spec, new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.From(x, a),
            ["b"] = SeriesData.From([.. x, n], [.. b, 0]),
            ["c"] = SeriesData.From(x, c),
        }, Options with { XRange = (0, n - 1) });

        foreach (var id in new[] { "b", "c" })
        {
            var (s, v) = (shared.HitTest.Series.Single(h => h.SeriesId == id), byValue.HitTest.Series.Single(h => h.SeriesId == id));
            Assert.Equal(s.Index.Length, v.Index.Length);
            for (var i = 0; i < s.Index.Length; i++)
            {
                Assert.Equal(s.Y[i], v.Y[i], 0.01f);
            }
        }
    }

    [Fact]
    public void A_million_point_line_is_laid_out_without_copying_its_points()
    {
        const int n = 1_000_000;
        var x = new double[n];
        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            (x[i], y[i]) = (i, Math.Sin(i / 500.0));
        }

        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a" }] };
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From(x, y) };
        ChartLayout.Compute(spec, data, Options); // warm-up

        var before = GC.GetAllocatedBytesForCurrentThread();
        var layout = ChartLayout.Compute(spec, data, Options);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(layout.HitTest.Series[0].X.Length < 2000);
        Assert.True(allocated < 4_000_000, $"{allocated / 1e6:0.0} MB allocated"); // a copy of the points would be 8 MB or more
    }

    private static SeriesData Cloud(int n, params (double X, double Y)[] extra)
    {
        var random = new Random(7);
        double Gauss() => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
        var points = Enumerable.Range(0, n).Select(_ => (X: 50 + 5 * Gauss(), Y: 50 + 5 * Gauss())).Concat(extra).ToArray();
        return SeriesData.From([.. points.Select(p => p.X)], [.. points.Select(p => p.Y)]);
    }

    [Fact]
    public void Dense_scatter_clouds_draw_fewer_markers_but_keep_every_outlier()
    {
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "s", Type = SeriesType.Scatter }] };
        var data = new Dictionary<string, SeriesData> { ["s"] = Cloud(50_000, (5, 95), (95, 5), (90, 90)) };

        var layout = ChartLayout.Compute(spec, data, Options);

        var hits = layout.HitTest.Series[0];
        Assert.InRange(hits.Index.Length, 100, 12_000);
        var drawn = hits.Index.Select(i => (data["s"].X[i], data["s"].Y[i])).ToHashSet();
        Assert.Contains((5.0, 95.0), drawn);
        Assert.Contains((95.0, 5.0), drawn);
        Assert.Contains((90.0, 90.0), drawn);
        Assert.Equal(hits.Index.Length, layout.Tooltip.Series[0].X.Length);
    }

    [Fact]
    public void Small_scatter_series_draw_every_point()
    {
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "s", Type = SeriesType.Scatter }] };

        var layout = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["s"] = Cloud(500) }, Options);

        Assert.Equal(500, layout.HitTest.Series[0].Index.Length);
    }

    [Fact]
    public void The_trend_line_of_a_thinned_scatter_uses_all_points()
    {
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "s", Type = SeriesType.Scatter, TrendLine = true }] };
        var data = new Dictionary<string, SeriesData> { ["s"] = Cloud(20_000, (10, 10), (90, 90)) };
        PathNode Trend(ChartLayoutResult l) => All(l.Scene.Series).OfType<PathNode>().Single(p => p.Class == "rio-trend");

        var thinned = ChartLayout.Compute(spec, data, Options);
        var all = ChartLayout.Compute(spec, data, Options with { DownsampleThreshold = int.MaxValue });

        Assert.True(thinned.HitTest.Series[0].Index.Length < all.HitTest.Series[0].Index.Length);
        Assert.Equal(Trend(all).Path.Coords, Trend(thinned).Path.Coords);
    }
}
