using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Specs;

namespace RioCharts.Tests;

public class RadarTests
{
    private static readonly LayoutOptions Options = new() { Width = 500, Height = 400, Culture = CultureInfo.InvariantCulture };

    // A laptop's test scores (0-100) against the class average.
    private static readonly string[] Measures = ["Performance", "Battery", "Display", "Keyboard", "Weight"];

    private static ChartSpec Spec(params SeriesSpec[] series) => new() { Series = series };

    private static SeriesSpec Radar(string id, string? name = null) => new() { Id = id, Name = name, Type = SeriesType.Radar };

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    private static List<T> In<T>(ChartLayoutResult result, string cls) where T : Node =>
        All<GroupNode>(result.Scene.Series).Concat(All<GroupNode>(result.Scene.Static)).Concat(All<GroupNode>(result.Scene.Overlay))
            .SelectMany(g => g.Children.OfType<T>()).Where(n => n.Class?.Split(' ').Contains(cls) == true).Distinct().ToList();

    [Fact]
    public void Spokes_start_at_the_top_and_go_clockwise()
    {
        var result = ChartLayout.Compute(Spec(Radar("a")), new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(Measures, [100.0, 100, 100, 100, 100]),
        }, Options);

        var shape = Assert.Single(In<PathNode>(result, "rio-radar-shape"));
        Assert.Equal(PathCommand.Close, shape.Path.Commands[^1]);
        var radar = result.Tooltip.Radar!;
        Assert.Equal(5, radar.Count);
        var (x0, y0) = (shape.Path.Coords[0], shape.Path.Coords[1]);
        Assert.Equal(radar.Cx, x0, 0.5);
        Assert.Equal(radar.Cy - radar.Radius, y0, 0.5); // the first category at 12 o'clock, at the full radius
        Assert.True(shape.Path.Coords[2] > radar.Cx); // the second to the right of it
        Assert.Equal(0, result.YScale.DomainMin); // from zero
    }

    [Fact]
    public void The_tooltip_is_shared_per_spoke_and_the_legend_lists_series()
    {
        var result = ChartLayout.Compute(Spec(Radar("unit", "Laptop"), Radar("ref", "Average")), new Dictionary<string, SeriesData>
        {
            ["unit"] = SeriesData.FromCategories(Measures, [82.0, 64, 71, 90, 55]),
            ["ref"] = SeriesData.FromCategories(Measures, [75.0, 75, 75, 75, 75]),
        }, Options);

        Assert.Equal(["unit", "ref"], result.Legend.Select(l => l.SeriesId));
        Assert.Contains("Performance\t82", result.Legend[0].Tooltip, StringComparison.Ordinal);
        Assert.Equal(2, result.Tooltip.Series.Count);
        Assert.True(result.Tooltip.Shared);
        Assert.Equal([0, 1, 2, 3, 4], result.Tooltip.Series[0].Spokes!);
        Assert.Equal("Display", result.Tooltip.Series[1].XLabels[2]);
        Assert.Equal(2, In<PathNode>(result, "rio-radar-shape").Count);
        Assert.Contains("Radar chart", result.Scene.Description, StringComparison.Ordinal);
        Assert.Equal(5, All<TextNode>(result.Scene.Static).Count(t => Measures.Contains(t.Text)));
    }

    [Fact]
    public void A_missing_value_breaks_the_shape_into_open_lines()
    {
        var result = ChartLayout.Compute(Spec(Radar("a")), new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(Measures, [80.0, double.NaN, 60, 70, 50]),
        }, Options);

        Assert.Empty(In<PathNode>(result, "rio-radar-shape"));
        var line = Assert.Single(In<PathNode>(result, "rio-line"));
        Assert.DoesNotContain(PathCommand.Close, line.Path.Commands);
        Assert.Equal(1, line.Path.Commands.Count(c => c == PathCommand.MoveTo)); // wraps around: Display, Keyboard, Weight, Performance
        Assert.Equal(4, result.Tooltip.Series[0].X.Length);
        Assert.Equal([0, 2, 3, 4], result.Tooltip.Series[0].Spokes!);
    }

    [Fact]
    public void A_value_between_two_gaps_is_a_dot()
    {
        var result = ChartLayout.Compute(Spec(Radar("a")), new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(Measures, [80.0, double.NaN, 60, double.NaN, 50]),
        }, Options);

        Assert.Single(In<PathNode>(result, "rio-point"));
        Assert.Single(In<PathNode>(result, "rio-line")); // Weight to Performance
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Fewer_than_three_spokes_still_render(int count)
    {
        var result = ChartLayout.Compute(Spec(Radar("a")), new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(Measures[..count], [.. Enumerable.Repeat(50.0, count)]),
        }, Options);

        Assert.True(result.HasData);
        Assert.Empty(In<PathNode>(result, "rio-radar-shape"));
        Assert.Single(In<PathNode>(result, count == 1 ? "rio-point" : "rio-line"));
        Assert.NotEmpty(In<PathNode>(result, "rio-radar-ring"));
    }

    [Fact]
    public void Empty_hidden_and_mixed_series_render_a_clean_state()
    {
        var empty = ChartLayout.Compute(Spec(Radar("a")), new Dictionary<string, SeriesData>(), Options);
        var hidden = ChartLayout.Compute(Spec(Radar("a")), new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(Measures, [1.0, 2, 3, 4, 5]),
        }, Options with { HiddenSeries = new HashSet<string> { "a" } });
        var mixed = ChartLayout.Compute(Spec(Radar("a"), new SeriesSpec { Id = "l", Type = SeriesType.Line }), new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(Measures, [1.0, 2, 3, 4, 5]),
            ["l"] = SeriesData.FromCategories(Measures, [1.0, 2, 3, 4, 5]),
        }, Options);
        var equal = ChartLayout.Compute(Spec(Radar("a")), new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(Measures, [0.0, 0, 0, 0, 0]),
        }, Options);

        Assert.False(empty.HasData);
        Assert.Single(In<TextNode>(empty, "rio-empty"));
        Assert.False(hidden.HasData);
        Assert.Contains(mixed.Warnings, w => w.Contains("'l'", StringComparison.Ordinal) && w.Contains("radar", StringComparison.Ordinal));
        Assert.Single(mixed.Tooltip.Series);
        Assert.True(equal.HasData);
        Assert.True(equal.YScale.DomainMax > 0);
    }

    [Fact]
    public void The_value_axis_takes_a_fixed_range()
    {
        var spec = Spec(Radar("a")) with { Y = new AxisSpec { Max = 100 } };

        var result = ChartLayout.Compute(spec, new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(Measures, [50.0, 50, 50, 50, 50]),
        }, Options);

        Assert.Equal(100, result.YScale.DomainMax);
        var radar = result.Tooltip.Radar!;
        Assert.Equal(radar.Cy - radar.Radius / 2, result.Tooltip.Series[0].Y[0], 0.5); // 50 is half way out
        // The scale values sit above the shapes (labels layer) on a backing, so shapes never cover them.
        var axis = All<GroupNode>(result.Scene.Series).Single(g => g.Class == "rio-axis-y");
        Assert.Contains(All<TextNode>(axis), t => t.Text == "100");
        Assert.Equal(All<TextNode>(axis).Count, axis.Children.OfType<RectNode>().Count());
    }

    [Fact]
    public void Long_spoke_names_are_shortened()
    {
        string[] names = ["Battery life while streaming video at full brightness", "B", "C", "D", "E", "F"];
        var result = ChartLayout.Compute(Spec(Radar("a")), new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(names, [1.0, 2, 3, 4, 5, 6]),
        }, Options with { Width = 320, Height = 260 });

        Assert.Contains(All<TextNode>(result.Scene.Static), t => t.Text.EndsWith('…'));
        Assert.Equal(names[0], result.Tooltip.Series[0].XLabels[0]); // the tooltip keeps the full name
    }

    [Fact]
    public void A_json_spider_chart_is_a_radar()
    {
        var parsed = ChartSpecJson.Parse("""{ "dataset": "d", "series": [ { "type": "spider", "x": "measure", "y": "score" } ] }""");
        var rows = Measures.Select((m, i) => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["measure"] = m, ["score"] = 50.0 + i });

        var bound = ChartSpecBinder.Bind(parsed.Spec!, new Dictionary<string, ChartDataset> { ["d"] = ChartDataset.FromRows("d", rows) });

        Assert.Empty(bound.Errors);
        Assert.Equal(SeriesType.Radar, bound.Spec.Series.Single().Type);
        Assert.True(ChartLayout.Compute(bound.Spec, bound.Data, Options).HasData);
        Assert.Contains("<RadarSeries", ChartRazor.FromSpec(parsed.Spec!, "Row", "rows"), StringComparison.Ordinal);
    }
}
