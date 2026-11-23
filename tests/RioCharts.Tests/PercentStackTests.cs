using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Definitions;

namespace RioCharts.Tests;

public class PercentStackTests
{
    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static readonly LayoutOptions Options = new() { Width = 640, Height = 320, Culture = CultureInfo.InvariantCulture };

    private static readonly string[] Quarters = ["Q1", "Q2"];

    private static ChartDefinition Percent(params SeriesOptions[] series) => new() { Y = new AxisOptions { Stacking = StackMode.Percent }, Series = series };

    private static SeriesOptions Bar(string id, string? stack = null) => new() { Id = id, Type = SeriesType.Bar, Stack = stack };

    private static List<RectNode> Bars(ChartLayoutResult r, string id) =>
        All(r.Scene.Series).Where(n => n is GroupNode g && g.Key == "series:" + id).SelectMany(All).OfType<RectNode>().ToList();

    private static List<string> YLabels(ChartLayoutResult r) =>
        [.. All(r.Scene.Static).Where(n => n.Class == "rio-axis-y").SelectMany(All).OfType<TextNode>().Select(t => t.Text)];

    // Height of a bar as a share of the axis from 0 to 1.
    private static double Share(ChartLayoutResult r, RectNode bar) => bar.Bounds.Height / (r.YScale.Map(0) - r.YScale.Map(1));

    [Fact]
    public void Series_without_a_stack_name_fill_the_axis_with_their_shares()
    {
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(Quarters, [10.0, 50]),
            ["b"] = SeriesData.FromCategories(Quarters, [30.0, 50]),
        };

        var layout = ChartLayout.Compute(Percent(Bar("a"), Bar("b")), data, Options);

