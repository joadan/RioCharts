using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Skia;

namespace RioCharts.Tests;

/// <summary>Edge cases that threw or drew outside the chart (found in a review of the layouts).</summary>
public class LayoutEdgeCaseTests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };

    private static ChartLayoutResult Layout(ChartDefinition spec, SeriesData data, LayoutOptions? options = null) =>
        ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { [spec.Series[0].Id] = data }, options ?? Options);

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    private static IEnumerable<Node> Nodes(ChartLayoutResult layout) =>
        All<Node>(layout.Scene.Static).Concat(All<Node>(layout.Scene.Series)).Concat(All<Node>(layout.Scene.Overlay));

    [Fact]
    public void A_callout_wider_than_the_plot_is_shortened_not_a_crash()
    {
        var spec = new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "l" }],
            Annotations = [new Annotation { Type = AnnotationType.Callout, X = 1.0, Y = 2.0, Label = "A callout label that is clearly much wider than the whole plot area" }],
        };

        var layout = Layout(spec, SeriesData.From([0.0, 1, 2], [1.0, 2, 3]), Options with { Width = 300 });

        var label = Assert.Single(Nodes(layout).OfType<TextNode>(), t => t.Class == "rio-callout-label");
        Assert.EndsWith("…", label.Text, StringComparison.Ordinal);
        var box = Assert.Single(Nodes(layout).OfType<RectNode>(), r => r.Class == "rio-callout-box");
        Assert.True(box.Bounds.X >= layout.Plot.X && box.Bounds.Right <= layout.Plot.Right + 0.5f);
    }

    [Fact]
    public void A_bullet_with_fixed_limits_between_nice_steps_still_gets_ticks()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "b", Type = SeriesType.Bullet, Min = 5.1, Max = 9.3 }] };

        var layout = Layout(spec, SeriesData.FromCategories(["A"], [7.0]), Options with { Width = 200 });

        Assert.True(layout.HasData);
    }

    [Fact]
    public void A_null_field_name_in_json_is_an_error_not_a_crash()
    {
        var parsed = ChartDefinitionJson.Parse("""{"dataset":"d","series":[{"type":"bar","x":"region","y":"sales","filter":{"field":null,"op":"eq","value":"x"}}]}""");
        var rows = new List<IReadOnlyDictionary<string, object?>> { new Dictionary<string, object?> { ["region"] = "x", ["sales"] = 1.0 } };

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, new Dictionary<string, ChartDataset> { ["d"] = ChartDataset.FromRows("d", rows) });

        var error = Assert.Single(bound.Errors);
        Assert.Equal("series[0].filter[0].field", error.Path);
        Assert.Contains("available: ", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_y_min_above_every_value_keeps_a_usable_axis()
    {
        // Winter temperatures (°C) with the axis asked to start at 0.
        var spec = new ChartDefinition { Y = new AxisOptions { Min = 0 }, Series = [new SeriesOptions { Id = "l" }] };

        var layout = Layout(spec, SeriesData.From([0.0, 1, 2], [-5.0, -3, -1]));

        Assert.Equal(0, layout.YScale.DomainMin);
        Assert.True(layout.YScale.DomainMax > 0);
    }

    [Fact]
    public void A_y_max_below_every_value_keeps_a_usable_axis()
    {
        var spec = new ChartDefinition { Y = new AxisOptions { Max = 0 }, Series = [new SeriesOptions { Id = "l" }] };

        var layout = Layout(spec, SeriesData.From([0.0, 1, 2], [1.0, 3, 5]));

        Assert.Equal(0, layout.YScale.DomainMax);
        Assert.True(layout.YScale.DomainMin < 0);
    }

    [Fact]
    public void An_x_min_beyond_the_data_keeps_the_fixed_end()
    {
        var spec = new ChartDefinition { X = new AxisOptions { Min = 10 }, Series = [new SeriesOptions { Id = "l" }] };

        var layout = Layout(spec, SeriesData.From([0.0, 1, 2], [1.0, 2, 3]));

        Assert.Equal(10, layout.XScale!.DomainMin);
        Assert.True(layout.XScale.DomainMax > 10);
    }

    [Fact]
    public void Horizontal_bars_stay_in_the_plot_with_a_fixed_value_range()
    {
        var spec = new ChartDefinition { Horizontal = true, Y = new AxisOptions { Min = 50, Max = 80 }, Series = [new SeriesOptions { Id = "b", Type = SeriesType.Bar }] };

        var layout = Layout(spec, SeriesData.FromCategories(["A", "B"], [60.0, 100]));

        var plot = Assert.Single(All<GroupNode>(layout.Scene.Series), g => g.Class == "rio-plot");
        Assert.NotNull(plot.Clip);
        Assert.All(All<RectNode>(plot), r => Assert.True(r.Bounds.X >= layout.Plot.X - 0.5f && r.Bounds.Right <= layout.Plot.Right + 0.5f));
    }

    [Fact]
    public void Heatmap_cells_are_clipped_to_the_plot()
    {
        var spec = new ChartDefinition { X = new AxisOptions { Min = 12 }, Series = [new SeriesOptions { Id = "h", Type = SeriesType.Heatmap }] };

        var layout = Layout(spec, SeriesData.From([.. Enumerable.Range(0, 24).Select(h => (double)h)], [.. Enumerable.Range(0, 24).Select(h => (double)h)]));

        Assert.NotNull(Assert.Single(All<GroupNode>(layout.Scene.Series), g => g.Class == "rio-plot").Clip);
    }

    [Fact]
    public void A_funnel_with_many_stages_stays_in_the_chart()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "f", Type = SeriesType.Funnel }] };
        var names = Enumerable.Range(1, 30).Select(i => "Stage " + i).ToList();
        var data = new Dictionary<string, SeriesData> { ["f"] = SeriesData.FromCategories(names, [.. Enumerable.Range(1, 30).Select(i => 100.0 - i)]) };

        var svg = ChartExport.ToSvg(spec, data, new ExportOptions { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture });

        var ys = System.Text.RegularExpressions.Regex.Matches(svg, " y=\"([0-9.]+)\"").Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        Assert.True(ys.Max() <= 300, $"lowest y {ys.Max()}");
    }

    [Fact]
    public void A_radar_with_equal_limits_warns_and_uses_a_real_range()
    {
        var spec = new ChartDefinition { Y = new AxisOptions { Min = 5, Max = 5 }, Series = [new SeriesOptions { Id = "r", Type = SeriesType.Radar }] };

        var layout = Layout(spec, SeriesData.FromCategories(["A", "B", "C"], [5.0, 5, 5]));

        Assert.Contains(layout.Warnings, w => w.Contains("must be above min", StringComparison.Ordinal));
        var svg = ChartExport.ToSvg(spec, new Dictionary<string, SeriesData> { ["r"] = SeriesData.FromCategories(["A", "B", "C"], [5.0, 5, 5]) },
            new ExportOptions { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture });
        Assert.DoesNotContain("M0 0L0 0L0 0", svg, StringComparison.Ordinal); // NaN geometry is written as 0
    }

    [Fact]
    public void The_description_gives_times_in_the_display_zone()
    {
        // 11:00 UTC is 12:00 in Stockholm (UTC+1 in March).
        var spec = new ChartDefinition { TimeZone = "Europe/Stockholm", Series = [new SeriesOptions { Id = "l", Name = "Temperature" }] };
        var data = SeriesData.From([new DateTime(2026, 3, 1, 11, 0, 0, DateTimeKind.Utc), new DateTime(2026, 3, 1, 13, 0, 0, DateTimeKind.Utc)], [2.0, 5]);

        var layout = Layout(spec, data);

        Assert.Contains("12:00", layout.Scene.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("11:00", layout.Scene.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void The_description_writes_numeric_x_like_the_axis()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "l" }] };

        var layout = Layout(spec, SeriesData.From([12345.0, 23456], [1.0, 2]));

        Assert.Contains("12,345", layout.Scene.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("E+", layout.Scene.Description, StringComparison.Ordinal);
    }

    private static int Count(PathNode path, PathCommand command) => path.Path.Commands.Count(c => c == command);

    [Fact]
    public void A_hidden_treemap_group_keeps_its_total_in_the_legend_tooltip()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "t", Type = SeriesType.Treemap }] };
        var data = SeriesData.FromCategories(["Europe/Sweden/Stockholm", "Europe/Norway/Oslo", "Asia/Japan/Tokyo"], [10.0, 5, 7]);

        var layout = Layout(spec, data, Options with { HiddenSeries = new HashSet<string> { "Europe" } });

        var europe = Assert.Single(layout.Legend, e => e.SeriesId == "Europe");
        Assert.StartsWith("Europe\t15", europe.Tooltip, StringComparison.Ordinal);
        Assert.Contains("Sweden\t10", europe.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void A_gap_in_a_stacked_series_is_a_gap_in_the_series_above_it()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "a", Stack = "s" }, new SeriesOptions { Id = "b", Stack = "s" }] };
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.From([0.0, 1, 2], [10.0, double.NaN, 10]),
            ["b"] = SeriesData.From([0.0, 1, 2], [5.0, 5, 5]),
        };

        var layout = ChartLayout.Compute(spec, data, Options);

        // b's top would drop from 15 to 5 at x = 1 if a's gap counted as 0; it has no base there, so no point.
        var b = Assert.Single(layout.Tooltip.Series, s => s.Id == "b");
        Assert.DoesNotContain(1, b.Index);
        Assert.Equal(2, b.Index.Length);
    }

    [Fact]
    public void A_pie_with_equal_start_and_end_angles_is_a_full_circle()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "p", Type = SeriesType.Pie, StartAngle = 90, EndAngle = 90 }] };

        var layout = Layout(spec, SeriesData.FromCategories(["A", "B"], [1.0, 3]));

        Assert.Contains(layout.Warnings, w => w.Contains("too close", StringComparison.Ordinal));
        Assert.Equal(2, layout.Tooltip.Slices.Count);
        Assert.All(layout.Tooltip.Slices, s => Assert.True(s.A1 - s.A0 > 0.5));
    }

    [Fact]
    public void Other_never_takes_every_slice_and_its_shares_count_it_when_hidden()
    {
        var even = new ChartDefinition { Series = [new SeriesOptions { Id = "p", Type = SeriesType.Pie, GroupSmallSlices = 0.03 }] };
        var forty = SeriesData.FromCategories([.. Enumerable.Range(1, 40).Select(i => "C" + i)], [.. Enumerable.Repeat(1.0, 40)]);
        Assert.DoesNotContain(Layout(even, forty).Legend, e => e.SeriesId == ChartLayout.OtherSliceKey);

        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "p", Type = SeriesType.Pie, GroupSmallSlices = 0.05 }] };
        var data = SeriesData.FromCategories(["A", "B", "C", "D", "E"], [50.0, 40, 6, 2, 2]);
        var layout = Layout(spec, data, Options with { HiddenSeries = new HashSet<string> { ChartLayout.OtherSliceKey } });

        var other = Assert.Single(layout.Legend, e => e.SeriesId == ChartLayout.OtherSliceKey);
        Assert.StartsWith("Other\t4 (4", other.Tooltip, StringComparison.Ordinal); // 4 of 100, not of the 96 drawn
        Assert.DoesNotContain("∞", other.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void A_counter_clockwise_radial_bar_is_hovered_over_its_whole_track()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "r", Type = SeriesType.RadialBar, StartAngle = 0, EndAngle = -270 }] };

        var layout = Layout(spec, SeriesData.FromCategories(["A", "B"], [0.0, 50]));

        // The track runs 270° counter-clockwise from the start: hovering covers all of it, the empty bar included.
        Assert.All(layout.Tooltip.Slices, s => Assert.Equal(1.5 * Math.PI, s.HitEnd - s.HitStart!.Value, 3));
    }

    [Fact]
    public void A_waterfall_is_described_by_its_running_total()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "w", Name = "Cash", Type = SeriesType.Waterfall }] };

        var layout = Layout(spec, SeriesData.FromCategories(["Start", "Costs", "Sales"], [100.0, -30, 20]));

        Assert.Contains("90", layout.Scene.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("-30", layout.Scene.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Thinning_a_range_area_keeps_a_gap_in_its_low_values()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "r", Type = SeriesType.RangeArea }] };
        var x = Enumerable.Range(0, 2000).Select(i => (double)i).ToArray();
        var low = x.Select(i => i == 1000 ? double.NaN : Math.Sin(i / 50)).ToArray();
        var data = SeriesData.From(x, [.. x.Select(i => Math.Sin(i / 50) + 2)]).WithLow(low);

        var layout = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["r"] = data }, Options with { DownsampleThreshold = 100 });

        var range = Assert.Single(All<PathNode>(layout.Scene.Series), p => p.Class == "rio-range");
        Assert.True(Count(range, PathCommand.MoveTo) >= 2, "two pieces: the gap stays");
    }

    [Fact]
    public void A_trend_line_on_a_log_axis_follows_the_fit()
    {
        var spec = new ChartDefinition { Y = new AxisOptions { Scale = ScaleType.Log }, Series = [new SeriesOptions { Id = "s", Type = SeriesType.Scatter, TrendLine = true }] };
        var x = Enumerable.Range(1, 40).Select(i => (double)i).ToArray();

        var layout = Layout(spec, SeriesData.From(x, [.. x.Select(v => 10 + v * 25)]));

        var trend = Assert.Single(All<PathNode>(layout.Scene.Series), p => p.Class == "rio-trend");
        Assert.True(Count(trend, PathCommand.LineTo) > 2, "a curve, not one segment");
    }

    [Fact]
    public void Legend_items_say_hidden_in_the_chart_language()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "a", Name = "Temperatur" }, new SeriesOptions { Id = "b", Name = "Fukt" }] };
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From([0.0, 1], [1.0, 2]), ["b"] = SeriesData.From([0.0, 1], [3.0, 4]) };

        var layout = ChartLayout.Compute(spec, data, Options with { Culture = CultureInfo.GetCultureInfo("sv-SE"), HiddenSeries = new HashSet<string> { "a" } });

        var item = Assert.Single(All<GroupNode>(layout.Scene.Static), g => g.Key == "legend:a");
        Assert.Equal("Temperatur, dold", item.AriaLabel);
    }
}
