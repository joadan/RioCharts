using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Definitions;

namespace RioCharts.Tests;

public class HorizontalBarTests
{
    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static IEnumerable<Node> All(Scene scene) => All(scene.Static).Concat(All(scene.Series)).Concat(All(scene.Overlay));

    private static readonly LayoutOptions Options = new() { Width = 640, Height = 320, Culture = CultureInfo.InvariantCulture };

    private static readonly string[] Products = ["Trail shoe", "Rain jacket", "Headlamp"];

    private static ChartDefinition Spec(params SeriesOptions[] series) => new()
    {
        Horizontal = true,
        Series = series.Length > 0 ? series : [new SeriesOptions { Id = "r", Name = "Revenue", Type = SeriesType.Bar }],
    };

    private static ChartLayoutResult Layout(ChartDefinition spec, Dictionary<string, SeriesData> data, LayoutOptions? options = null) =>
        ChartLayout.Compute(spec, data, options ?? Options);

    private static Dictionary<string, SeriesData> Revenue(params double[] values) => new() { ["r"] = SeriesData.FromCategories(Products, values) };

    private static List<RectNode> Bars(ChartLayoutResult layout, string id = "r") =>
        All(layout.Scene.Series).OfType<GroupNode>().Single(g => g.Key == "series:" + id).Children.OfType<RectNode>().ToList();

    [Fact]
    public void Categories_run_down_the_side_first_on_top_and_bars_grow_to_the_right()
    {
        var layout = Layout(Spec(), Revenue(120, 90, 60));

        var labels = All(layout.Scene.Static).OfType<TextNode>().Where(t => t.Class == "rio-row-label").ToList();
        Assert.Equal(Products, labels.Select(l => l.Text));
        Assert.True(labels[0].At.Y < labels[1].At.Y);
        var bars = Bars(layout);
        Assert.All(bars, b => Assert.Equal(layout.Plot.X, b.Bounds.X, 0.5)); // from zero at the left edge
        Assert.Equal(2.0, bars[0].Bounds.Width / bars[2].Bounds.Width, 0.02);  // 120 against 60
        Assert.False(layout.Tooltip.Shared);
    }

    [Fact]
    public void Several_series_are_grouped_in_each_row()
    {
        var spec = Spec(new SeriesOptions { Id = "a", Type = SeriesType.Bar }, new SeriesOptions { Id = "b", Type = SeriesType.Bar });
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.FromCategories(Products, [1, 2, 3]), ["b"] = SeriesData.FromCategories(Products, [3, 2, 1]) };

        var layout = Layout(spec, data);

