using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Definitions;
using RioCharts.Skia;
using SkiaSharp;

namespace RioCharts.Tests;

public class HeatmapTests
{
    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static IEnumerable<Node> All(Scene scene) => All(scene.Static).Concat(All(scene.Series)).Concat(All(scene.Overlay));

    private static readonly LayoutOptions Options = new() { Width = 640, Height = 300, Culture = CultureInfo.InvariantCulture };

    private static readonly string[] Days = ["Mon", "Tue", "Wed"];

    private static readonly string[] Hours = ["08", "12", "16", "20"];

    /// <summary>Sessions: Mon 10 20 30 40, Tue 50 60 70 80, Wed 90 100 110 (no 20:00: a gap).</summary>
    private static Dictionary<string, SeriesData> Week() => new()
    {
        ["Mon"] = SeriesData.FromCategories(Hours, [10, 20, 30, 40]),
        ["Tue"] = SeriesData.FromCategories(Hours, [50, 60, 70, 80]),
        ["Wed"] = SeriesData.FromCategories(Hours, [90, 100, 110, double.NaN]),
    };

    private static ChartDefinition Spec(bool labels = false, double? max = null) => new()
    {
        Title = "Sessions",
        Series = [.. Days.Select(d => new SeriesOptions { Id = d, Type = SeriesType.Heatmap, DataLabels = labels, Max = max, Format = "#,0" })],
    };

    private static List<RectNode> Cells(ChartLayoutResult layout) =>
        All(layout.Scene.Series).OfType<GroupNode>().Single(g => g.Key == "series:heatmap").Children.OfType<RectNode>().ToList();

    private static float Opacity(ChartLayoutResult layout, RectNode cell) => layout.Scene.Styles.Get(cell.Style).FillOpacity ?? 1;

    [Fact]
    public void Each_value_is_a_cell_and_each_series_a_row()
    {
        var layout = ChartLayout.Compute(Spec(), Week(), Options);

        var cells = Cells(layout);
        Assert.Equal(11, cells.Count); // 12 minus the gap
        Assert.Equal(Days, All(layout.Scene.Static).OfType<TextNode>().Where(t => t.Class == "rio-row-label").Select(t => t.Text));
        var rows = cells.Select(c => c.Bounds.Y).Distinct().OrderBy(y => y).ToList();
        Assert.Equal(3, rows.Count);
        Assert.True(cells.All(c => Math.Abs(c.Bounds.Width - cells[0].Bounds.Width) < 0.01)); // equal cells
        Assert.False(layout.Tooltip.Shared);
    }

    [Fact]
    public void Higher_values_are_more_opaque()
    {
        var layout = ChartLayout.Compute(Spec(), Week(), Options);

        var cells = Cells(layout);
        Assert.Equal(0.08f, Opacity(layout, cells[0]), 2); // Mon 08: the lowest
        Assert.Equal(1f, Opacity(layout, cells[^1]), 2);   // Wed 16: the highest
        Assert.True(Opacity(layout, cells[1]) > Opacity(layout, cells[0]));
    }

    [Fact]
    public void Max_fixes_the_top_of_the_scale()
    {
        var layout = ChartLayout.Compute(Spec(max: 220), Week(), Options);

        Assert.Equal(0.08 + 0.92 * 100 / 210, Opacity(layout, Cells(layout)[^1]), 2); // 110 of 10..220
    }

    [Fact]
    public void The_tooltip_knows_each_cell()
    {
        var layout = ChartLayout.Compute(Spec(), Week(), Options);

        var tue = layout.Tooltip.Series[1];
        Assert.Equal("Tue", tue.Name);
        Assert.Equal(Hours, tue.XLabels);
        Assert.Equal(["50", "60", "70", "80"], tue.Values);
        Assert.True(tue.CellHeight > 10);
        Assert.Equal(tue.BarWidth, Cells(layout)[0].Bounds.Width + 1, 0.01); // cell minus the 1 px gap
        Assert.True(tue.X.Zip(tue.X.Skip(1)).All(p => p.First < p.Second));
    }

    [Fact]
    public void The_legend_is_a_color_scale()
    {
        var layout = ChartLayout.Compute(Spec(), Week(), Options);

        var legend = All(layout.Scene.Static).OfType<GroupNode>().Single(g => g.Class == "rio-legend rio-heat-legend");
        Assert.Equal(["10", "110"], legend.Children.OfType<TextNode>().Select(t => t.Text));
        Assert.Equal(5, legend.Children.OfType<RectNode>().Count());
        Assert.Empty(layout.Legend); // no series legend: series are rows
        Assert.Equal("Chart \"Sessions\" with 3 series. Lowest 10 at Mon 08, highest 110 at Wed 16.", layout.Scene.Description);
    }

    private static readonly ColorBand[] Traffic =
    [
        new() { Below = 40, Color = "#3ca951" },
        new() { Above = 40, Below = 80, Color = "#efb118" },
        new() { Above = 80, Color = "#e11d48" },
    ];

