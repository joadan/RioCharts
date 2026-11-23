using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.AI;
using RioCharts.Core.Scene;
using RioCharts.Core.Definitions;

namespace RioCharts.Tests;

public class BoxPlotTests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };

    private sealed record Reading(string Station, double? Temp);

    // Outdoor temperatures in °C per weather station; Station 2 has one reading far off.
    private static readonly Reading[] Readings =
    [
        .. new[] { 18.0, 18.5, 19.0, 19.5, 20.0, 20.5, 21.0 }.Select(t => new Reading("Station 1", t)),
        .. new[] { 19.0, 19.2, 19.4, 19.6, 19.8, 20.0, 27.5 }.Select(t => new Reading("Station 2", t)),
        new Reading("Station 3", null),
    ];

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    // ---------- Statistics ----------

    [Fact]
    public void Quartiles_interpolate_between_sorted_values()
    {
        var odd = BoxSummary.FromValues([9, 1, 5, 3, 7, 2, 8, 4, 6]);
        var even = BoxSummary.FromValues([4, 1, 3, 2]);

        Assert.Equal((3.0, 5.0, 7.0), (odd.Q1, odd.Median, odd.Q3));
        Assert.Equal((1.0, 9.0, 9), (odd.Low, odd.High, odd.Count));
        Assert.Empty(odd.Outliers);
        Assert.Equal((1.75, 2.5, 3.25), (even.Q1, even.Median, even.Q3));
    }

    [Fact]
    public void Values_beyond_one_and_a_half_box_lengths_are_outliers()
    {
        var box = BoxSummary.FromValues([1, 2, 3, 4, 5, 6, 7, 8, 9, 30, -20]);

        Assert.Equal([-20.0, 30.0], box.Outliers);
        Assert.Equal((1.0, 9.0), (box.Low, box.High)); // whiskers stop at the last value inside
        Assert.Equal((-20.0, 30.0), (box.Min, box.Max));
    }

    [Fact]
    public void Edge_cases_give_a_clean_box_or_a_gap()
    {
        var empty = BoxSummary.FromValues([double.NaN]);
        var single = BoxSummary.FromValues([21.5]);
        var equal = BoxSummary.FromValues([20, 20, 20]);

        Assert.False(empty.HasBox);
        Assert.True(single.HasBox);
        Assert.Equal((21.5, 21.5, 21.5, 21.5, 21.5), (single.Low, single.Q1, single.Median, single.Q3, single.High));
        Assert.Empty(equal.Outliers);
    }

    [Fact]
    public void Five_numbers_are_put_in_order()
    {
        var box = BoxSummary.FromFiveNumbers(22, 18, 20, 19, 21, outliers: [30, 10], count: 12);

        Assert.Equal((18.0, 19.0, 20.0, 21.0, 22.0), (box.Low, box.Q1, box.Median, box.Q3, box.High));
        Assert.Equal((10.0, 30.0, 12), (box.Min, box.Max, box.Count));
    }

    // ---------- Data ----------

    [Fact]
    public void Raw_values_group_by_x_into_boxes()
    {
        var data = SeriesData.FromBoxGroups(Readings, r => r.Station, r => r.Temp, out var groups);

        Assert.Equal(["Station 1", "Station 2", "Station 3"], data.Categories);
        Assert.Equal(19.5, data.Y[0]); // the median
        Assert.Equal(19.6, data.Y[1], 6);
        Assert.True(double.IsNaN(data.Y[2])); // no values: a gap
        Assert.Equal([27.5], data.Box!.OutliersOf(1).ToArray());
        Assert.Empty(data.Box.OutliersOf(0).ToArray());
        Assert.Equal(7, data.Box.Count[1]);
        Assert.Equal(7, groups[0].Length);
    }

    [Fact]
    public void Boxes_follow_their_points_when_x_is_sorted()
    {
        var data = SeriesData.From([3.0, 1, 2], [0.0, 0, 0]).WithBox(
        [
            BoxSummary.FromValues([30, 31, 32]),
            BoxSummary.FromValues([10, 11, 12]),
            BoxSummary.FromValues([20, 21, 22]),
        ]);

        Assert.Equal([1.0, 2, 3], data.X);
        Assert.Equal([11.0, 21, 31], data.Y);
        Assert.Equal([10.0, 20, 30], data.Box!.Low);
    }

    // ---------- Layout ----------

    private static ChartLayoutResult Layout(params SeriesOptions[] series) => Layout(series, null);

    private static ChartLayoutResult Layout(SeriesOptions[] series, Dictionary<string, SeriesData>? data)
    {
        var spec = new ChartDefinition { Series = series };
        data ??= series.ToDictionary(s => s.Id, _ => SeriesData.FromBoxGroups(Readings, r => r.Station, r => r.Temp, out IReadOnlyList<int[]> _));
        return ChartLayout.Compute(spec, data, Options);
    }

    [Fact]
    public void Boxes_whiskers_medians_and_outliers_are_drawn()
    {
        var layout = Layout(new SeriesOptions { Id = "t", Type = SeriesType.BoxPlot, Name = "Temperature" });

        var nodes = All<Node>(layout.Scene.Series);
        Assert.Equal(2, nodes.OfType<RectNode>().Count(n => n.Class == "rio-box")); // Station 3 is a gap
        Assert.Single(nodes, n => n.Class == "rio-whiskers");
        Assert.Single(nodes, n => n.Class == "rio-medians");
        Assert.Single(nodes, n => n.Class == "rio-outliers");
        Assert.Empty(layout.Warnings);
    }

    [Fact]
    public void The_y_axis_fits_the_outliers_and_does_not_force_zero()
    {
        var layout = Layout(new SeriesOptions { Id = "t", Type = SeriesType.BoxPlot });

        Assert.True(layout.YScale.DomainMax >= 27.5);
        Assert.True(layout.YScale.DomainMin > 0 && layout.YScale.DomainMin <= 18);
    }

    [Fact]
    public void Two_box_series_sit_side_by_side_and_are_never_stacked()
    {
        var layout = Layout(
            new SeriesOptions { Id = "a", Type = SeriesType.BoxPlot, Stack = "s" },
            new SeriesOptions { Id = "b", Type = SeriesType.BoxPlot, Stack = "s" });

        var boxes = All<RectNode>(layout.Scene.Series).Where(n => n.Class == "rio-box").ToList();
        Assert.Equal(4, boxes.Count);
        Assert.True(boxes[2].Bounds.X > boxes[0].Bounds.Right); // b's Station 1 box is right of a's
        Assert.Equal(boxes[0].Bounds.Y, boxes[2].Bounds.Y); // same data, not stacked on top
    }

    [Fact]
    public void Tooltip_lists_the_box_under_the_median()
    {
        var layout = Layout(new SeriesOptions { Id = "t", Type = SeriesType.BoxPlot, Format = "0.0" });

        var s = layout.Tooltip.Series.Single();
        Assert.Equal("19.6", s.Values[1]);
        Assert.Equal(["Max", "Upper quartile", "Median", "Lower quartile", "Min", "Count", "Outliers"], s.PartLabels!);
        Assert.Equal(["27.5", "19.9", "19.6", "19.3", "19.0", "7", "1: 27.5"], s.PartValues![1]);
        Assert.Equal("", s.PartValues[0][6]); // no outliers: the row is left out
    }

    [Fact]
    public void A_box_series_without_boxes_warns_and_draws_nothing()
    {
        var layout = Layout([new SeriesOptions { Id = "t", Type = SeriesType.BoxPlot }],
            new Dictionary<string, SeriesData> { ["t"] = SeriesData.FromCategories(["a"], [1.0]) });

        Assert.Contains(layout.Warnings, w => w.Contains("box plot needs boxes"));
        Assert.DoesNotContain(All<Node>(layout.Scene.Series), n => n.Class == "rio-box");
    }

    [Fact]
    public void Empty_box_data_renders_the_empty_state()
    {
        var layout = Layout([new SeriesOptions { Id = "t", Type = SeriesType.BoxPlot }],
            new Dictionary<string, SeriesData> { ["t"] = SeriesData.FromBoxGroups(Array.Empty<Reading>(), r => r.Station, r => r.Temp, out _) });

        Assert.False(layout.HasData);
    }

    [Fact]
    public void A_selected_box_is_outlined_from_quartile_to_quartile()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "t", Type = SeriesType.BoxPlot }] };
        var data = new Dictionary<string, SeriesData> { ["t"] = SeriesData.FromBoxGroups(Readings, r => r.Station, r => r.Temp, out _) };

        var layout = ChartLayout.Compute(spec, data, Options with { SelectedPoints = new Dictionary<string, IReadOnlyCollection<int>> { ["t"] = [0] } });

        var box = All<RectNode>(layout.Scene.Series).First(n => n.Class == "rio-box");
        var outline = All<RectNode>(layout.Scene.Series).Single(n => n.Class == "rio-selected-bar");
        Assert.Equal(box.Bounds.Y - 1.5, outline.Bounds.Y, 1);
        Assert.Equal(box.Bounds.Height + 3, outline.Bounds.Height, 1);
    }

    // ---------- JSON definitions ----------

    private static Dictionary<string, ChartDataset> Rows(params (string Station, double Temp)[] values) => new()
    {
        ["d"] = ChartDataset.FromRows("d", values.Select(v => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["station"] = v.Station, ["temp"] = v.Temp })),
    };

    [Fact]
    public void A_json_box_plot_computes_the_boxes_from_the_raw_values_per_x()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "box", "x": "station", "y": "temp" } ] }""");

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, Rows(("Station 1", 18), ("Station 1", 20), ("Station 1", 22), ("Station 2", 19), ("Station 2", 21)));

        Assert.Empty(bound.Errors);
        Assert.DoesNotContain(bound.Warnings, w => w.Contains("using sum")); // repeating x is the point here
        Assert.Equal(SeriesType.BoxPlot, bound.Definition.Series.Single().Type);
        var data = bound.Data.Values.Single();
        Assert.Equal([20.0, 20], data.Y);
        Assert.Equal([3, 2], data.Box!.Count);
        Assert.Contains("<BoxPlotSeries", ChartRazor.FromDefinition(parsed.Definition!, "Row", "rows"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_json_box_plot_reads_precomputed_boxes()
    {
        var rows = new[] { (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["station"] = "Station 1", ["lo"] = 17.0, ["p25"] = 18.5, ["med"] = 19.4, ["p75"] = 20.1, ["hi"] = 21.0,
        } };
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "boxPlot", "x": "station", "y": "med", "low": "lo", "q1": "p25", "q3": "p75", "high": "hi" } ] }""");

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, new Dictionary<string, ChartDataset> { ["d"] = ChartDataset.FromRows("d", rows) });

        Assert.Empty(bound.Errors);
        var box = bound.Data.Values.Single().Box!;
        Assert.Equal((17.0, 18.5, 20.1, 21.0), (box.Low[0], box.Q1[0], box.Q3[0], box.High[0]));
        var razor = ChartRazor.FromDefinition(parsed.Definition!, "Row", "rows");
        Assert.Contains(" Q1Value=\"r => r.P25\" Q3Value=\"r => r.P75\" LowValue=\"r => r.Lo\" HighValue=\"r => r.Hi\"", razor, StringComparison.Ordinal);
    }

    [Fact]
    public void A_json_box_plot_with_only_one_quartile_explains_what_is_missing()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "boxPlot", "x": "station", "y": "temp", "q1": "temp" } ] }""");

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, Rows(("Station 1", 18)));

        Assert.Equal("series[0].q3", Assert.Single(bound.Errors).Path);
    }
}
