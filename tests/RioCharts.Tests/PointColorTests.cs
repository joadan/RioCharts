using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Specs;

namespace RioCharts.Tests;

/// <summary>Per-point colors (SeriesData.Colors, ColorValue on the components, "colorField" in specs).</summary>
public class PointColorTests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };
    private static readonly string[] Days = ["Mon", "Tue", "Wed", "Thu", "Fri"];
    private const string Late = "--rio-negative";

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    private static ChartLayoutResult Chart(SeriesType type, SeriesData data) =>
        ChartLayout.Compute(new ChartSpec { Series = [new SeriesSpec { Id = "s", Type = type }] }, new Dictionary<string, SeriesData> { ["s"] = data }, Options);

    // Orders per day; Thursday is over the limit.
    private static SeriesData Orders() => SeriesData.FromCategories(Days, [80.0, 95, 70, 130, 90]).WithColors([null, null, null, Late, null]);

    [Fact]
    public void Bars_take_their_own_color_and_the_rest_keep_the_series_color()
    {
        var result = Chart(SeriesType.Bar, Orders());

        var fills = All<RectNode>(result.Scene.Series).Where(r => r.Class == "rio-bar").Select(r => result.Scene.Styles.Get(r.Style).Fill).ToList();
        Assert.Equal([ThemeTokens.Series(0), ThemeTokens.Series(0), ThemeTokens.Series(0), Late, ThemeTokens.Series(0)], fills);
        Assert.Equal(Late, result.Tooltip.Series[0].PointColors![3]); // the tooltip dot too
        Assert.Equal(ThemeTokens.Series(0), result.Tooltip.Series[0].PointColors![0]);
    }

    [Fact]
    public void Horizontal_bars_scatter_and_lines_use_it()
    {
        var horizontal = ChartLayout.Compute(new ChartSpec { Horizontal = true, Series = [new SeriesSpec { Id = "s", Type = SeriesType.Bar }] },
            new Dictionary<string, SeriesData> { ["s"] = Orders() }, Options);
        var scatter = Chart(SeriesType.Scatter, SeriesData.FromColumns([1.0, 2, 3, 4], [5.0, 6, 7, 8], XKind.Numeric).WithColors([null, "#d6485a", null, "#d6485a"]));
        var line = Chart(SeriesType.Line, Orders());

        Assert.Contains(All<RectNode>(horizontal.Scene.Series), r => horizontal.Scene.Styles.Get(r.Style).Fill == Late);
        Assert.Equal(Late, horizontal.Tooltip.Series[0].PointColors![3]);
        var markers = All<PathNode>(scatter.Scene.Series).Where(p => p.Class == "rio-marker").Select(p => scatter.Scene.Styles.Get(p.Style).Fill).ToList();
        Assert.Equal([ThemeTokens.Series(0), "#d6485a"], markers); // one path per color
        var dots = All<PathNode>(line.Scene.Series).Where(p => p.Class == "rio-point rio-point-colored").ToList();
        Assert.Equal(Late, line.Scene.Styles.Get(Assert.Single(dots).Style).Fill); // the line keeps its color, Thursday gets a dot
    }

    [Fact]
    public void Slices_radial_bars_and_their_legend_take_it()
    {
        var pie = Chart(SeriesType.Pie, SeriesData.FromCategories(["Shoes", "Bags", "Other"], [50.0, 30, 20]).WithColors([null, null, "#9ca3af"]));
        var radial = Chart(SeriesType.RadialBar, Orders());

        Assert.Equal("#9ca3af", pie.Legend.Single(l => l.SeriesId == "Other").Color);
        Assert.Equal("#9ca3af", pie.Tooltip.Slices.Single(s => s.Label == "Other").Color);
        Assert.Equal(ThemeTokens.Series(0), pie.Legend[0].Color);
        Assert.Equal(Late, radial.Tooltip.Slices.Single(s => s.Label == "Thu").Color);
    }

    [Fact]
    public void Treemap_items_take_it_and_groups_keep_theirs()
    {
        var data = SeriesData.FromCategories(["Asia/India", "Asia/China", "Europe/Sweden"], [1430.0, 1410, 600]).WithColors([null, null, "#2f9e6e"]);

        var result = Chart(SeriesType.Treemap, data);

        var tip = result.Tooltip.Series[0];
        Assert.Equal("#2f9e6e", tip.PointColors![Array.IndexOf(tip.XLabels, "Europe › Sweden")]);
        Assert.Equal(ThemeTokens.Series(1), tip.PointColors[Array.IndexOf(tip.XLabels, "Europe")]); // the group keeps its color
    }

    [Fact]
    public void Colors_follow_their_points_when_the_data_is_sorted_or_extended()
    {
        // Times out of order are sorted on creation: colors are given in input order and follow.
        var t = new DateTime(2026, 11, 20);
        var data = SeriesData.FromItems([(t.AddHours(2), 3.0), (t, 1.0), (t.AddHours(1), 2.0)], p => p.Item1, p => p.Item2)
            .WithColors(["red", null, "blue"])
            .WithLow([0.0, 0, 0]);

        Assert.Equal(["", "blue", "red"], data.Colors!.Select(c => c ?? ""));
        Assert.Equal("red", data.ColorAt(2, "x"));
        Assert.Equal("x", data.ColorAt(0, "x"));
        Assert.Null(SeriesData.FromCategories(Days, [1.0, 2, 3, 4, 5]).WithColors([null, " ", null]).Colors); // none given: no column
    }

    [Fact]
    public void A_json_colorField_colors_points_and_groups_take_the_first_row()
    {
        var rows = new[] { ("Mon", 80.0, ""), ("Thu", 70.0, Late), ("Thu", 60.0, "#000"), ("Fri", 90.0, "") }
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["day"] = r.Item1, ["orders"] = r.Item2, ["status"] = r.Item3 });
        var parsed = ChartSpecJson.Parse("""{ "dataset": "d", "series": [ { "type": "bar", "x": "day", "y": "orders", "aggregate": "sum", "colorField": "status" } ] }""");

        var bound = ChartSpecBinder.Bind(parsed.Spec!, new Dictionary<string, ChartDataset> { ["d"] = ChartDataset.FromRows("d", rows) });

        Assert.Empty(bound.Errors);
        Assert.Equal(["", Late, ""], bound.Data.Values.Single().Colors!.Select(c => c ?? ""));
        Assert.Contains("ColorValue=\"r => r.Status\"", ChartRazor.FromSpec(parsed.Spec!, "Row", "rows"), StringComparison.Ordinal);
    }
}