    [Fact]
    public void Color_bands_give_stepped_solid_colors_and_a_legend_of_the_bands()
    {
        var spec = Spec(labels: true) with { Series = [.. Spec().Series.Select(s => s with { ColorBands = Traffic, DataLabels = true })] };
        var layout = ChartLayout.Compute(spec, Week(), Options);

        var cells = Cells(layout);
        string Fill(RectNode cell) => layout.Scene.Styles.Get(cell.Style).Fill!;
        Assert.Equal("#3ca951", Fill(cells[0]));  // Mon 08: 10
        Assert.Equal("#efb118", Fill(cells[4]));  // Tue 08: 50
        Assert.Equal("#e11d48", Fill(cells[^1])); // Wed 16: 110
        Assert.All(cells, c => Assert.Equal(1f, Opacity(layout, c)));

        var legend = All(layout.Scene.Static).OfType<GroupNode>().Single(g => g.Class == "rio-legend rio-heat-legend");
        Assert.Equal(["< 40", "40–80", "> 80"], legend.Children.OfType<TextNode>().Select(t => t.Text));
        Assert.Equal(3, legend.Children.OfType<RectNode>().Count());
        Assert.Equal("#e11d48", layout.Tooltip.Series[2].PointColors![^1]);
        Assert.Equal("Chart \"Sessions\" with 3 series. Lowest 10 at Mon 08, highest 110 at Wed 16.", layout.Scene.Description); // real values

        // Labels on the yellow band are dark, on green and red light.
        var labels = All(layout.Scene.Series).OfType<TextNode>().Where(t => t.Class == "rio-data-label").ToList();
        Assert.Equal("heat-label-dark", labels.Single(l => l.Text == "50").Style.Name);
        Assert.Equal("heat-label-inverse", labels.Single(l => l.Text == "10").Style.Name);
    }

    [Fact]
    public void A_cell_own_color_wins_over_the_scale()
    {
        var data = Week();
        data["Mon"] = data["Mon"].WithColors(["--rio-negative", null, null, null]);
        var layout = ChartLayout.Compute(Spec(), data, Options);

        var cells = Cells(layout);
        Assert.Equal("--rio-negative", layout.Scene.Styles.Get(cells[0].Style).Fill);
        Assert.Equal(1f, Opacity(layout, cells[0]));
        Assert.True(Opacity(layout, cells[1]) < 1); // the others keep the opacity scale
    }

    [Fact]
    public void Values_show_in_cells_with_contrast()
    {
        var layout = ChartLayout.Compute(Spec(labels: true), Week(), Options);

        var labels = All(layout.Scene.Series).OfType<TextNode>().Where(t => t.Class == "rio-data-label").ToList();
        Assert.Equal(11, labels.Count);
        Assert.Equal("heat-label", labels.First(l => l.Text == "10").Style.Name);
        Assert.Equal("heat-label-inverse", labels.First(l => l.Text == "110").Style.Name);
    }

