using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class Milestone3Tests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 320, Culture = CultureInfo.InvariantCulture };

    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static IEnumerable<Node> All(Scene scene) => All(scene.Static).Concat(All(scene.Series)).Concat(All(scene.Overlay));

    private static ChartLayoutResult Pie(double[] values, SeriesSpec? spec = null, LayoutOptions? options = null)
    {
        var names = values.Select((_, i) => "C" + (i + 1)).ToArray();
        spec ??= new SeriesSpec { Id = "p", Type = SeriesType.Pie };
        return ChartLayout.Compute(new ChartSpec { Series = [spec] }, new Dictionary<string, SeriesData> { [spec.Id] = SeriesData.FromCategories(names, values) }, options ?? Options);
    }

    private static List<PathNode> Slices(ChartLayoutResult r) => All(r.Scene).OfType<PathNode>().Where(p => p.Class == "rio-slice").ToList();

    [Fact]
    public void Pie_has_a_slice_and_a_legend_item_per_category()
    {
        var result = Pie([10, 20, 30]);

        Assert.Equal(3, Slices(result).Count);
        Assert.Equal(["C1", "C2", "C3"], result.Legend.Select(l => l.SeriesId));
        Assert.Equal(3, result.Tooltip.Slices.Count);
        Assert.Equal(Math.PI * 2, result.Tooltip.Slices[^1].A1, 3);
        Assert.Equal("30 (50%)", result.Tooltip.Slices[2].Value);
    }

    [Fact]
    public void Slices_and_radial_bars_are_keyed_like_their_legend_items()
    {
        var pie = Pie([10, 20, 30, 1, 1], new SeriesSpec { Id = "p", Type = SeriesType.Pie, GroupSmallSlices = 0.05 });
        var bars = Pie([10, 20, 30], new SeriesSpec { Id = "p", Type = SeriesType.RadialBar });

        // Legend hover highlights the group with the item's key (the ring "series:p" holds them).
        string[] Keys(ChartLayoutResult r) => [.. All(r.Scene).OfType<GroupNode>().Select(g => g.Key).OfType<string>().Where(k => k.StartsWith("series:", StringComparison.Ordinal))];
        Assert.Equal(["series:p", "series:C1", "series:C2", "series:C3", "series:" + ChartLayout.OtherSliceKey], Keys(pie));
        Assert.Equal(pie.Legend.Select(l => "series:" + l.SeriesId), Keys(pie).Skip(1));
        Assert.Equal(["series:p", "series:C1", "series:C2", "series:C3"], Keys(bars));
    }

    [Fact]
    public void Hiding_a_category_removes_its_slice_and_keeps_its_color()
    {
        var result = Pie([10, 20, 30], options: Options with { HiddenSeries = new HashSet<string> { "C2" } });

        Assert.Equal(2, Slices(result).Count);
        Assert.False(result.Legend[1].Visible);
        Assert.Equal(ThemeTokens.Series(2), result.Tooltip.Slices[1].Color);
        Assert.Equal("30 (75%)", result.Tooltip.Slices[1].Value);
    }

    [Fact]
    public void Small_slices_are_grouped_into_other()
    {
        var result = Pie([100, 100, 1, 1, 1], new SeriesSpec { Id = "p", Type = SeriesType.Pie, GroupSmallSlices = 0.05 });

        Assert.Equal(3, result.Tooltip.Slices.Count);
        var other = result.Tooltip.Slices.Single(s => s.Label == "Other");
        Assert.Equal(-1, other.Index);
    }

    [Fact]
    public void Donut_shows_the_total_in_the_middle()
    {
        var result = Pie([10, 20, 30], new SeriesSpec { Id = "p", Type = SeriesType.Pie, InnerRadius = 0.6 });

        Assert.Contains(All(result.Scene), n => n is TextNode { Class: "rio-center-value", Text: "60" });
        Assert.All(result.Tooltip.Slices, s => Assert.True(s.R0 > 0));
    }

    [Fact]
    public void Half_donut_fits_its_sweep_not_the_full_circle()
    {
        var full = Pie([1, 1], new SeriesSpec { Id = "p", Type = SeriesType.Pie, InnerRadius = 0.6 }, Options with { Width = 400, Height = 200 });
        var half = Pie([1, 1], new SeriesSpec { Id = "p", Type = SeriesType.Pie, InnerRadius = 0.6, StartAngle = -90, EndAngle = 90 },
            Options with { Width = 400, Height = 200 });

        Assert.True(half.Tooltip.Slices[0].R1 > full.Tooltip.Slices[0].R1 * 1.4);
        Assert.True(half.Tooltip.Slices[0].Cy > half.Plot.CenterY); // center moved down
        Assert.True(half.Tooltip.Slices[0].Cy - half.Tooltip.Slices[0].R1 >= half.Plot.Y - 1);
    }

    [Fact]
    public void Outside_labels_do_not_overlap()
    {
        var result = Pie([500, 400, 1, 1, 1, 1, 1, 1]);

        var outside = All(result.Scene).OfType<TextNode>().Where(t => t.Style.Name == "slice-label").ToList();
        Assert.NotEmpty(outside);
        foreach (var side in outside.GroupBy(t => t.Anchor))
        {
            var ys = side.Select(t => t.At.Y).Order().ToList();
            for (var i = 1; i < ys.Count; i++)
            {
                Assert.True(ys[i] - ys[i - 1] >= 13, $"labels at {ys[i - 1]} and {ys[i]} overlap");
            }
        }

        Assert.Equal(outside.Count, All(result.Scene).Count(n => n.Class == "rio-leader"));
    }

    [Fact]
    public void Nested_pie_series_become_rings()
    {
        var data = new Dictionary<string, SeriesData>
        {
            ["outer"] = SeriesData.FromCategories(["A", "B"], [1.0, 2]),
            ["inner"] = SeriesData.FromCategories(["A", "B"], [2.0, 1]),
        };
        var spec = new ChartSpec
        {
            Series = [new SeriesSpec { Id = "outer", Type = SeriesType.Pie }, new SeriesSpec { Id = "inner", Type = SeriesType.Pie }],
        };

        var result = ChartLayout.Compute(spec, data, Options);
        var outer = result.Tooltip.Slices.Where(s => s.SeriesId == "outer").ToList();
        var inner = result.Tooltip.Slices.Where(s => s.SeriesId == "inner").ToList();

        Assert.True(inner[0].R1 < outer[0].R0);
        Assert.Equal(2, result.Legend.Count); // categories, not series
        Assert.Equal("outer", outer[0].Header);
    }

    [Fact]
    public void Radial_bars_fill_their_track_by_value()
    {
        var spec = new SeriesSpec { Id = "r", Type = SeriesType.RadialBar, Max = 100 };
        var result = ChartLayout.Compute(new ChartSpec { Series = [spec] },
            new Dictionary<string, SeriesData> { ["r"] = SeriesData.FromCategories(["North", "South"], [50.0, 100]) }, Options);

        var north = result.Tooltip.Slices[0];
        var south = result.Tooltip.Slices[1];
        Assert.Equal(Math.PI * 1.5 / 2, north.A1 - north.A0, 3); // half of 270°
        Assert.Equal(Math.PI * 1.5, south.A1 - south.A0, 3);
        Assert.Equal(Math.PI * 1.5, north.HitEnd, 3); // the whole track is hoverable
        Assert.True(north.R0 > south.R1); // first category outermost
        Assert.Contains(All(result.Scene), n => n is TextNode { Text: "North 50" });
    }

    [Fact]
    public void Polar_and_cartesian_series_do_not_mix()
    {
        var spec = new ChartSpec
        {
            Series = [new SeriesSpec { Id = "p", Type = SeriesType.Pie }, new SeriesSpec { Id = "l" }],
        };
        var data = new Dictionary<string, SeriesData>
        {
            ["p"] = SeriesData.FromCategories(["A"], [1.0]),
            ["l"] = SeriesData.From([0.0, 1], [1.0, 2]),
        };

        var result = ChartLayout.Compute(spec, data, Options);

        Assert.True(result.HasData);
        Assert.Contains(result.Warnings, w => w.Contains("cannot be mixed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(new double[0])]
    [InlineData(new[] { 0.0, 0 })]
    [InlineData(new[] { -5.0, 10 })]
    [InlineData(new[] { 42.0 })]
    public void Edge_cases_render_cleanly(double[] values)
    {
        var result = Pie(values);

        Assert.Equal(values.Any(v => v > 0), result.HasData);
        if (!result.HasData)
        {
            Assert.Contains(All(result.Scene.Overlay), n => n is TextNode { Text: "No data available" });
        }
    }
}
