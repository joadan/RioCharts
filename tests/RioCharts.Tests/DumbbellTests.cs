using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class DumbbellTests
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

    private static readonly SeriesData Cities = SeriesData.FromCategories(["Utrecht", "Malmö", "Lyon"], [41.0, 33, 12]).WithLow([33.0, 30, 6]);

    private static ChartLayoutResult Layout(SeriesData data, bool horizontal = false, SeriesOptions? series = null) =>
        ChartLayout.Compute(new ChartDefinition
        {
            Horizontal = horizontal,
            Series = [(series ?? new SeriesOptions()) with { Id = "d", Name = "2016 → 2026", Type = SeriesType.Dumbbell }],
        }, new Dictionary<string, SeriesData> { ["d"] = data }, Options);

    private static PathNode Path(ChartLayoutResult layout, string cls) => All<PathNode>(layout.Scene.Series).Single(p => p.Class == cls);

    private static int Subpaths(PathNode path) => path.Path.Commands.Count(c => c == PathCommand.MoveTo);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Each_category_gets_a_line_from_a_hollow_to_a_filled_dot(bool horizontal)
    {
        var layout = Layout(Cities, horizontal);

        Assert.True(layout.HasData);
        Assert.Empty(layout.Warnings);
        Assert.Equal(3, Subpaths(Path(layout, "rio-dumbbell-line")));
        Assert.Equal(3, Subpaths(Path(layout, "rio-dumbbell-from")));
        Assert.Equal(3, Subpaths(Path(layout, "rio-dumbbell-to")));
        Assert.Equal(ThemeTokens.Surface, layout.Scene.Styles.Get(Path(layout, "rio-dumbbell-from").Style).Fill); // hollow
        Assert.Equal(["33 → 41 (+8)", "30 → 33 (+3)", "6 → 12 (+6)"], layout.Tooltip.Series[0].Values);
        Assert.Equal(["Utrecht", "Malmö", "Lyon"], layout.Tooltip.Series[0].XLabels);
    }

    [Fact]
    public void The_value_axis_fits_both_ends_without_starting_at_zero()
    {
        var data = SeriesData.FromCategories(["A", "B"], [152.0, 171]).WithLow([160.0, 160]);
        var layout = Layout(data);

        Assert.True(layout.YScale!.DomainMin > 0); // unlike bars
        Assert.True(layout.YScale.DomainMin <= 152 && layout.YScale.DomainMax >= 171);
        Assert.Equal("160 → 152 (-8)", layout.Tooltip.Series[0].Values[0]);
    }

    [Fact]
    public void A_missing_end_draws_the_other_dot_alone()
    {
        var data = SeriesData.FromCategories(["A", "B", "C"], [10.0, double.NaN, double.NaN]).WithLow([double.NaN, 7, double.NaN]);
        var layout = Layout(data);

        Assert.Empty(Path(layout, "rio-dumbbell-line").Path.Commands);
        Assert.Equal(1, Subpaths(Path(layout, "rio-dumbbell-from")));
        Assert.Equal(1, Subpaths(Path(layout, "rio-dumbbell-to")));
        Assert.Equal(["10", "7 →"], layout.Tooltip.Series[0].Values); // C has neither: no point
    }

    [Fact]
    public void Without_first_values_only_the_second_are_drawn_with_a_warning()
    {
        var layout = Layout(SeriesData.FromCategories(["A", "B"], [1.0, 2]));

        Assert.Contains(layout.Warnings, w => w.Contains("needs its first values", StringComparison.Ordinal));
        Assert.Equal(2, Subpaths(Path(layout, "rio-dumbbell-to")));
    }

    [Fact]
    public void Horizontal_dumbbells_fit_their_values_and_mix_with_bars()
    {
        var alone = Layout(Cities, horizontal: true, new SeriesOptions { DataLabels = true });
        var labels = All<TextNode>(alone.Scene.Series).Where(t => t.Class == "rio-data-label").ToList();
        Assert.Equal(6, labels.Count); // both ends of every row
        Assert.Contains(labels, l => l.Text == "33" && l.Anchor == Anchor.End); // the lower value left of its dot
        Assert.Contains(labels, l => l.Text == "41" && l.Anchor == Anchor.Start);

        var mixed = ChartLayout.Compute(new ChartDefinition
        {
            Horizontal = true,
            Series =
            [
                new SeriesOptions { Id = "b", Type = SeriesType.Bar },
                new SeriesOptions { Id = "d", Type = SeriesType.Dumbbell },
            ],
        }, new Dictionary<string, SeriesData> { ["b"] = SeriesData.FromCategories(["Utrecht", "Malmö", "Lyon"], [20.0, 25, 30]), ["d"] = Cities }, Options);
        Assert.Empty(mixed.Warnings);
        Assert.Equal(2, mixed.Tooltip.Series.Count);
        // The bar keeps the whole row (dumbbells take no slot).
        Assert.True(All<RectNode>(mixed.Scene.Series).First().Bounds.Height > 20);
    }

    [Fact]
    public void Point_colors_color_the_second_dot()
    {
        var data = Cities.WithColors([null, "--rio-negative", null]);
        var layout = Layout(data);

        var tos = All<PathNode>(layout.Scene.Series).Where(p => p.Class == "rio-dumbbell-to").ToList();
        Assert.Equal(2, tos.Count);
        Assert.Contains(tos, p => layout.Scene.Styles.Get(p.Style).Fill == "--rio-negative");
    }

    [Fact]
    public void The_legend_shows_a_small_dumbbell()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Series =
            [
                new SeriesOptions { Id = "a", Name = "North", Type = SeriesType.Dumbbell },
                new SeriesOptions { Id = "b", Name = "South", Type = SeriesType.Dumbbell },
            ],
        }, new Dictionary<string, SeriesData> { ["a"] = Cities, ["b"] = Cities }, Options);

        Assert.Equal(2, All<GroupNode>(layout.Scene.Static).Count(g => g.Class == "rio-swatch rio-swatch-dumbbell"));
    }

    [Fact]
    public void Dumbbells_are_not_stacked()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Series =
            [
                new SeriesOptions { Id = "a", Type = SeriesType.Dumbbell, Stack = "s" },
                new SeriesOptions { Id = "b", Type = SeriesType.Dumbbell, Stack = "s" },
            ],
        }, new Dictionary<string, SeriesData> { ["a"] = Cities, ["b"] = Cities }, Options);

        Assert.Equal(layout.Tooltip.Series[0].Y, layout.Tooltip.Series[1].Y); // drawn with their own values
    }

    // ---------- JSON definitions ----------

    private static Dictionary<string, ChartDataset> Trips() => new()
    {
        ["trips"] = ChartDataset.FromRows("trips", new (string City, double Then, double Now)[] { ("Utrecht", 33, 41), ("Lyon", 6, 12), ("Utrecht", 35, 43) }
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["city"] = r.City, ["then"] = r.Then, ["now"] = r.Now })),
    };

    private static DefinitionBindResult Bind(string json)
    {
        var parsed = ChartDefinitionJson.Parse(json);
        Assert.True(parsed.Success, string.Join("; ", parsed.Errors.Select(e => e.Message)));
        return ChartDefinitionBinder.Bind(parsed.Definition!, Trips());
    }

    [Fact]
    public void A_json_dumbbell_reads_from_and_y_aggregated_alike()
    {
        var bound = Bind("""{ "dataset": "trips", "series": [ { "type": "connectedDot", "x": "city", "from": "then", "y": "now", "aggregate": "average" } ] }""");
        Assert.True(bound.Success, string.Join("; ", bound.Errors.Select(e => e.Message)));
        var series = Assert.Single(bound.Definition.Series);
        Assert.Equal(SeriesType.Dumbbell, series.Type);
        var data = bound.Data[series.Id];
        Assert.Equal([42.0, 12], data.Y);
        Assert.Equal([34.0, 6], data.Low!); // the average too, not the lowest as for a range

        var missing = Bind("""{ "dataset": "trips", "series": [ { "type": "dumbbell", "x": "city", "y": "now" } ] }""");
        Assert.Contains(missing.Errors, e => e.Path == "series[0].from" && e.Message.Contains("then, now", StringComparison.Ordinal));

        var ignored = Bind("""{ "dataset": "trips", "series": [ { "type": "bar", "x": "city", "y": "now", "from": "then" } ] }""");
        Assert.Contains(ignored.Warnings, w => w.Contains("only dumbbell series", StringComparison.Ordinal));
    }

    [Fact]
    public void Razor_for_a_dumbbell_uses_from_value()
    {
        var razor = ChartRazor.FromDefinition(ChartDefinitionJson.Parse(
            """{ "dataset": "trips", "series": [ { "type": "dumbbell", "x": "city", "from": "then", "y": "now" } ] }""").Definition!);

        Assert.Contains("<DumbbellSeries XValue=\"r => r.City\"", razor, StringComparison.Ordinal);
        Assert.Contains(" FromValue=\"r => r.Then\"", razor, StringComparison.Ordinal);
        Assert.Contains(" YValue=\"r => r.Now\"", razor, StringComparison.Ordinal);
    }
}
