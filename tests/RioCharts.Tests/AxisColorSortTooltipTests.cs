using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

/// <summary>Axis colors (Color, SeriesColor), categories sorted by a series' values, and the tooltip's x format.</summary>
public class AxisColorSortTooltipTests
{
    private static readonly LayoutOptions Options = new() { Width = 640, Height = 320, Culture = CultureInfo.InvariantCulture };

    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static List<TextNode> AxisLabels(ChartLayoutResult r, string id) =>
        All(r.Scene.Static).OfType<GroupNode>().Single(g => g.Key == "axis:" + id).Children.OfType<TextNode>().ToList();

    private static TextNode Title(ChartLayoutResult r, string text) =>
        All(r.Scene.Static).OfType<TextNode>().Single(t => t.Class == "rio-axis-title" && t.Text == text);

    private static string? Fill(ChartLayoutResult r, TextStyle style) => r.Scene.Styles.Text[style.Name].Fill;

    // ---------- Axis colors ----------

    [Fact]
    public void An_axis_takes_its_color_or_its_series_color()
    {
        var spec = new ChartDefinition
        {
            Y = new AxisOptions { Title = "Temperature", SeriesColor = true },
            YAxes = [new AxisOptions { Id = "kw", Title = "Power", Color = "#d9480f" }, new AxisOptions { Id = "plain", Title = "Plain" }],
            Series =
            [
                new SeriesOptions { Id = "t" },
                new SeriesOptions { Id = "p", YAxisId = "kw" },
                new SeriesOptions { Id = "q", YAxisId = "plain" },
            ],
        };
        var data = new Dictionary<string, SeriesData>
        {
            ["t"] = SeriesData.From([0.0, 1, 2], [20.0, 21, 22]),
            ["p"] = SeriesData.From([0.0, 1, 2], [500.0, 800, 900]),
            ["q"] = SeriesData.From([0.0, 1, 2], [5.0, 8, 9]),
        };

        var result = ChartLayout.Compute(spec, data, Options);

        var seriesColor = result.Legend.Single(l => l.SeriesId == "t").Color;
        Assert.All(AxisLabels(result, "y"), t => Assert.Equal(seriesColor, Fill(result, t.Style)));
        Assert.Equal(seriesColor, Fill(result, Title(result, "Temperature").Style));
        Assert.All(AxisLabels(result, "kw"), t => Assert.Equal("#d9480f", Fill(result, t.Style)));
        Assert.Equal("#d9480f", Fill(result, Title(result, "Power").Style));
        Assert.All(AxisLabels(result, "plain"), t => Assert.Equal("axis-label", t.Style.Name));
        Assert.Equal("axis-title", Title(result, "Plain").Style.Name);
    }

    // ---------- Sort ----------

    private static List<string> CategoryOrder(ChartLayoutResult r) =>
        All(r.Scene.Static).OfType<GroupNode>().Single(g => g.Class == "rio-axis-x").Children.OfType<TextNode>().Select(t => t.Text).ToList();