        var (a, b) = (Bars(layout, "a"), Bars(layout, "b"));
        Assert.Equal((0.0, 1.0), (layout.YScale.DomainMin, layout.YScale.DomainMax));
        Assert.Equal(0.25, Share(layout, a[0]), 2);
        Assert.Equal(0.5, Share(layout, a[1]), 2);
        Assert.Equal(a[0].Bounds.X, b[0].Bounds.X, 0.01);                // one slot: stacked, not grouped
        Assert.Equal(layout.YScale.Map(1), b[0].Bounds.Y, 1.5);          // the stack reaches the top
        Assert.Contains("100 %", YLabels(layout));
        Assert.Equal("10 (25 %)", layout.Tooltip.Series[0].Values[0]); // the value and its share
    }

    [Fact]
    public void Negative_values_take_their_share_below_zero()
    {
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From([0.0], [30.0]), ["b"] = SeriesData.From([0.0], [-10.0]) };

        var layout = ChartLayout.Compute(Percent(Bar("a"), Bar("b")), data, Options);

        Assert.InRange(layout.YScale.DomainMin, -1, -0.25);
        Assert.Equal(1, layout.YScale.DomainMax);
        Assert.Equal(0.75, Share(layout, Bars(layout, "a")[0]), 0.02);
        Assert.Equal(0.25, Share(layout, Bars(layout, "b")[0]), 0.02);
    }

    [Fact]
    public void Hiding_a_series_gives_its_share_to_the_others()
    {
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.FromCategories(Quarters, [10.0, 50]), ["b"] = SeriesData.FromCategories(Quarters, [30.0, 50]) };

        var layout = ChartLayout.Compute(Percent(Bar("a"), Bar("b")), data, Options with { HiddenSeries = new HashSet<string> { "b" } });

        Assert.All(Bars(layout, "a"), bar => Assert.Equal(1, Share(layout, bar), 2));
    }

    [Fact]
    public void Areas_with_different_x_values_stack_per_x()
    {
        var spec = Percent(new SeriesOptions { Id = "a", Type = SeriesType.Area }, new SeriesOptions { Id = "b", Type = SeriesType.Area });
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From([0.0, 1], [2.0, 2]), ["b"] = SeriesData.From([1.0, 2], [6.0, 3]) };

        var layout = ChartLayout.Compute(spec, data, Options);

        Assert.Empty(layout.Warnings);
        Assert.Equal(["2 (100 %)", "2 (25 %)"], layout.Tooltip.Series[0].Values);
        Assert.Equal(["6 (75 %)", "3 (100 %)"], layout.Tooltip.Series[1].Values);
    }

    [Fact]
    public void Zeros_and_gaps_render_without_a_share()
    {
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(Quarters, [0.0, double.NaN]),
            ["b"] = SeriesData.FromCategories(Quarters, [0.0, 4]),
        };

        var layout = ChartLayout.Compute(Percent(Bar("a"), Bar("b")), data, Options);

        Assert.Equal(1, Share(layout, Bars(layout, "b")[^1]), 2);
        Assert.Equal(["0 (0 %)"], layout.Tooltip.Series[0].Values.Take(1));
    }

    [Fact]
    public void Named_stacks_each_fill_the_axis_and_other_series_are_left_alone()
    {
        var spec = Percent(Bar("a", "x"), Bar("b", "x"), Bar("c", "y"), new SeriesOptions { Id = "d", Type = SeriesType.Scatter });
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(Quarters, [1.0, 1]),
            ["b"] = SeriesData.FromCategories(Quarters, [3.0, 1]),
            ["c"] = SeriesData.FromCategories(Quarters, [7.0, 7]),
            ["d"] = SeriesData.FromCategories(Quarters, [0.5, 0.5]),
        };

        var layout = ChartLayout.Compute(spec, data, Options);

        Assert.NotEqual(Bars(layout, "a")[0].Bounds.X, Bars(layout, "c")[0].Bounds.X); // two stacks side by side
        Assert.Equal(1, Share(layout, Bars(layout, "c")[0]), 2);
        Assert.Contains(layout.Warnings, w => w.Contains("percent axis stacks line, area and bar series; this Scatter", StringComparison.Ordinal));
    }

    [Fact]
    public void A_normal_axis_without_stack_names_still_groups_bars()
    {
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.FromCategories(Quarters, [10.0, 50]), ["b"] = SeriesData.FromCategories(Quarters, [30.0, 50]) };

        var layout = ChartLayout.Compute(new ChartDefinition { Series = [Bar("a"), Bar("b")] }, data, Options);

        Assert.NotEqual(Bars(layout, "a")[0].Bounds.X, Bars(layout, "b")[0].Bounds.X);
        Assert.Equal("10", layout.Tooltip.Series[0].Values[0]);
    }

    [Fact]
    public void Horizontal_bars_stack_to_the_full_width()
    {
        var spec = Percent(Bar("a"), Bar("b")) with { Horizontal = true };
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.FromCategories(Quarters, [10.0, 50]), ["b"] = SeriesData.FromCategories(Quarters, [30.0, 50]) };

        var layout = ChartLayout.Compute(spec, data, Options);

        var (a, b) = (Bars(layout, "a"), Bars(layout, "b"));
        var full = b[0].Bounds.X + b[0].Bounds.Width - a[0].Bounds.X;
        Assert.Equal(0.25, a[0].Bounds.Width / full, 2);
        Assert.Equal(a[1].Bounds.Width + b[1].Bounds.Width, full, 1);
        Assert.Contains(All(layout.Scene.Static).OfType<TextNode>(), t => t.Text == "100 %");
        Assert.Equal("30 (75 %)", layout.Tooltip.Series[1].Values[0]);
    }

    [Theory]
    [InlineData("""{ "y": { "stacking": "100%" }, "series": [ { "type": "bar", "x": "q", "y": "v" } ] }""")]
    [InlineData("""{ "stacking": "normalize", "series": [ { "type": "bar", "x": "q", "y": "v" } ] }""")]
    [InlineData("""{ "series": [ { "type": "stackedBar100", "x": "q", "y": "v" } ] }""")]
    [InlineData("""{ "series": [ { "type": "100% stacked column", "x": "q", "y": "v" } ] }""")]
    [InlineData("""{ "series": [ { "type": "bar", "x": "q", "y": "v", "stacking": "percent" } ] }""")]
    public void Specs_ask_for_percent_stacking_in_several_ways(string json)
    {
        var parsed = ChartDefinitionJson.Parse(json);

        Assert.Empty(parsed.Errors);
        Assert.Equal(StackMode.Percent, parsed.Definition!.Y.Stacking);
    }

    [Fact]
    public void A_series_puts_its_stacking_on_its_own_axis()
    {
        var parsed = ChartDefinitionJson.Parse("""
            { "yAxes": [ { "id": "share" } ],
              "series": [ { "type": "area", "x": "t", "y": "v", "yAxisId": "share", "stacking": "percent" } ] }
            """);

        Assert.Empty(parsed.Errors);
        Assert.Equal(StackMode.Normal, parsed.Definition!.Y.Stacking);
        Assert.Equal(StackMode.Percent, parsed.Definition.YAxes[0].Stacking);
    }

    [Fact]
    public void Unknown_stacking_is_an_error_with_the_valid_values()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "y": { "stacking": "sideways" }, "series": [ { "type": "bar", "x": "q", "y": "v" } ] }""");

        Assert.Contains(parsed.Errors, e => e.Path == "y.stacking" && e.Message.Contains("percent", StringComparison.Ordinal));
    }

    [Fact]
    public void Razor_export_writes_the_stacking()
    {
        var spec = ChartDefinitionJson.Parse("""{ "y": { "title": "Share" }, "series": [ { "type": "stackedBar100", "x": "quarter", "y": "sales" } ] }""").Definition!;

        var razor = ChartRazor.FromDefinition(spec, "Sale", "sales");

        Assert.Contains("YAxis=\"@(new AxisOptions { Title = \"Share\", Stacking = StackMode.Percent })\"", razor);
    }
}
