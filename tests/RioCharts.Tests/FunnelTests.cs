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

    // A milder funnel, wide enough for its values inside: 15,000 → 10,000 → 7,000 → 4,000.
    private static readonly SeriesData Shop = SeriesData.FromCategories(["Visits", "Views", "Cart", "Checkout"], [15000.0, 10000, 7000, 4000]);

    private static readonly LayoutOptions Wide = Options with { Width = 800, Height = 320 };

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

    private static List<PathNode> Stages(ChartLayoutResult layout) => All<PathNode>(layout.Scene.Series).Where(r => r.Class == "rio-funnel-stage").ToList();

    private static List<string> Texts(ChartLayoutResult layout, string cls) =>
        All<TextNode>(layout.Scene.Static).Concat(All<TextNode>(layout.Scene.Series)).Where(t => t.Class == cls).Select(t => t.Text).ToList();

    [Fact]
    public void Stages_are_centered_segments_as_wide_as_their_share_of_the_largest()
    {
        var layout = Funnel(Signups);

        Assert.Equal(4, Stages(layout).Count);
        var s = layout.Tooltip.Series.Single();
        Assert.Equal(540.0 / 1200, s.CellWidths![1] / s.CellWidths[0], 2); // a segment's top edge
        Assert.All(s.X, x => Assert.Equal(s.X[0], x, 1)); // one center line, right of the name column
        Assert.True(s.X[0] > layout.Plot.X + layout.Plot.Width / 2);
        Assert.True(s.Y.Zip(s.Y.Skip(1)).All(p => p.First < p.Second)); // first stage on top
        Assert.Equal(4, Texts(layout, "rio-row-label").Count);
        Assert.Equal(4, All<PathNode>(layout.Scene.Static).Count(p => p.Class == "rio-funnel-leader"));
    }

    [Fact]
    public void Heights_follow_the_values_with_room_for_every_label()
    {
        var layout = Funnel(Shop, options: Wide);

        var heights = layout.Tooltip.Series.Single().CellHeights!;
        Assert.True(heights.Zip(heights.Skip(1)).All(p => p.First > p.Second)); // larger stages are taller
        Assert.All(heights, h => Assert.True(h >= 14, $"{h}")); // the smallest still fits its label

        var equal = Funnel(SeriesData.FromCategories(["A", "B", "C"], [5.0, 5, 5]), options: Wide).Tooltip.Series.Single().CellHeights!;
        Assert.All(equal, h => Assert.Equal(equal[0], h, 1));
    }

    [Fact]
    public void Each_stage_has_its_own_color_unless_the_series_sets_one()
    {
        var colors = Stages(Funnel(Signups)).Select(p => p.Style).Distinct().Count();
        var one = Funnel(Signups, new SeriesOptions { Id = "f", Color = "#336699" });

        Assert.Equal(4, colors);
        Assert.Single(Stages(one).Select(p => p.Style).Distinct());
        Assert.DoesNotContain(All<GroupNode>(one.Scene.Series), g => g.Key == "selection:points");
    }

    [Fact]
    public void Labels_give_the_value_and_the_share_of_the_stage_before()
    {
        var layout = Funnel(Shop, options: Wide);

        Assert.Equal(["15,000 (100%)", "10,000 (66.7%)", "7,000 (70%)", "4,000 (57.1%)"], Texts(layout, "rio-data-label"));
        Assert.Equal(["Visits", "Views", "Cart", "Checkout"], Texts(layout, "rio-row-label"));
        Assert.Empty(Texts(Funnel(Shop, new SeriesOptions { Id = "f", DataLabels = false }, Wide), "rio-data-label"));
    }

    [Fact]
    public void A_value_that_does_not_fit_goes_just_after_its_segment()
    {
        var layout = Funnel(Signups);

        var values = All<TextNode>(layout.Scene.Series).Where(t => t.Class == "rio-data-label").ToList();
        Assert.Equal(["1,200 (100%)", "540 (45%)", "180 (33.3%)", "45 (25%)"], values.Select(t => t.Text));
        var thin = values.Single(t => t.Text == "45 (25%)"); // too thin for its value
        Assert.Equal(Anchor.Start, thin.Anchor);
        Assert.True(thin.At.X > layout.Tooltip.Series.Single().X[3]);
        Assert.Equal(["Visits", "Sign-ups", "Trials", "Customers"], Texts(layout, "rio-row-label")); // names stay plain
    }

    [Fact]
    public void Tooltip_gives_the_share_of_the_first_and_of_the_previous_stage()
    {
        var s = Funnel(Signups).Tooltip.Series.Single();

        Assert.Equal(["Visits", "Sign-ups", "Trials", "Customers"], s.XLabels);
        Assert.Equal(["Of first stage", "Of previous stage"], s.PartLabels!);
        Assert.Equal(["", ""], s.PartValues![0]); // the first stage has nothing to compare with
        Assert.Equal(["45%", "45%"], s.PartValues[1]);
        Assert.Equal(["15%", "33.3%"], s.PartValues[2]);
        Assert.Equal(["3.8%", "25%"], s.PartValues[3]);
        Assert.Equal(SeriesType.Funnel, s.Type);
    }

    [Fact]
    public void Stages_keep_the_input_order_even_when_a_later_one_is_larger()
    {
        var data = SeriesData.FromCategories(["Leads", "Meetings", "Offers"], [80.0, 100, 30]);

        var s = Funnel(data).Tooltip.Series.Single();

        Assert.Equal(0.8, s.CellWidths![0] / s.CellWidths[1], 2); // the larger second stage is the widest
        Assert.Equal(["Leads", "Meetings", "Offers"], s.XLabels);
        Assert.Equal(["125%", "125%"], s.PartValues![1]);
    }

    [Fact]
    public void Gaps_negatives_and_zeros_render_cleanly()
    {
        var data = SeriesData.FromCategories(["A", "B", "C", "D"], [100.0, double.NaN, -5, 0]);

        var layout = Funnel(data);

        Assert.Equal(["A", "C", "D"], layout.Tooltip.Series.Single().XLabels); // the gap is left out
        Assert.Single(Stages(layout)); // nothing to draw for zero and below
        Assert.Contains(layout.Warnings, w => w.Contains("below zero", StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_single_and_equal_stages_do_not_throw()
    {
        var empty = Funnel(SeriesData.FromCategories([], []));
        Assert.False(empty.HasData);
        Assert.Single(All<TextNode>(empty.Scene.Overlay), t => t.Class == "rio-empty");

        Assert.Single(Stages(Funnel(SeriesData.FromCategories(["Only"], [7.0]))));
        Assert.Equal(2, Stages(Funnel(SeriesData.FromCategories(["A", "B"], [5.0, 5]))).Count);
        Assert.Empty(Stages(Funnel(SeriesData.FromCategories(["A", "B"], [0.0, 0]))));
        var tiny = Funnel(Signups, options: Options with { Width = 20, Height = 20 });
        Assert.NotEmpty(Stages(tiny)); // tiny: still lays out, without labels
        Assert.Empty(Texts(tiny, "rio-row-label"));
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

        Assert.Equal(4, Stages(layout).Count);
        Assert.Contains(layout.Warnings, w => w.Contains("series 'b': a chart shows one funnel", StringComparison.Ordinal));
        Assert.Contains(layout.Warnings, w => w.Contains("series 'c': Line series cannot be mixed with a funnel", StringComparison.Ordinal));
    }

    [Fact]
    public void Point_colors_and_selection_outlines()
    {
        var data = Signups.WithColors(["--rio-negative", null, null, null]);

        var layout = Funnel(data, options: Options with { SelectedPoints = new Dictionary<string, IReadOnlyCollection<int>> { ["f"] = [2] } });

        var stages = Stages(layout);
        Assert.Equal("--rio-negative", layout.Scene.Styles.Get(stages[0].Style).Fill);
        var outline = All<PathNode>(layout.Scene.Series).Single(r => r.Class == "rio-selected-bar");
        Assert.Equal(stages[2].Path.Coords, outline.Path.Coords); // around the stage's own shape
        Assert.Equal("none", layout.Scene.Styles.Get(outline.Style).Fill);
    }

    [Fact]
    public void Screen_readers_hear_the_stages_and_shares()
    {
        var layout = Funnel(Signups, options: Options with { Culture = CultureInfo.GetCultureInfo("sv-SE") });

        Assert.Contains("Trattdiagram", layout.Scene.Description, StringComparison.Ordinal);
        Assert.Contains("Visits 1", layout.Scene.Description, StringComparison.Ordinal);
        Assert.Contains("Customers 45 (25%)", layout.Scene.Description, StringComparison.Ordinal);
        Assert.Contains("Trials 180 (33,3%)", layout.Scene.Description, StringComparison.Ordinal); // the culture's decimal comma
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
