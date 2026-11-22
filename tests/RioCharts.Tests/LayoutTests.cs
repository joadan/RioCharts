using System.Globalization;
using System.Text.Json;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class LayoutTests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };

    private static ChartLayoutResult Layout(ChartSpec spec, Dictionary<string, SeriesData> data, LayoutOptions? options = null) =>
        ChartLayout.Compute(spec, data, options ?? Options);

    private static ChartSpec Spec(params SeriesSpec[] series) => new() { Series = series };

    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static IEnumerable<Node> All(Scene scene) => All(scene.Static).Concat(All(scene.Series)).Concat(All(scene.Overlay));

    [Fact]
    public void Single_line_has_no_legend()
    {
        var result = Layout(Spec(new SeriesSpec { Id = "a" }), new() { ["a"] = SeriesData.From([0.0, 1, 2], [1.0, 3, 2]) });

        Assert.DoesNotContain(All(result.Scene), n => n.Class == "rio-legend");
    }

    [Fact]
    public void Two_series_show_a_legend_with_toggles()
    {
        var result = Layout(Spec(new SeriesSpec { Id = "a" }, new SeriesSpec { Id = "b" }),
            new() { ["a"] = SeriesData.From([0.0, 1], [1.0, 2]), ["b"] = SeriesData.From([0.0, 1], [2.0, 3]) });

        var items = All(result.Scene).OfType<GroupNode>().Where(g => g.Interaction?.Kind == "legend").ToList();
        Assert.Equal(["a", "b"], items.Select(i => i.Interaction!.Id));
    }

    [Fact]
    public void Bars_and_areas_start_at_zero_lines_do_not()
    {
        var data = new Dictionary<string, SeriesData> { ["s"] = SeriesData.From([0.0, 1, 2], [50.0, 60, 70]) };

        var line = Layout(Spec(new SeriesSpec { Id = "s", Type = SeriesType.Line }), data);
        var bar = Layout(Spec(new SeriesSpec { Id = "s", Type = SeriesType.Bar }), data);

        Assert.True(line.YScale.DomainMin > 0);
        Assert.Equal(0, bar.YScale.DomainMin);
    }

    [Fact]
    public void Hidden_series_keep_color_and_legend_but_leave_domain()
    {
        var spec = Spec(new SeriesSpec { Id = "small" }, new SeriesSpec { Id = "big" });
        var data = new Dictionary<string, SeriesData>
        {
            ["small"] = SeriesData.From([0.0, 1], [1.0, 2]),
            ["big"] = SeriesData.From([0.0, 1], [1000.0, 2000]),
        };

        var result = Layout(spec, data, Options with { HiddenSeries = new HashSet<string> { "big" } });

        Assert.True(result.YScale.DomainMax < 10);
        var big = result.Legend.Single(l => l.SeriesId == "big");
        Assert.False(big.Visible);
        Assert.Equal(ThemeTokens.Series(1), big.Color); // color is stable when toggled
        Assert.DoesNotContain(All(result.Scene.Series), n => n.Key == "series:big");
    }

    [Fact]
    public void Empty_chart_renders_a_message_not_an_error()
    {
        var result = Layout(Spec(new SeriesSpec { Id = "a" }), new());

        Assert.False(result.HasData);
        Assert.Contains(All(result.Scene.Overlay), n => n is TextNode { Text: "No data available" });
    }

    [Fact]
    public void Single_point_and_equal_values_do_not_collapse_the_axes()
    {
        var single = Layout(Spec(new SeriesSpec { Id = "a" }), new() { ["a"] = SeriesData.From([5.0], [7.0]) });
        var equal = Layout(Spec(new SeriesSpec { Id = "a" }), new() { ["a"] = SeriesData.From([1.0, 2, 3], [4.0, 4, 4]) });

        Assert.True(single.XScale!.DomainMax > single.XScale.DomainMin);
        Assert.True(single.YScale.DomainMax > single.YScale.DomainMin);
        Assert.True(equal.YScale.DomainMin < 4 && equal.YScale.DomainMax > 4);
    }

    [Fact]
    public void Gaps_split_the_line_path()
    {
        var result = Layout(Spec(new SeriesSpec { Id = "a" }), new() { ["a"] = SeriesData.From([0.0, 1, 2, 3, 4], [1.0, 2, double.NaN, 3, 4]) });

        var path = All(result.Scene.Series).OfType<PathNode>().Single(p => p.Class == "rio-line").Path;
        Assert.Equal(2, path.Commands.Count(c => c == PathCommand.MoveTo));
    }

    [Fact]
    public void Lone_points_get_a_marker_others_do_not()
    {
        var result = Layout(Spec(new SeriesSpec { Id = "a" }), new() { ["a"] = SeriesData.From([0.0, 1, 2, 3, 4], [1.0, 2, double.NaN, 3, double.NaN]) });

        var points = All(result.Scene.Series).OfType<PathNode>().Where(p => p.Class == "rio-point").ToList();

        Assert.Single(points);
        Assert.Single(points[0].Path.Commands, c => c == PathCommand.Arc); // only the point at x=3
    }

    [Fact]
    public void Y_title_is_horizontal_above_the_axis()
    {
        var spec = new ChartSpec { Y = new AxisSpec { Title = "°C" }, Series = [new SeriesSpec { Id = "a" }] };

        var result = Layout(spec, new() { ["a"] = SeriesData.From([0.0, 1], [1.0, 2]) });
        var title = All(result.Scene.Static).OfType<TextNode>().Single(t => t.Text == "°C");

        Assert.Equal(0, title.Rotation);
        Assert.True(title.At.Y < result.Plot.Y);
    }

    [Fact]
    public void Default_z_order_is_areas_bars_lines()
    {
        var spec = Spec(
            new SeriesSpec { Id = "line", Type = SeriesType.Line },
            new SeriesSpec { Id = "bar", Type = SeriesType.Bar },
            new SeriesSpec { Id = "area", Type = SeriesType.Area });
        var d = SeriesData.From([0.0, 1], [1.0, 2]);

        var result = Layout(spec, new() { ["line"] = d, ["bar"] = d, ["area"] = d });
        var plot = All(result.Scene.Series).OfType<GroupNode>().Single(g => g.Class == "rio-plot");
        var order = plot.Children.Select(n => n.Key);

        Assert.Equal(["series:area", "series:bar", "series:line"], order);
    }

    [Fact]
    public void Grouped_bars_share_a_category_band_without_overlap()
    {
        var spec = Spec(new SeriesSpec { Id = "a", Type = SeriesType.Bar }, new SeriesSpec { Id = "b", Type = SeriesType.Bar });
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(["Q1", "Q2"], [1.0, 2]),
            ["b"] = SeriesData.FromCategories(["Q1", "Q2"], [3.0, 4]),
        };

        var result = Layout(spec, data);
        var a = All(result.Scene.Series).Where(n => n is GroupNode { Key: "series:a" }).SelectMany(All).OfType<RectNode>().ToList();
        var b = All(result.Scene.Series).Where(n => n is GroupNode { Key: "series:b" }).SelectMany(All).OfType<RectNode>().ToList();

        Assert.Equal(2, a.Count);
        Assert.True(a[0].Bounds.Right <= b[0].Bounds.X + 0.01f);
        Assert.Equal(XKind.Category, result.XKind);
    }

    [Fact]
    public void Wider_charts_get_more_x_ticks()
    {
        var spec = Spec(new SeriesSpec { Id = "a" });
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From([0.0, 1000], [0.0, 1]) };

        int Labels(double width) => All(Layout(spec, data, Options with { Width = width }).Scene.Static)
            .Where(n => n is GroupNode { Class: "rio-axis-x" }).SelectMany(All).OfType<TextNode>().Count();

        Assert.True(Labels(1200) > Labels(300));
    }

    [Fact]
    public void X_labels_never_overlap()
    {
        var spec = Spec(new SeriesSpec { Id = "a" });
        var start = new DateTime(2026, 1, 1);
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.From(Enumerable.Range(0, 500).Select(i => start.AddHours(i)).ToArray(), Enumerable.Range(0, 500).Select(i => (double)i).ToArray()),
        };

        foreach (var width in new[] { 250.0, 400, 800, 1600 })
        {
            var labels = All(Layout(spec, data, Options with { Width = width }).Scene.Static)
                .Where(n => n is GroupNode { Class: "rio-axis-x" }).SelectMany(All).OfType<TextNode>().ToList();
            (double Left, double Right) Extent(TextNode t)
            {
                var w = Options.Measurer.Measure(t.Text, 12);
                return t.Anchor switch
                {
                    Anchor.Start => (t.At.X, t.At.X + w),
                    Anchor.End => (t.At.X - w, t.At.X),
                    _ => (t.At.X - w / 2, t.At.X + w / 2),
                };
            }

            for (var i = 1; i < labels.Count; i++)
            {
                Assert.True(Extent(labels[i]).Left >= Extent(labels[i - 1]).Right,
                    $"width {width}: '{labels[i - 1].Text}' and '{labels[i].Text}' overlap");
            }

            Assert.All(labels, l => Assert.InRange(Extent(l).Right, 0, width));
        }
    }

    [Fact]
    public void Large_series_are_downsampled_to_the_plot_width()
    {
        var n = 50_000;
        var data = SeriesData.From(Enumerable.Range(0, n).Select(i => (double)i).ToArray(), Enumerable.Range(0, n).Select(i => Math.Sin(i / 100.0)).ToArray());

        var result = Layout(Spec(new SeriesSpec { Id = "a" }), new() { ["a"] = data });

        Assert.True(result.HitTest.Series[0].X.Length < 2000);
    }

    [Fact]
    public void Hit_test_finds_the_nearest_point()
    {
        var result = Layout(Spec(new SeriesSpec { Id = "a" }), new() { ["a"] = SeriesData.From([0.0, 1, 2, 3], [0.0, 1, 0, 1]) });
        var second = result.HitTest.Series[0];

        var hit = result.HitTest.FindNearest(second.X[2] + 3, second.Y[2] - 2);

        Assert.NotNull(hit);
        Assert.Equal(2, hit.Value.Index);
        Assert.Single(result.HitTest.FindAtX(second.X[1]));
    }

    [Fact]
    public void Duplicate_ids_and_mixed_x_kinds_are_reported()
    {
        var spec = Spec(new SeriesSpec { Id = "a" }, new SeriesSpec { Id = "a" }, new SeriesSpec { Id = "c" });
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.From([0.0, 1], [1.0, 2]),
            ["c"] = SeriesData.FromCategories(["x"], [1.0]),
        };

        var result = Layout(spec, data);

        Assert.Contains(result.Warnings, w => w.Contains("duplicate", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("series 'c'", StringComparison.Ordinal));
    }

    [Fact]
    public void Description_mentions_extremes_and_trend()
    {
        var result = Layout(Spec(new SeriesSpec { Id = "temp", Name = "London" }), new() { ["temp"] = SeriesData.From([0.0, 1, 2, 3], [10.0, 12, 14, 16]) });

        Assert.Contains("London: 4 values", result.Scene.Description);
        Assert.Contains("highest 16", result.Scene.Description);
        Assert.Contains("trend rising", result.Scene.Description);
    }

    [Fact]
    public void Spec_round_trips_through_json()
    {
        var spec = new ChartSpec
        {
            Title = "Temps",
            Y = new AxisSpec { Title = "°C", Min = 0 },
            Series = [new SeriesSpec { Id = "a", Type = SeriesType.Area, Curve = CurveType.Monotone }],
        };

        var json = JsonSerializer.Serialize(spec);
        var back = JsonSerializer.Deserialize<ChartSpec>(json)!;

        Assert.Contains("\"Area\"", json);
        Assert.DoesNotContain("DisplayName", json);
        Assert.Equal(spec.Title, back.Title);
        Assert.Equal(spec.Y, back.Y);
        Assert.Equal(spec.Series[0], back.Series[0]);
    }
}