        var (a, b) = (Bars(layout, "a")[0], Bars(layout, "b")[0]);
        Assert.True(b.Bounds.Y > a.Bounds.Y + a.Bounds.Height - 0.5); // b below a, in the same row
        Assert.Contains(All(layout.Scene.Static), n => n.Class == "rio-legend");
    }

    [Fact]
    public void Stacked_segments_continue_where_the_last_ended_and_negatives_go_left()
    {
        var spec = Spec(new SeriesOptions { Id = "a", Type = SeriesType.Bar, Stack = "s" }, new SeriesOptions { Id = "b", Type = SeriesType.Bar, Stack = "s" });
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.FromCategories(Products, [10, 20, 30]), ["b"] = SeriesData.FromCategories(Products, [5, -5, 5]) };

        var layout = Layout(spec, data);

        var (a, b) = (Bars(layout, "a"), Bars(layout, "b"));
        Assert.Equal(a[0].Bounds.X + a[0].Bounds.Width, b[0].Bounds.X, 0.5); // continues
        Assert.Equal(a[0].Bounds.Y, b[0].Bounds.Y, 0.01);                      // same row position
        var zero = All(layout.Scene.Static).OfType<PathNode>().Single(n => n.Class == "rio-baseline").Path.Coords[0];
        Assert.True(b[1].Bounds.X + b[1].Bounds.Width <= zero + 0.5);          // -5 left of zero
    }

    [Fact]
    public void Values_sit_at_the_bar_ends()
    {
        var spec = Spec(new SeriesOptions { Id = "r", Type = SeriesType.Bar, DataLabels = true, Format = "0 k€" });

        var layout = Layout(spec, Revenue(120, 90, 60));

        var labels = All(layout.Scene.Series).OfType<TextNode>().Where(t => t.Class == "rio-data-label").ToList();
        Assert.Equal(["120 k€", "90 k€", "60 k€"], labels.Select(l => l.Text));
        var bars = Bars(layout);
        Assert.Equal(bars[1].Bounds.X + bars[1].Bounds.Width + 4, labels[1].At.X, 0.5);
        Assert.Equal(Anchor.Start, labels[1].Anchor);
    }

    [Fact]
    public void Long_names_are_shortened_to_leave_room_for_the_bars()
    {
        var names = new[] { "A very long product name that would take most of the chart width", "Short" };
        var layout = Layout(Spec(), new() { ["r"] = SeriesData.FromCategories(names, [1, 2]) }, Options with { Width = 400 });

        var label = All(layout.Scene.Static).OfType<TextNode>().First(t => t.Class == "rio-row-label");
        Assert.EndsWith("…", label.Text);
        Assert.True(layout.Plot.Width > 400 * 0.45);
        Assert.Equal(names[0], layout.Tooltip.Series[0].XLabels[0]); // the tooltip has the full name
    }

    [Fact]
    public void Value_lines_are_drawn_across_the_rows_with_their_label_above()
    {
        var spec = Spec() with
        {
            Annotations =
            [
                new Annotation { Value = 100, Label = "Target" },
                new Annotation { Type = AnnotationType.Band, From = 70, To = 80 },
                new Annotation { Type = AnnotationType.Point, X = "Headlamp", Y = 60 },
            ],
        };

        var layout = Layout(spec, Revenue(120, 90, 60));

        var label = All(layout.Scene.Series).OfType<TextNode>().Single(t => t.Text == "Target");
        Assert.True(label.At.Y < layout.Plot.Y);
        Assert.Contains(All(layout.Scene.Series), n => n.Class == "rio-annotation rio-annotation-band");
        Assert.Contains(layout.Warnings, w => w.Contains("value lines and bands only", StringComparison.Ordinal));
    }

    [Fact]
    public void The_tooltip_knows_each_bar()
    {
        var layout = Layout(Spec(), Revenue(120, 90, 60));

        var series = layout.Tooltip.Series[0];
        Assert.Equal(Products, series.XLabels);
        Assert.Equal(["120", "90", "60"], series.Values);
        Assert.Equal(Bars(layout)[0].Bounds.Width, series.CellWidths![0], 0.5);
        Assert.True(series.CellHeight > 10);
    }

    [Fact]
    public void Other_series_are_skipped_and_edge_cases_render_cleanly()
    {
        var mixed = Layout(Spec(new SeriesOptions { Id = "r", Type = SeriesType.Bar }, new SeriesOptions { Id = "l" }),
            new() { ["r"] = SeriesData.FromCategories(Products, [1, 2, 3]), ["l"] = SeriesData.FromCategories(Products, [1, 2, 3]) });
        var empty = Layout(Spec(), new() { ["r"] = SeriesData.Empty });
        var compact = Layout(Spec() with { Compact = true }, Revenue(1, 2, 3), Options with { Width = 120, Height = 40 });

        Assert.Contains(mixed.Warnings, w => w.Contains("cannot be drawn in a horizontal chart", StringComparison.Ordinal));
        Assert.Contains(All(empty.Scene), n => n is TextNode { Text: "No data available" });
        Assert.Equal(3, Bars(compact).Count);
        Assert.DoesNotContain(All(compact.Scene), n => n is TextNode);
    }

    // ---------- Box plots ----------

    // Outdoor temperatures in °C at two weather stations; the second has one reading far off.
    private static Dictionary<string, SeriesData> Temperatures() => new()
    {
        ["t"] = SeriesData.FromCategories(["Station 1", "Station 2"], [0.0, 0]).WithBox(
        [
            BoxSummary.FromValues([18.0, 18.5, 19.0, 19.5, 20.0, 20.5, 21.0]),
            BoxSummary.FromValues([19.0, 19.2, 19.4, 19.6, 19.8, 20.0, 27.5]),
        ]),
    };

    [Fact]
    public void Box_plots_lie_down_with_the_median_as_a_vertical_line()
    {
        var layout = Layout(Spec(new SeriesOptions { Id = "t", Name = "Temperature", Type = SeriesType.BoxPlot, Format = "0.0" }), Temperatures());

        var x = layout.YScale;
        var boxes = All(layout.Scene.Series).OfType<RectNode>().Where(n => n.Class == "rio-box").ToList();
        Assert.Equal(2, boxes.Count);
        Assert.Equal(x.Map(18.75), boxes[0].Bounds.X, 1); // from the lower quartile ...
        Assert.Equal(x.Map(20.25), boxes[0].Bounds.Right, 1); // ... to the upper one
        Assert.True(boxes[1].Bounds.Y > boxes[0].Bounds.Bottom); // Station 2 below Station 1
        Assert.Contains(All(layout.Scene.Series), n => n is PathNode { Class: "rio-medians" });
        Assert.Contains(All(layout.Scene.Series), n => n is PathNode { Class: "rio-whiskers" });
        Assert.Contains(All(layout.Scene.Series), n => n is PathNode { Class: "rio-outliers" });
        Assert.True(x.DomainMax >= 27.5 && x.DomainMin > 0); // the outlier fits; no forced zero
        Assert.Empty(layout.Warnings);

        var s = layout.Tooltip.Series.Single();
        Assert.Equal(["Station 1", "Station 2"], s.XLabels);
        Assert.Equal("19.6", s.Values[1]);
        Assert.Equal(["27.5", "19.9", "19.6", "19.3", "19.0", "7", "1: 27.5"], s.PartValues![1]);
        Assert.Equal(boxes[0].Bounds.Width, s.CellWidths![0], 0.5);
    }

    [Fact]
    public void Box_plots_and_bars_share_the_row_in_slots()
    {
        var data = Temperatures();
        data["b"] = SeriesData.FromCategories(["Station 1", "Station 2"], [19.5, 20.1]);
        var layout = Layout(Spec(new SeriesOptions { Id = "t", Type = SeriesType.BoxPlot, Stack = "s" }, new SeriesOptions { Id = "b", Type = SeriesType.Bar, Stack = "s" }), data);

        var box = All(layout.Scene.Series).OfType<RectNode>().First(n => n.Class == "rio-box");
        var bar = Bars(layout, "b")[0];
        Assert.True(bar.Bounds.Y >= box.Bounds.Bottom - 0.5); // the bar's slot is below the box, not stacked onto it
        Assert.Equal(0, layout.YScale.DomainMin); // the bars bring in zero
    }

    // ---------- Waterfalls ----------

    // A month's result in k€: revenue, two costs, a subtotal and the result.
    private static Dictionary<string, SeriesData> Result() => new()
    {
        ["w"] = SeriesData.FromCategories(["Revenue", "Cost of sales", "Gross profit", "Salaries", "Result"], [420.0, -180, double.NaN, -150, double.NaN])
            .WithTotals([false, false, true, false, true]),
    };

    [Fact]
    public void Waterfalls_run_down_the_rows_from_the_running_total()
    {
        var layout = Layout(Spec(new SeriesOptions { Id = "w", Name = "Result", Type = SeriesType.Waterfall, DataLabels = true }), Result());

        var x = layout.YScale;
        var bars = All(layout.Scene.Series).OfType<RectNode>().Where(n => n.Class?.StartsWith("rio-waterfall", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(5, bars.Count);
        Assert.Equal(x.Map(0), bars[0].Bounds.X, 1); // revenue: 0 to 420
        Assert.Equal(x.Map(420), bars[0].Bounds.Right, 1);
        Assert.Equal(x.Map(240), bars[1].Bounds.X, 1); // cost of sales: from 420 back to 240
        Assert.Equal(x.Map(420), bars[1].Bounds.Right, 1);
        Assert.Equal(x.Map(240), bars[2].Bounds.Right, 1); // gross profit: a total from zero
        Assert.Equal("rio-waterfall rio-waterfall-total", bars[2].Class);
        Assert.Equal(x.Map(90), bars[4].Bounds.Right, 1); // the result
        Assert.True(bars[1].Bounds.Y > bars[0].Bounds.Bottom); // first on top
        Assert.Equal(0, x.DomainMin);

        var styles = layout.Scene.Styles!;
        Assert.Equal(ThemeTokens.Positive, styles.Get(bars[0].Style).Fill);
        Assert.Equal(ThemeTokens.Negative, styles.Get(bars[1].Style).Fill);
        Assert.Contains(All(layout.Scene.Series), n => n is PathNode { Class: "rio-connectors" });
        Assert.Contains(All(layout.Scene.Series), n => n is TextNode { Class: "rio-data-label", Text: "-180" });

        var s = layout.Tooltip.Series.Single();
        Assert.Equal(["+420", "-180", "240", "-150", "90"], s.Values.Select(v => v.Replace('−', '-')));
        Assert.Equal(["Total"], s.PartLabels!);
        Assert.Equal("240", s.PartValues![1][0]); // the running total after the change
        Assert.Equal("", s.PartValues[2][0]); // a total shows its value alone
        Assert.Equal(ThemeTokens.Negative, s.PointColors![1]);
    }

    [Fact]
    public void A_json_box_plot_or_waterfall_turns_sideways_with_horizontal()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "horizontal": true, "series": [ { "type": "waterfall", "x": "step", "y": "amount" } ] }""");

        Assert.Empty(parsed.Errors);
        Assert.True(parsed.Definition!.Horizontal);
        Assert.Contains(" Horizontal=\"true\"", ChartRazor.FromDefinition(parsed.Definition, "Row", "rows"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "series": [ { "type": "horizontalBar", "x": "product", "y": "revenue" } ] }""")]
    [InlineData("""{ "horizontal": true, "series": [ { "type": "bar", "x": "product", "y": "revenue" } ] }""")]
    public void Json_specs_turn_bars_sideways(string json)
    {
        var parsed = ChartDefinitionJson.Parse(json);

        Assert.Empty(parsed.Errors);
        Assert.True(parsed.Definition!.Horizontal);
        Assert.Equal(SeriesType.Bar, parsed.Definition.Series[0].Type);
        Assert.Contains(" Horizontal=\"true\"", ChartRazor.FromDefinition(parsed.Definition, "Row", "rows"), StringComparison.Ordinal);
    }
}
