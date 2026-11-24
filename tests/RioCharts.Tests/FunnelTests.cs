using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class FunnelTests
{
    private static readonly LayoutOptions Options = new() { Width = 500, Height = 260, Culture = CultureInfo.InvariantCulture };

    private static readonly SeriesData Signups = SeriesData.FromCategories(["Visits", "Sign-ups", "Trials", "Customers"], [1200.0, 540, 180, 45]);

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    private static ChartLayoutResult Funnel(SeriesData data, SeriesOptions? spec = null, LayoutOptions? options = null) =>
        ChartLayout.Compute(new ChartDefinition { Series = [(spec ?? new SeriesOptions { Id = "f" }) with { Type = SeriesType.Funnel }] },
            new Dictionary<string, SeriesData> { [(spec?.Id ?? "f")] = data }, options ?? Options);

    private static List<RectNode> Bars(ChartLayoutResult layout) => All<RectNode>(layout.Scene.Series).Where(r => r.Class == "rio-funnel-bar").ToList();

    private static List<string> Texts(ChartLayoutResult layout, string cls) =>
        All<TextNode>(layout.Scene.Static).Concat(All<TextNode>(layout.Scene.Series)).Where(t => t.Class == cls).Select(t => t.Text).ToList();

    [Fact]
    public void Stages_are_centered_bars_as_wide_as_their_share_of_the_largest()
    {
        var layout = Funnel(Signups);

        var bars = Bars(layout);
        Assert.Equal(4, bars.Count);
        Assert.Equal(layout.Plot.Width, bars[0].Bounds.Width, 1);
        Assert.Equal(layout.Plot.Width * 540 / 1200, bars[1].Bounds.Width, 1);
        Assert.All(bars, b => Assert.Equal(layout.Plot.X + layout.Plot.Width / 2, b.Bounds.X + b.Bounds.Width / 2, 1));
        Assert.True(bars.Zip(bars.Skip(1)).All(p => p.First.Bounds.Y < p.Second.Bounds.Y)); // first stage on top
        Assert.Equal(["Visits", "Sign-ups", "Trials", "Customers"], Texts(layout, "rio-row-label"));
    }

    [Fact]
    public void Bands_join_each_bar_to_the_next()
    {
        var layout = Funnel(Signups);

        Assert.Equal(3, All<PathNode>(layout.Scene.Series).Count(p => p.Class == "rio-funnel-neck"));
        Assert.DoesNotContain(All<GroupNode>(layout.Scene.Series), g => g.Key == "selection:points");
    }

    [Fact]
    public void Labels_give_the_value_and_the_share_of_the_first_stage()
    {
        var labels = Texts(Funnel(Signups), "rio-data-label");

        Assert.Equal(["1,200", "540 (45%)", "180 (15%)", "45 (4%)"], labels);
        Assert.Empty(Texts(Funnel(Signups, new SeriesOptions { Id = "f", DataLabels = false }), "rio-data-label"));
    }

    [Fact]
    public void Tooltip_adds_the_share_of_the_stage_before()
    {
        var s = Funnel(Signups).Tooltip.Series.Single();

        Assert.Equal(["Visits", "Sign-ups", "Trials", "Customers"], s.XLabels);
        Assert.Equal(["Of first stage", "Of previous stage"], s.PartLabels!);
        Assert.Equal(["", ""], s.PartValues![0]); // the first stage has nothing to compare with
        Assert.Equal(["45%", "45%"], s.PartValues[1]);
        Assert.Equal(["15%", "33%"], s.PartValues[2]);
        Assert.Equal(["4%", "25%"], s.PartValues[3]);
        Assert.Equal(SeriesType.Funnel, s.Type);
    }

    [Fact]
    public void Stages_keep_the_input_order_even_when_a_later_one_is_larger()
    {
        var data = SeriesData.FromCategories(["Leads", "Meetings", "Offers"], [80.0, 100, 30]);

        var layout = Funnel(data);

        var bars = Bars(layout);
        Assert.Equal(layout.Plot.Width, bars[1].Bounds.Width, 1); // the largest is full width
        Assert.Equal(["Leads", "Meetings", "Offers"], layout.Tooltip.Series.Single().XLabels);
        Assert.Equal(["125%", "125%"], layout.Tooltip.Series.Single().PartValues![1]);
    }

    [Fact]
    public void Gaps_negatives_and_zeros_render_cleanly()
    {
        var data = SeriesData.FromCategories(["A", "B", "C", "D"], [100.0, double.NaN, -5, 0]);

        var layout = Funnel(data);

        Assert.Equal(["A", "C", "D"], layout.Tooltip.Series.Single().XLabels); // the gap is left out
        Assert.Single(Bars(layout)); // nothing to draw for zero and below
        Assert.Contains(layout.Warnings, w => w.Contains("below zero", StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_single_and_equal_stages_do_not_throw()
    {
        var empty = Funnel(SeriesData.FromCategories([], []));
        Assert.False(empty.HasData);
        Assert.Single(All<TextNode>(empty.Scene.Overlay), t => t.Class == "rio-empty");

        Assert.Single(Bars(Funnel(SeriesData.FromCategories(["Only"], [7.0]))));
        Assert.All(Bars(Funnel(SeriesData.FromCategories(["A", "B"], [5.0, 5]))), b => Assert.True(b.Bounds.Width > 100));
        Assert.Empty(Bars(Funnel(SeriesData.FromCategories(["A", "B"], [0.0, 0]))));
        Assert.NotEmpty(Bars(Funnel(Signups, options: Options with { Width = 20, Height = 20 }))); // tiny: still lays out
    }

    [Fact]
    public void One_funnel_per_chart_and_no_mixing()
    {
        var spec = new ChartDefinition
        {
            Series =
            [
                new SeriesOptions { Id = "a", Type = SeriesType.Funnel },
                new SeriesOptions { Id = "b", Type = SeriesType.Funnel },
                new SeriesOptions { Id = "c", Type = SeriesType.Line },
            ],
        };
        var data = new Dictionary<string, SeriesData> { ["a"] = Signups, ["b"] = Signups, ["c"] = Signups };

        var layout = ChartLayout.Compute(spec, data, Options);

        Assert.Equal(4, Bars(layout).Count);
        Assert.Contains(layout.Warnings, w => w.Contains("series 'b': a chart shows one funnel", StringComparison.Ordinal));
        Assert.Contains(layout.Warnings, w => w.Contains("series 'c': Line series cannot be mixed with a funnel", StringComparison.Ordinal));
    }

    [Fact]
    public void Point_colors_and_selection_outlines()
    {
        var data = Signups.WithColors(["--rio-series-2", null, null, null]);

        var layout = Funnel(data, options: Options with { SelectedPoints = new Dictionary<string, IReadOnlyCollection<int>> { ["f"] = [2] } });

        var bars = Bars(layout);
        Assert.Equal("--rio-series-2", layout.Scene.Styles.Get(bars[0].Style).Fill);
        var outline = All<RectNode>(layout.Scene.Series).Single(r => r.Class == "rio-selected-bar");
        Assert.Equal(bars[2].Bounds.X - 1.5, outline.Bounds.X, 1);
        Assert.Equal(bars[2].Bounds.Right + 1.5, outline.Bounds.Right, 1);
    }

    [Fact]
    public void Screen_readers_hear_the_stages_and_shares()
    {
        var layout = Funnel(Signups, options: Options with { Culture = CultureInfo.GetCultureInfo("sv-SE") });

        Assert.Contains("Trattdiagram", layout.Scene.Description, StringComparison.Ordinal);
        Assert.Contains("Visits 1", layout.Scene.Description, StringComparison.Ordinal);
        Assert.Contains("Customers 45 (4%)", layout.Scene.Description, StringComparison.Ordinal);
        Assert.Equal("Av föregående steg", layout.Tooltip.Series.Single().PartLabels![1]);
    }

    // ---------- JSON definitions and Razor ----------

    private static Dictionary<string, ChartDataset> Steps() => new()
    {
        ["steps"] = ChartDataset.FromRows("steps", new (string Step, double Users)[] { ("Visit", 700), ("Visit", 500), ("Cart", 300), ("Checkout", 120), ("Cart", 60) }
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["step"] = r.Step, ["users"] = r.Users })),
    };

    [Fact]
    public void A_json_funnel_sums_rows_per_stage_in_first_appearance_order()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "steps", "series": [ { "type": "conversionFunnel", "x": "step", "y": "users", "aggregate": "sum" } ] }""");
        Assert.True(parsed.Success, string.Join("; ", parsed.Errors.Select(e => e.Message)));
        Assert.Equal(SeriesType.Funnel, parsed.Definition!.Series[0].Type);

        var bound = ChartDefinitionBinder.Bind(parsed.Definition, Steps());

        Assert.True(bound.Success, string.Join("; ", bound.Errors.Select(e => e.Message)));
        var layout = ChartLayout.Compute(bound.Definition, bound.Data, Options);
        Assert.Equal(["Visit", "Cart", "Checkout"], layout.Tooltip.Series.Single().XLabels);
        Assert.Equal(["1,200", "360", "120"], layout.Tooltip.Series.Single().Values);
    }

    [Fact]
    public void Razor_for_a_funnel_is_a_funnel_series()
    {
        var razor = ChartRazor.FromDefinition(ChartDefinitionJson.Parse(
            """{ "dataset": "steps", "series": [ { "type": "funnel", "x": "step", "y": "users", "aggregate": "sum" } ] }""").Definition!);

        Assert.Contains("<FunnelSeries XValue=\"r => r.Step\" YValue=\"r => r.Users\" Aggregate=\"Aggregate.Sum\"", razor, StringComparison.Ordinal);
    }
}
