using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class ErrorBarTests
{
    private static readonly LayoutOptions Options = new() { Width = 500, Height = 300, Culture = CultureInfo.InvariantCulture };

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    private static ChartLayoutResult Layout(ChartDefinition spec, Dictionary<string, SeriesData> data, LayoutOptions? options = null) =>
        ChartLayout.Compute(spec, data, options ?? Options);

    private static ChartLayoutResult One(SeriesOptions series, SeriesData data, LayoutOptions? options = null) =>
        Layout(new ChartDefinition { Series = [series with { Id = "s" }] }, new() { ["s"] = data }, options);

    private static readonly SeriesData Scores = SeriesData.FromCategories(["A", "B", "C"], [72.0, 65, 80]).WithErrors([64.0, 59, 75], [80.0, 71, 95]);

    private static PathNode? Whiskers(ChartLayoutResult layout) => All<PathNode>(layout.Scene.Series).SingleOrDefault(p => p.Class == "rio-error-bar");


    private static List<(double X0, double Y0, double X1, double Y1)> Lines(PathNode path)
    {
        var c = path.Path.Coords;
        var lines = new List<(double, double, double, double)>();
        for (var k = 0; k + 3 < c.Count; k += 12)
        {
            lines.Add((c[k], c[k + 1], c[k + 2], c[k + 3]));
        }

        return lines;
    }

    [Fact]
    public void Bars_get_a_capped_line_from_low_to_high()
    {
        var layout = One(new SeriesOptions { Type = SeriesType.Bar }, Scores);

        var path = Whiskers(layout);
        Assert.NotNull(path);
        var lines = Lines(path);
        Assert.Equal(3, lines.Count);
        var bars = All<RectNode>(layout.Scene.Series).Where(r => r.Class == "rio-bar").ToList();
        var y = layout.YScale!;
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(bars[i].Bounds.X + bars[i].Bounds.Width / 2, lines[i].X0, 0.5); // centered on its bar
            Assert.Equal(y.Map(Scores.ErrorLow![i]), lines[i].Y0, 0.5);
            Assert.Equal(y.Map(Scores.ErrorHigh![i]), lines[i].Y1, 0.5);
        }

        Assert.Equal(ThemeTokens.Text, layout.Scene.Styles.Get(path.Style).Stroke);
        Assert.Equal(["72 ± 8", "65 ± 6", "80 (75 – 95)"], layout.Tooltip.Series[0].Values);
    }

    [Fact]
    public void The_axis_makes_room_for_the_error_bars()
    {
        var with = One(new SeriesOptions { Type = SeriesType.Bar }, Scores);
        var without = One(new SeriesOptions { Type = SeriesType.Bar }, SeriesData.FromCategories(["A", "B", "C"], [72.0, 65, 80]));

        Assert.True(with.YScale!.DomainMax >= 95);
        Assert.True(without.YScale!.DomainMax < 95);
    }

    [Fact]
    public void An_amount_for_every_point_works_on_lines_and_scatter()
    {
        var data = SeriesData.From([1.0, 2, 3], [10.0, 12, 11]);
        foreach (var type in new[] { SeriesType.Line, SeriesType.Scatter })
        {
            var layout = One(new SeriesOptions { Type = type, Error = 1.5, Color = "#336699" }, data);

            var path = Whiskers(layout);
            Assert.NotNull(path);
            Assert.Equal(3, Lines(path).Count);
            Assert.Equal("#336699", layout.Scene.Styles.Get(path.Style).Stroke); // points and lines: the series color
            Assert.Equal(["10 ± 1.5", "12 ± 1.5", "11 ± 1.5"], layout.Tooltip.Series[0].Values);
            Assert.Empty(layout.Warnings);
        }
    }

    [Fact]
    public void Points_without_an_error_and_gaps_get_none()
    {
        var data = SeriesData.FromCategories(["A", "B", "C", "D"], [5.0, double.NaN, 7, 8]).WithErrors([4.0, 1, double.NaN, 7], [6.0, 2, 9, 9]);
        var layout = One(new SeriesOptions { Type = SeriesType.Bar }, data);

        Assert.Equal(2, Lines(Whiskers(layout)!).Count); // A and D
        Assert.Equal(["5 ± 1", "7", "8 ± 1"], layout.Tooltip.Series[0].Values);
    }

    [Fact]
    public void Stacked_bars_carry_their_error_bars_on_top()
    {
        var layout = Layout(new ChartDefinition
        {
            Series =
            [
                new SeriesOptions { Id = "a", Type = SeriesType.Bar, Stack = "s" },
                new SeriesOptions { Id = "b", Type = SeriesType.Bar, Stack = "s", Error = 2 },
            ],
        }, new() { ["a"] = SeriesData.FromCategories(["X"], [10.0]), ["b"] = SeriesData.FromCategories(["X"], [5.0]) });

        var line = Lines(Whiskers(layout)!).Single();
        var y = layout.YScale!;
        Assert.Equal(y.Map(13), line.Y0, 0.5); // 15 - 2
        Assert.Equal(y.Map(17), line.Y1, 0.5);
        Assert.Equal("5 ± 2", layout.Tooltip.Series[1].Values[0]);
    }

    [Fact]
    public void Percent_stacks_and_other_types_skip_error_bars_with_a_warning()
    {
        var percent = Layout(new ChartDefinition
        {
            Y = new AxisOptions { Stacking = StackMode.Percent },
            Series = [new SeriesOptions { Id = "a", Type = SeriesType.Bar, Stack = "s", Error = 1 }, new SeriesOptions { Id = "b", Type = SeriesType.Bar, Stack = "s" }],
        }, new() { ["a"] = SeriesData.FromCategories(["X"], [10.0]), ["b"] = SeriesData.FromCategories(["X"], [5.0]) });
        Assert.Null(Whiskers(percent));
        Assert.Contains(percent.Warnings, w => w.Contains("stacked to 100 %", StringComparison.Ordinal));

        var area = One(new SeriesOptions { Type = SeriesType.Area, Error = 1 }, SeriesData.From([1.0, 2], [3.0, 4]));
        Assert.Null(Whiskers(area));
        Assert.Contains(area.Warnings, w => w.Contains("only bar, line and scatter", StringComparison.Ordinal));
    }

    [Fact]
    public void Horizontal_bars_get_horizontal_error_bars()
    {
        var layout = Layout(new ChartDefinition { Horizontal = true, Series = [new SeriesOptions { Id = "s", Type = SeriesType.Bar }] }, new() { ["s"] = Scores });

        var lines = Lines(Whiskers(layout)!);
        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.Equal(l.Y0, l.Y1, 0.01)); // across the bar
        Assert.True(lines[2].X1 - lines[2].X0 > lines[1].X1 - lines[1].X0); // C's 75–95 is wider than B's 59–71
        Assert.Equal("65 ± 6", layout.Tooltip.Series[0].Values[1]);
        Assert.True(lines[2].X1 <= layout.Plot.Right + 0.5); // the value axis makes room for 95
    }

    [Fact]
    public void A_log_axis_leaves_out_ends_below_zero()
    {
        var data = SeriesData.FromCategories(["A", "B"], [10.0, 100]).WithErrors([-5.0, 50], [20.0, 200]);
        var layout = Layout(new ChartDefinition { Y = new AxisOptions { Scale = ScaleType.Log }, Series = [new SeriesOptions { Id = "s", Type = SeriesType.Scatter }] },
            new() { ["s"] = data });

        Assert.True(layout.HasData);
        Assert.Single(Lines(Whiskers(layout)!)); // A's low end is not on the axis
    }

    [Fact]
    public void Errors_follow_their_points_when_sorted_again()
    {
        var times = new[] { new DateTime(2026, 3, 3), new DateTime(2026, 3, 1), new DateTime(2026, 3, 2) };
        var data = SeriesData.From(times, [3.0, 1, 2]).WithErrors([2.0, 0, 1], [4.0, 2, 3]).WithColors([null, "#f00", null]);

        Assert.Equal([0.0, 1, 2], data.ErrorLow!);
        Assert.Equal([2.0, 3, 4], data.ErrorHigh!);
    }

    // ---------- JSON definitions ----------

    private static Dictionary<string, ChartDataset> Tests() => new()
    {
        ["tests"] = ChartDataset.FromRows("tests", new (string Class, double Score, double Sd, double Low, double High)[]
            {
                ("A", 72, 8, 64, 80), ("B", 65, 6, 59, 71), ("A", 76, 4, 70, 84),
            }
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["class"] = r.Class, ["score"] = r.Score, ["sd"] = r.Sd, ["low"] = r.Low, ["high"] = r.High,
            })),
    };

    private static DefinitionBindResult Bind(string json)
    {
        var parsed = ChartDefinitionJson.Parse(json);
        Assert.True(parsed.Success, string.Join("; ", parsed.Errors.Select(e => e.Message)));
        return ChartDefinitionBinder.Bind(parsed.Definition!, Tests());
    }

    [Fact]
    public void A_json_error_field_is_a_plus_or_minus_amount()
    {
        var bound = Bind("""{ "dataset": "tests", "series": [ { "type": "bar", "x": "class", "y": "score", "errorField": "sd", "aggregate": "average" } ] }""");
        Assert.True(bound.Success, string.Join("; ", bound.Errors.Select(e => e.Message)));
        var data = bound.Data[bound.Definition.Series[0].Id];
        Assert.Equal([74.0, 65], data.Y);
        Assert.Equal([68.0, 59], data.ErrorLow!); // A: 74 ± the average of 8 and 4
        Assert.Equal([80.0, 71], data.ErrorHigh!);
    }

    [Fact]
    public void Json_error_ends_are_lowest_and_highest_when_aggregated()
    {
        var bound = Bind("""{ "dataset": "tests", "series": [ { "type": "line", "x": "class", "y": "score", "errorLow": "low", "errorHigh": "high", "aggregate": "average" } ] }""");
        Assert.True(bound.Success, string.Join("; ", bound.Errors.Select(e => e.Message)));
        var data = bound.Data[bound.Definition.Series[0].Id];
        Assert.Equal([64.0, 59], data.ErrorLow!);
        Assert.Equal([84.0, 71], data.ErrorHigh!);

        var half = Bind("""{ "dataset": "tests", "series": [ { "type": "bar", "x": "class", "y": "score", "errorLow": "low" } ] }""");
        Assert.Contains(half.Warnings, w => w.Contains("need both errorLow and errorHigh", StringComparison.Ordinal));
        Assert.Null(half.Data[half.Definition.Series[0].Id].ErrorLow);

        var pie = Bind("""{ "dataset": "tests", "series": [ { "type": "pie", "x": "class", "y": "score", "errorField": "sd" } ] }""");
        Assert.Contains(pie.Warnings, w => w.Contains("only bar, line and scatter", StringComparison.Ordinal));
    }

    [Fact]
    public void A_json_error_amount_is_kept_for_the_layout()
    {
        var bound = Bind("""{ "dataset": "tests", "series": [ { "type": "scatter", "x": "sd", "y": "score", "error": 2.5 } ] }""");
        Assert.True(bound.Success);
        Assert.Equal(2.5, bound.Definition.Series[0].Error);
        var layout = ChartLayout.Compute(bound.Definition, bound.Data, Options);
        Assert.NotNull(Whiskers(layout));
    }

    [Fact]
    public void Razor_for_error_bars_uses_the_error_parameters()
    {
        var amount = ChartRazor.FromDefinition(ChartDefinitionJson.Parse(
            """{ "dataset": "tests", "series": [ { "type": "bar", "x": "class", "y": "score", "errorField": "sd" } ] }""").Definition!);
        Assert.Contains(" ErrorValue=\"r => r.Sd\"", amount, StringComparison.Ordinal);

        var ends = ChartRazor.FromDefinition(ChartDefinitionJson.Parse(
            """{ "dataset": "tests", "series": [ { "type": "line", "x": "class", "y": "score", "errorLow": "low", "errorHigh": "high", "error": 1 } ] }""").Definition!);
        Assert.Contains(" ErrorLowValue=\"r => r.Low\" ErrorHighValue=\"r => r.High\"", ends, StringComparison.Ordinal);
        Assert.Contains(" Error=\"1\"", ends, StringComparison.Ordinal);
    }
}
