using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class BubbleTests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };

    // Stores: floor area in m² (x), revenue per m² in € (y) and visitors per day (size).
    private static readonly double[] Area = [400, 650, 900, 1200, 1500];
    private static readonly double[] Revenue = [310, 280, 350, 240, 300];
    private static readonly double[] Visitors = [100, 400, 900, 1600, 2500];

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    private static SeriesData Stores(double[]? sizes = null) => SeriesData.From(Area, Revenue).WithSizes(sizes ?? Visitors);

    private static ChartLayoutResult Layout(SeriesData data, SeriesOptions? series = null, LayoutOptions? options = null) =>
        ChartLayout.Compute(new ChartDefinition { Series = [series ?? new SeriesOptions { Id = "s", Name = "Stores", Type = SeriesType.Scatter, SizeName = "Visitors" }] },
            new Dictionary<string, SeriesData> { ["s"] = data }, options ?? Options);

    /// <summary>The drawn circles in draw order: center and radius.</summary>
    private static List<(float X, float Y, float R)> Circles(ChartLayoutResult layout) =>
        [.. All<PathNode>(layout.Scene.Series).Where(n => n.Class == "rio-marker rio-bubble").SelectMany(n =>
        {
            var circles = new List<(float, float, float)>();
            var k = 0;
            foreach (var command in n.Path.Commands)
            {
                if (command == PathCommand.Arc)
                {
                    circles.Add((n.Path.Coords[k], n.Path.Coords[k + 1], n.Path.Coords[k + 2]));
                }

                k += PathData.CoordCount(command);
            }

            return circles;
        })];

    // ---------- Data ----------

    [Fact]
    public void Sizes_follow_their_points_when_x_is_sorted()
    {
        var data = SeriesData.From([3.0, 1, 2], [30.0, 10, 20]).WithSizes([300.0, 100, 200]);

        Assert.Equal([1.0, 2, 3], data.X);
        Assert.Equal([100.0, 200, 300], data.Sizes!);
        Assert.Equal([100.0, 200, 300], data.WithColors(["red", null, null]).WithLow([1.0, 2, 3]).Sizes!);
    }

    // ---------- Layout ----------

    [Fact]
    public void A_bubbles_area_is_proportional_to_its_size()
    {
        var circles = Circles(Layout(Stores())).OrderBy(c => c.R).ToList();

        Assert.Equal(5, circles.Count);
        for (var i = 1; i < circles.Count; i++)
        {
            // Radius ∝ √size: 100, 400, 900, 1600 and 2500 give radii in the ratio 1 : 2 : 3 : 4 : 5.
            Assert.Equal(i + 1, circles[i].R / circles[0].R, 2);
        }
    }

    [Fact]
    public void The_largest_bubble_is_drawn_first()
    {
        var radii = Circles(Layout(Stores())).Select(c => c.R).ToList();

        Assert.Equal(radii.OrderByDescending(r => r), radii);
    }

    [Fact]
    public void Marker_size_sets_the_largest_bubbles_diameter()
    {
        var layout = Layout(Stores(), new SeriesOptions { Id = "s", Type = SeriesType.Scatter, MarkerSize = 60 });

        Assert.Equal(30, Circles(layout).Max(c => c.R), 2);
    }

    [Fact]
    public void Bubble_series_share_one_size_scale()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "a", Type = SeriesType.Scatter }, new SeriesOptions { Id = "b", Type = SeriesType.Scatter }],
        }, new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.From([1.0], [1.0]).WithSizes([100.0]),
            ["b"] = SeriesData.From([2.0], [2.0]).WithSizes([400.0]),
        }, Options);

        var radii = Circles(layout).Select(c => c.R).OrderBy(r => r).ToList();
        Assert.Equal(2, radii[1] / radii[0], 2);
    }

    [Fact]
    public void Sizes_of_zero_or_less_or_missing_are_not_drawn()
    {
        var layout = Layout(Stores([100, 0, -5, double.NaN, 400]));

        Assert.Equal(2, Circles(layout).Count);
        Assert.Equal(2, layout.Tooltip.Series.Single().X.Length);
    }

    [Fact]
    public void The_axes_leave_room_for_the_bubbles()
    {
        var layout = Layout(Stores());

        // The largest bubble (at the largest x) and the bubble at the highest y fit inside the plot.
        var plot = layout.Tooltip.Plot;
        foreach (var (x, y, r) in Circles(layout))
        {
            Assert.InRange(x + r, plot.X, plot.Right + 1);
            Assert.InRange(y - r, plot.Y - 1, plot.Bottom);
        }
    }

    [Fact]
    public void Set_axis_limits_are_kept()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            X = new AxisOptions { Min = 0, Max = 2000 },
            Y = new AxisOptions { Min = 200, Max = 400 },
            Series = [new SeriesOptions { Id = "s", Type = SeriesType.Scatter }],
        }, new Dictionary<string, SeriesData> { ["s"] = Stores() }, Options);

        Assert.Equal(0, layout.XScale!.DomainMin);
        Assert.Equal(2000, layout.XScale.DomainMax);
        Assert.Equal(200, layout.YScale.DomainMin);
        Assert.Equal(400, layout.YScale.DomainMax);
    }

    [Fact]
    public void The_tooltip_shows_the_size_and_the_bubbles_radius()
    {
        var layout = Layout(Stores(), new SeriesOptions { Id = "s", Type = SeriesType.Scatter, SizeName = "Visitors", SizeFormat = "#,0 'a day'" });

        var s = layout.Tooltip.Series.Single();
        Assert.Equal(["Visitors"], s.PartLabels!);
        Assert.Equal("2,500 a day", s.PartValues![4][0]);
        Assert.Equal(Circles(layout).Max(c => c.R), s.Radii![4], 2);
        Assert.Equal(["Size"], Layout(Stores(), new SeriesOptions { Id = "s", Type = SeriesType.Scatter }).Tooltip.Series.Single().PartLabels!);
    }

    [Fact]
    public void Bubbles_are_described_with_their_size_range()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Legend = new LegendOptions { Mode = LegendMode.Show },
            Series = [new SeriesOptions { Id = "s", Name = "Stores", Type = SeriesType.Scatter, SizeName = "Visitors" }],
        }, new Dictionary<string, SeriesData> { ["s"] = Stores() }, Options);

        Assert.EndsWith("Visitors 100 to 2,500", layout.Legend.Single().Tooltip, StringComparison.Ordinal);
        var swedish = ChartLayout.Compute(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "s", Name = "Butiker", Type = SeriesType.Scatter }],
        }, new Dictionary<string, SeriesData> { ["s"] = Stores() }, Options with { Culture = CultureInfo.GetCultureInfo("sv-SE") }).Scene.Description;
        Assert.Contains("Storlek 100 till 2", swedish, StringComparison.Ordinal);
    }

    [Fact]
    public void Odd_data_renders_cleanly()
    {
        foreach (var data in new[]
        {
            SeriesData.Empty.WithSizes([]),
            SeriesData.From([1.0], [1.0]).WithSizes([5.0]),
            SeriesData.From([1.0, 1, 1], [2.0, 2, 2]).WithSizes([3.0, 3, 3]),
            Stores([0, 0, 0, 0, 0]),
            SeriesData.FromCategories(["a", "b"], [1.0, 2]).WithSizes([10.0, 20]),
        })
        {
            Assert.NotNull(Layout(data).Scene);
        }

        Assert.Single(Circles(Layout(SeriesData.From([1.0], [1.0]).WithSizes([5.0]))));
    }

    [Fact]
    public void Compact_bubbles_stay_small()
    {
        var layout = ChartLayout.Compute(new ChartDefinition { Compact = true, Series = [new SeriesOptions { Id = "s", Type = SeriesType.Scatter }] },
            new Dictionary<string, SeriesData> { ["s"] = Stores() }, Options with { Width = 120, Height = 48 });

        Assert.InRange(Circles(layout).Max(c => c.R), 2, 6);
    }

    // ---------- Size legend ----------

    private static GroupNode? SizeLegend(ChartLayoutResult layout) =>
        All<GroupNode>(layout.Scene.Static).SingleOrDefault(g => g.Class == "rio-size-legend");

    private static ChartLayoutResult WithLegend(LegendOptions legend, double width = 600) => ChartLayout.Compute(new ChartDefinition
    {
        Legend = legend,
        Series = [new SeriesOptions { Id = "s", Type = SeriesType.Scatter, SizeName = "Visitors", SizeFormat = "#,0" }],
    }, new Dictionary<string, SeriesData> { ["s"] = Stores() }, Options with { Width = width });

    [Fact]
    public void The_size_legend_shows_round_sizes_at_their_real_size()
    {
        var layout = Layout(Stores(), new SeriesOptions { Id = "s", Type = SeriesType.Scatter, SizeName = "Visitors", SizeFormat = "#,0" });

        var legend = SizeLegend(layout)!;
        Assert.Equal(["Visitors", "2,000", "500"], All<TextNode>(legend).Select(t => t.Text)); // 100 would sit on 500
        Assert.Equal("Visitors: 500, 2,000", legend.AriaLabel);

        // The circle for 2,000 is as large as a bubble of 2,000: √(2000 / 2500) of the largest bubble's radius.
        var largest = Circles(layout).Max(c => c.R);
        var circle = All<PathNode>(legend).First(n => n.Path.Commands.Contains(PathCommand.Arc));
        Assert.Equal(largest * Math.Sqrt(2000 / 2500.0), circle.Path.Coords[4], 1); // MoveTo x, y, then Arc cx, cy, r

        // The plot makes room for it on the right.
        Assert.True(layout.Tooltip.Plot.Right < 600 - 70);
    }

    [Fact]
    public void The_size_legend_can_be_turned_off_and_is_left_out_of_narrow_charts()
    {
        Assert.Null(SizeLegend(WithLegend(new LegendOptions { Sizes = false })));
        Assert.Null(SizeLegend(WithLegend(new LegendOptions(), width: 340)));
        Assert.NotNull(SizeLegend(WithLegend(new LegendOptions { Sizes = true }, width: 340)));
        Assert.Null(SizeLegend(Layout(SeriesData.From(Area, Revenue)))); // no sizes, no size legend
    }

    [Fact]
    public void The_size_legend_sits_under_a_right_legend()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Legend = new LegendOptions { Position = LegendPosition.Right },
            Series = [new SeriesOptions { Id = "a", Name = "Lund", Type = SeriesType.Scatter }, new SeriesOptions { Id = "b", Name = "Malmö", Type = SeriesType.Scatter }],
        }, new Dictionary<string, SeriesData> { ["a"] = Stores(), ["b"] = Stores() }, Options);

        var title = All<TextNode>(SizeLegend(layout)!).First();
        var items = All<TextNode>(All<GroupNode>(layout.Scene.Static).Single(g => g.Class == "rio-legend"));
        Assert.True(title.At.Y > items.Max(t => t.At.Y));
        Assert.Equal(items[0].At.X - 18, title.At.X, 1); // left-aligned with the swatches
    }

    [Fact]
    public void The_json_legend_turns_the_size_legend_off()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "legend": { "sizes": false }, "series": [ { "type": "bubble", "x": "a", "y": "b", "size": "c" } ] }""");

        Assert.False(parsed.Definition!.Legend.Sizes);
    }

    // ---------- JSON definitions ----------

    private static Dictionary<string, ChartDataset> Dataset() => new()
    {
        ["d"] = ChartDataset.FromRows("d", Area.Select((a, i) => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["area"] = a, ["revenue"] = Revenue[i], ["visitors"] = Visitors[i], ["city"] = i < 3 ? "Lund" : "Malmö",
        })),
    };

    [Fact]
    public void A_json_bubble_chart_binds_the_size_field()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "bubble", "x": "area", "y": "revenue", "size": "visitors" } ] }""");

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, Dataset());

        Assert.Empty(bound.Errors);
        var series = bound.Definition!.Series.Single();
        Assert.Equal(SeriesType.Scatter, series.Type);
        Assert.Equal("Visitors", series.SizeName);
        Assert.Equal(Visitors, bound.Data.Values.Single().Sizes!);
        var razor = ChartRazor.FromDefinition(parsed.Definition!, "Store", "stores");
        Assert.Contains("<ScatterSeries", razor, StringComparison.Ordinal);
        Assert.Contains(" SizeValue=\"r => r.Visitors\"", razor, StringComparison.Ordinal);
    }

    [Fact]
    public void Aggregated_bubbles_add_up_their_sizes()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "scatter", "x": "city", "y": "revenue", "aggregate": "average", "size": "visitors" } ] }""");

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, Dataset());

        Assert.Empty(bound.Errors);
        Assert.Equal([1400.0, 4100], bound.Data.Values.Single().Sizes!);
    }

    [Fact]
    public void Auto_with_a_size_is_a_bubble_chart_and_other_types_ignore_it()
    {
        var auto = ChartDefinitionBinder.Bind(ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "auto", "x": "area", "y": "revenue", "size": "visitors" } ] }""").Definition!, Dataset());
        Assert.Equal(SeriesType.Scatter, auto.Definition!.Series.Single().Type);

        var line = ChartDefinitionBinder.Bind(ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "line", "x": "area", "y": "revenue", "size": "visitors" } ] }""").Definition!, Dataset());
        Assert.Contains(line.Warnings, w => w.Contains("only scatter series have sizes", StringComparison.Ordinal));
        Assert.Null(line.Data.Values.Single().Sizes);

        var text = ChartDefinitionBinder.Bind(ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "bubble", "x": "area", "y": "revenue", "size": "city" } ] }""").Definition!, Dataset());
        Assert.Contains(text.Errors, e => e.Path == "series[0].size");
    }
}