    [Fact]
    public void Time_columns_are_as_wide_as_the_data_spacing()
    {
        var start = new DateTime(2026, 11, 16);
        var times = Enumerable.Range(0, 24).Select(h => start.AddHours(h)).ToArray();
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "a", Type = SeriesType.Heatmap }, new SeriesOptions { Id = "b", Type = SeriesType.Heatmap }] };
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.From(times, times.Select(t => (double)t.Hour).ToArray()),
            ["b"] = SeriesData.From(times, times.Select(t => 24.0 - t.Hour).ToArray()),
        };

        var layout = ChartLayout.Compute(spec, data, Options);

        var cells = Cells(layout);
        Assert.Equal(48, cells.Count);
        Assert.Equal(layout.Plot.Width / 24, cells[0].Bounds.Width + 1, 0.01);
        Assert.Contains(All(layout.Scene.Static), n => n.Class == "rio-axis-x");
    }

    [Fact]
    public void Other_series_types_are_skipped_with_a_warning()
    {
        var spec = Spec() with { Series = [.. Spec().Series, new SeriesOptions { Id = "line" }] };
        var data = Week();
        data["line"] = SeriesData.From([1.0, 2], [1.0, 2]);

        var layout = ChartLayout.Compute(spec, data, Options);

        Assert.Contains(layout.Warnings, w => w.Contains("cannot be mixed with heatmap", StringComparison.Ordinal));
        Assert.Equal(11, Cells(layout).Count);
    }

    [Fact]
    public void Empty_and_compact_render_cleanly()
    {
        var empty = ChartLayout.Compute(Spec(), Days.ToDictionary(d => d, _ => SeriesData.Empty), Options);
        var compact = ChartLayout.Compute(Spec() with { Compact = true }, Week(), Options with { Width = 160, Height = 40 });

        Assert.False(empty.HasData);
        Assert.Contains(All(empty.Scene), n => n is TextNode { Text: "No data available" });
        Assert.Equal(11, Cells(compact).Count);
        Assert.DoesNotContain(All(compact.Scene), n => n is TextNode);
    }

    [Fact]
    public void A_json_spec_makes_rows_with_groupBy()
    {
        var rows = Days.SelectMany(d => Hours.Select((h, i) => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["weekday"] = d, ["hour"] = h, ["sessions"] = 10.0 * (i + 1),
        }));
        var dataset = ChartDataset.FromRows("traffic", rows);
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "traffic", "series": [ { "type": "heatmap", "x": "hour", "y": "sessions", "groupBy": "weekday" } ] }""");

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, new Dictionary<string, ChartDataset> { ["traffic"] = dataset });
        var layout = ChartLayout.Compute(bound.Definition, bound.Data, Options);

        Assert.Empty(bound.Errors);
        Assert.Equal(Days, layout.Tooltip.Series.Select(s => s.Name));
        Assert.Equal(12, Cells(layout).Count);
        Assert.Contains("<HeatmapSeries", ChartRazor.FromDefinition(parsed.Definition!, "Row", "rows"), StringComparison.Ordinal);
    }

    [Fact]
    public void Skia_renders_a_heatmap()
    {
        using var bitmap = SKBitmap.Decode(ChartExport.ToPng(Spec(labels: true), Week(), new ExportOptions { Width = 400, Height = 240, Scale = 1 }));

        Assert.Equal(400, bitmap.Width);
    }

    // ---------- Diverging colors ----------

    /// <summary>Change in sessions against last week, in %: Mon −40 −10 0 20, Tue 5 10 −20 80.</summary>
    private static Dictionary<string, SeriesData> Changes() => new()
    {
        ["Mon"] = SeriesData.FromCategories(Hours, [-40, -10, 0, 20]),
        ["Tue"] = SeriesData.FromCategories(Hours, [5, 10, -20, 80]),
    };

    private static ChartDefinition Diverging(string? below = null, double midpoint = 0) => new()
    {
        Series =
        [
            new SeriesOptions { Id = "Mon", Type = SeriesType.Heatmap, Midpoint = midpoint, BelowColor = below, Color = "--rio-positive" },
            new SeriesOptions { Id = "Tue", Type = SeriesType.Heatmap },
        ],
    };

    [Fact]
    public void A_midpoint_colors_values_below_it_in_the_below_color_and_each_side_grows_from_it()
    {
        var layout = ChartLayout.Compute(Diverging(), Changes(), Options);

        var cells = Cells(layout);
        var styles = layout.Scene.Styles;
        string Fill(int i) => styles.Get(cells[i].Style).Fill!;
        Assert.Equal(ThemeTokens.Negative, Fill(0)); // −40
        Assert.Equal(ThemeTokens.Negative, Fill(1)); // −10
        Assert.Equal("--rio-positive", Fill(2)); // the midpoint itself
        Assert.Equal("--rio-positive", Fill(7)); // 80
        Assert.Equal(1, Opacity(layout, cells[0]), 2); // the lowest: full
        Assert.Equal(1, Opacity(layout, cells[7]), 2); // the highest: full
        Assert.Equal(0.08, Opacity(layout, cells[2]), 2); // the midpoint: faint
        Assert.True(Opacity(layout, cells[1]) < Opacity(layout, cells[6])); // −10 is a quarter of the way down, −20 half
        Assert.Equal(ThemeTokens.Negative, layout.Tooltip.Series[0].PointColors![0]);

        var legend = All(layout.Scene.Static).OfType<GroupNode>().Single(g => g.Class == "rio-legend rio-heat-legend");
        Assert.Equal(["-40", "0", "80"], legend.Children.OfType<TextNode>().Select(t => t.Text.Replace('−', '-')));
        Assert.Equal(8, legend.Children.OfType<RectNode>().Count());
    }

    [Fact]
    public void A_diverging_scale_takes_its_below_color_and_leaves_out_an_empty_side()
    {
        var own = ChartLayout.Compute(Diverging("--rio-series-2"), Changes(), Options);
        var allAbove = ChartLayout.Compute(Diverging(midpoint: -50), Changes(), Options);

        Assert.Equal("--rio-series-2", own.Scene.Styles.Get(Cells(own)[0].Style).Fill);
        var legend = All(allAbove.Scene.Static).OfType<GroupNode>().Single(g => g.Class == "rio-legend rio-heat-legend");
        Assert.Equal(["-50", "80"], legend.Children.OfType<TextNode>().Select(t => t.Text.Replace('−', '-')));
        Assert.Equal(4, legend.Children.OfType<RectNode>().Count());
        Assert.All(Cells(allAbove), c => Assert.Equal("--rio-positive", allAbove.Scene.Styles.Get(c.Style).Fill));
    }

    [Fact]
    public void A_json_heatmap_reads_its_midpoint()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "series": [ { "type": "heatmap", "x": "hour", "y": "change", "midpoint": 0, "belowColor": "#2563eb" } ] }""");

        Assert.Empty(parsed.Errors);
        Assert.Equal(0, parsed.Definition!.Series[0].Midpoint);
        Assert.Equal("#2563eb", parsed.Definition.Series[0].BelowColor);
        var razor = ChartRazor.FromDefinition(parsed.Definition, "Row", "rows");
        Assert.Contains(" Midpoint=\"0\"", razor, StringComparison.Ordinal);
        Assert.Contains(" BelowColor=\"#2563eb\"", razor, StringComparison.Ordinal);
    }
}
