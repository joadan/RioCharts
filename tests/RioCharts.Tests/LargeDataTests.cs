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
}
