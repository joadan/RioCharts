using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class WaterfallTests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    // A month's result in k€: revenue, two costs, a subtotal, other income and the result.
    private static readonly string[] Steps = ["Revenue", "Cost of sales", "Gross profit", "Salaries", "Other income", "Result"];

    private static SeriesData Result() =>
        SeriesData.FromCategories(Steps, [420.0, -180, double.NaN, -150, 15, double.NaN]).WithTotals([false, false, true, false, false, true]);

    private static ChartLayoutResult Layout(SeriesData data, LayoutOptions? options = null, SeriesOptions? series = null) =>
        ChartLayout.Compute(new ChartDefinition { Series = [series ?? new SeriesOptions { Id = "w", Name = "Result", Type = SeriesType.Waterfall }] },
            new Dictionary<string, SeriesData> { ["w"] = data }, options ?? Options);

    private static List<RectNode> Bars(ChartLayoutResult layout) =>
        All<RectNode>(layout.Scene.Series).Where(n => n.Class?.StartsWith("rio-waterfall", StringComparison.Ordinal) == true).ToList();

    [Fact]
    public void Totals_take_the_running_total()
    {
        var data = Result();

        Assert.Equal([420.0, -180, 240, -150, 15, 105], data.Y);
        Assert.Equal([false, false, true, false, false, true], data.Totals!);
        Assert.Null(SeriesData.FromCategories(["a"], [1.0]).WithTotals([false]).Totals); // no totals: nothing to keep
    }

    [Fact]
    public void Changes_float_from_the_running_total_and_totals_start_at_zero()
    {
        var layout = Layout(Result());
        var bars = Bars(layout);
        var y = layout.YScale;

        Assert.Equal(6, bars.Count);
        Assert.Equal(y.Map(420), bars[1].Bounds.Y, 1); // cost of sales: from 420 down to 240
        Assert.Equal(y.Map(240), bars[1].Bounds.Bottom, 1);
        Assert.Equal(y.Map(240), bars[2].Bounds.Y, 1); // gross profit: from zero to 240
        Assert.Equal(y.Map(0), bars[2].Bounds.Bottom, 1);
        Assert.Equal(y.Map(105), bars[5].Bounds.Y, 1);
        Assert.Equal(["rio-waterfall rio-waterfall-total", "rio-waterfall rio-waterfall-total"], bars.Where((_, i) => i is 2 or 5).Select(b => b.Class));
        Assert.True(layout.YScale.DomainMin <= 0 && layout.YScale.DomainMax >= 420);
        Assert.Empty(layout.Warnings);
    }

    [Fact]
    public void Rises_are_positive_falls_negative_and_totals_in_the_series_color()
    {
        var layout = Layout(Result());

        var fills = Bars(layout).Select(b => layout.Scene.Styles.Get(b.Style).Fill).ToList();
        Assert.Equal([ThemeTokens.Positive, ThemeTokens.Negative, ThemeTokens.Series(0), ThemeTokens.Negative, ThemeTokens.Positive, ThemeTokens.Series(0)], fills);
        Assert.Equal(fills, layout.Tooltip.Series.Single().PointColors!); // the tooltip dot as the bar
    }

    [Fact]
    public void Own_colors_win()
    {
        var layout = Layout(Result().WithColors([null, "#888888", null, null, null, null]));

        Assert.Equal("#888888", layout.Scene.Styles.Get(Bars(layout)[1].Style).Fill);
    }

    [Fact]
    public void Connectors_carry_each_end_to_the_next_bar()
    {
        var layout = Layout(Result());
        var bars = Bars(layout);

        var connectors = All<PathNode>(layout.Scene.Series).Single(n => n.Class == "rio-connectors");
        Assert.Equal(5, connectors.Path.Commands.Count(c => c == PathCommand.MoveTo));
        Assert.Equal(bars[0].Bounds.Right, connectors.Path.Coords[0], 1); // from revenue's end ...
        Assert.Equal(bars[1].Bounds.X, connectors.Path.Coords[2], 1); // ... to cost of sales
        Assert.Equal(bars[0].Bounds.Y, connectors.Path.Coords[1], 1);
    }

    [Fact]
    public void Tooltip_shows_the_change_and_the_running_total()
    {
        var s = Layout(Result()).Tooltip.Series.Single();

        Assert.Equal(["+420", "-180", "240", "-150", "+15", "105"], s.Values.Select(v => v.Replace('−', '-')));
        Assert.Equal(["Total"], s.PartLabels!);
        Assert.Equal(["240"], s.PartValues![1]);
        Assert.Equal([""], s.PartValues[2]); // a total says it once
    }

    [Fact]
    public void Data_labels_sit_above_rises_and_below_falls()
    {
        var layout = Layout(Result(), series: new SeriesOptions { Id = "w", Type = SeriesType.Waterfall, DataLabels = true });
        var bars = Bars(layout);

        var labels = new[] { layout.Scene.Static, layout.Scene.Series, layout.Scene.Overlay }.SelectMany(All<GroupNode>)
            .Where(g => g.Class == "rio-data-labels").SelectMany(g => g.Children.OfType<TextNode>()).ToList();
        Assert.Equal(6, labels.Count);
        Assert.True(labels[0].At.Y < bars[0].Bounds.Y); // revenue: above
        Assert.True(labels[1].At.Y > bars[1].Bounds.Bottom); // cost of sales: below
        Assert.Equal("240", labels[2].Text);
    }

    [Fact]
    public void Gaps_change_nothing_and_odd_data_renders_cleanly()
    {
        var gap = Layout(SeriesData.FromCategories(["a", "b", "c"], [10.0, double.NaN, -4]));
        Assert.Equal(2, Bars(gap).Count);
        Assert.Equal(gap.YScale.Map(10), Bars(gap)[1].Bounds.Y, 1); // c falls from 10

        foreach (var data in new[]
        {
            SeriesData.Empty,
            SeriesData.FromCategories(["a"], [5.0]),
            SeriesData.FromCategories(["a", "b"], [0.0, 0]),
            SeriesData.FromCategories(["a", "b"], [double.NaN, double.NaN]).WithTotals([true, true]),
            SeriesData.FromCategories(["a", "b"], [-5.0, double.NaN]).WithTotals([false, true]),
        })
        {
            var layout = Layout(data);
            Assert.NotNull(layout.Scene);
        }
    }

    [Fact]
    public void A_waterfall_is_not_stacked()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Y = new AxisOptions { Stacking = StackMode.Percent },
            Series = [new SeriesOptions { Id = "w", Type = SeriesType.Waterfall }],
        }, new Dictionary<string, SeriesData> { ["w"] = Result() }, Options);

        Assert.Contains(layout.Warnings, w => w.Contains("percent axis", StringComparison.Ordinal));
        Assert.Equal(layout.YScale.Map(105), Bars(layout)[5].Bounds.Y, 1);
    }

    [Fact]
    public void A_selected_bar_is_outlined_from_start_to_end()
    {
        var layout = Layout(Result(), Options with { SelectedPoints = new Dictionary<string, IReadOnlyCollection<int>> { ["w"] = [1] } });

        var outline = All<RectNode>(layout.Scene.Series).Single(n => n.Class == "rio-selected-bar");
        Assert.Equal(layout.YScale.Map(420) - 1.5, outline.Bounds.Y, 1);
        Assert.Equal(layout.YScale.Map(240) + 1.5, outline.Bounds.Bottom, 1);
    }

    [Fact]
    public void The_legend_shows_a_rise_and_a_fall_and_describes_the_running_total()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Legend = new LegendOptions { Mode = LegendMode.Show },
            Series = [new SeriesOptions { Id = "w", Name = "Result", Type = SeriesType.Waterfall }],
        }, new Dictionary<string, SeriesData> { ["w"] = Result() }, Options);

        Assert.Single(All<GroupNode>(layout.Scene.Static), g => g.Class == "rio-swatch rio-swatch-waterfall");
        Assert.Equal("Result: 0 to 420, last 105", layout.Legend.Single().Tooltip);
    }

    // ---------- JSON definitions ----------

    private static Dictionary<string, ChartDataset> Rows() => new()
    {
        ["d"] = ChartDataset.FromRows("d", Steps.Select((step, i) => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["step"] = step,
            ["amount"] = i is 2 or 5 ? null : new[] { 420.0, -180, 0, -150, 15, 0 }[i],
            ["isTotal"] = i is 2 or 5,
        })),
    };

    [Fact]
    public void A_json_waterfall_reads_its_totals()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "bridge", "x": "step", "y": "amount", "total": "isTotal" } ] }""");

        Assert.Empty(parsed.Errors);
        Assert.Equal(SeriesType.Waterfall, parsed.Definition!.Series[0].Type);
        var bound = ChartDefinitionBinder.Bind(parsed.Definition, Rows());

        Assert.Empty(bound.Errors);
        var data = bound.Data.Values.Single();
        Assert.Equal([false, false, true, false, false, true], data.Totals!);
        Assert.Equal(105, data.Y[5]);
        var razor = ChartRazor.FromDefinition(parsed.Definition, "Row", "rows");
        Assert.Contains("<WaterfallSeries", razor, StringComparison.Ordinal);
        Assert.Contains(" IsTotal=\"r => r.IsTotal\"", razor, StringComparison.Ordinal);
    }

    [Fact]
    public void A_total_field_on_another_type_is_ignored_with_a_warning()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "bar", "x": "step", "y": "amount", "total": "isTotal" } ] }""");

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, Rows());

        Assert.Contains(bound.Warnings, w => w.Contains("only waterfall series have totals", StringComparison.Ordinal));
        Assert.Null(bound.Data.Values.Single().Totals);
    }
}