    [Theory]
    [InlineData(SortOrder.Descending, new[] { "Spain", "France", "Sweden" })]
    [InlineData(SortOrder.Ascending, new[] { "Sweden", "France", "Spain" })]
    public void Sort_orders_the_categories_by_the_series_values(SortOrder order, string[] expected)
    {
        var spec = new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "issues", Type = SeriesType.Bar, Sort = order }, new SeriesOptions { Id = "other", Type = SeriesType.Bar }],
        };
        var data = new Dictionary<string, SeriesData>
        {
            // As a Blazor series gives them after Aggregate.Count: in the order of the items, not sorted.
            ["issues"] = SeriesData.FromCategories(["Sweden", "Spain", "France"], [2.0, 9, 5]),
            ["other"] = SeriesData.FromCategories(["France", "Sweden", "Spain"], [1.0, 1, 1]),
        };

        var result = ChartLayout.Compute(spec, data, Options);

        Assert.Equal(expected, CategoryOrder(result));
    }

    [Fact]
    public void Categories_without_a_value_in_the_sorted_series_come_last()
    {
        var spec = new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "a", Type = SeriesType.Bar, Sort = SortOrder.Descending }, new SeriesOptions { Id = "b", Type = SeriesType.Bar }],
        };
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(["North", "South"], [1.0, 4]),
            ["b"] = SeriesData.FromCategories(["West", "North", "South"], [3.0, 1, 1]),
        };

        Assert.Equal(["South", "North", "West"], CategoryOrder(ChartLayout.Compute(spec, data, Options)));
    }

    [Fact]
    public void Sort_leaves_horizontal_rows_in_value_order_too()
    {
        var spec = new ChartDefinition
        {
            Horizontal = true,
            Series = [new SeriesOptions { Id = "a", Type = SeriesType.Bar, Sort = SortOrder.Descending }],
        };
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.FromCategories(["Small", "Large", "Medium"], [1.0, 9, 5]) };

        var result = ChartLayout.Compute(spec, data, Options);

        var rows = All(result.Scene.Static).OfType<GroupNode>().Single(g => g.Class == "rio-axis-y").Children.OfType<TextNode>()
            .OrderBy(t => t.At.Y).Select(t => t.Text);
        Assert.Equal(["Large", "Medium", "Small"], rows);
    }

    // ---------- Tooltip format ----------

    [Fact]
    public void TooltipFormat_sets_the_tooltip_header_without_the_offset_and_leaves_the_axis_alone()
    {
        var times = new[] { new DateTime(2026, 11, 17, 6, 47, 37, DateTimeKind.Utc), new DateTime(2026, 11, 17, 7, 12, 5, DateTimeKind.Utc) };
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From(times, [1.0, 2]) };
        var spec = new ChartDefinition { TimeZone = "Asia/Bangkok", Series = [new SeriesOptions { Id = "a" }] };
        var viewer = Options with { ViewerTimeZone = "Europe/Stockholm" };

        var plain = ChartLayout.Compute(spec, data, viewer);
        var formatted = ChartLayout.Compute(spec with { X = new AxisOptions { TooltipFormat = "d MMM HH:mm" } }, data, viewer);

        Assert.Equal("11/17/2026 13:47:37 UTC+07:00", plain.Tooltip.Series[0].XLabels[0]);
        Assert.Equal("17 Nov 13:47", formatted.Tooltip.Series[0].XLabels[0]);
        Assert.Equal(CategoryOrder(plain), CategoryOrder(formatted)); // the x axis labels do not change
    }

    // ---------- JSON and Razor ----------

    [Fact]
    public void Json_and_razor_carry_the_new_settings()
    {
        var spec = ChartDefinitionJson.Parse("""
            { "x": { "tooltipFormat": "HH:mm" },
              "y": { "title": "Issues", "seriesColor": true },
              "yAxes": [ { "id": "kw", "title": "kW", "color": "#d9480f" } ],
              "series": [ { "type": "bar", "x": "country", "aggregate": "count", "sort": "descending" } ] }
            """).Definition!;

        Assert.Equal("HH:mm", spec.X.TooltipFormat);
        Assert.True(spec.Y.SeriesColor);
        Assert.Equal("#d9480f", spec.YAxes[0].Color);

        var razor = ChartRazor.FromDefinition(spec, "Row", "rows");
        Assert.Contains("XAxis=\"@(new AxisOptions { TooltipFormat = \"HH:mm\" })\"", razor, StringComparison.Ordinal);
        Assert.Contains("SeriesColor = true", razor, StringComparison.Ordinal);
        Assert.Contains("Color = \"#d9480f\"", razor, StringComparison.Ordinal);
        Assert.Contains("Sort=\"SortOrder.Descending\"", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("sorted descending", razor, StringComparison.Ordinal);
    }
}
