using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

/// <summary>Color scale legends (heatmap, calendar, map) follow Legend.Position, and tick steps follow AxisOptions.TickBase.</summary>
public class ScaleLegendTests
{
    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static readonly LayoutOptions Options = new() { Width = 640, Height = 320, Culture = CultureInfo.InvariantCulture };

    private static readonly LegendOptions Bottom = new() { Position = LegendPosition.Bottom };

    private static float ScaleY(ChartLayoutResult layout) =>
        All(All(layout.Scene.Static).OfType<GroupNode>().Single(g => g.Class == "rio-legend rio-heat-legend")).OfType<RectNode>().First().Bounds.Y;

    private static ChartLayoutResult Heatmap(LegendOptions? legend) => ChartLayout.Compute(
        new ChartDefinition { Legend = legend ?? new LegendOptions(), Series = [new SeriesOptions { Id = "Mon", Type = SeriesType.Heatmap }, new SeriesOptions { Id = "Tue", Type = SeriesType.Heatmap }] },
        new Dictionary<string, SeriesData>
        {
            ["Mon"] = SeriesData.FromCategories(["08", "12"], [10, 20]),
            ["Tue"] = SeriesData.FromCategories(["08", "12"], [30, 40]),
        }, Options);

    [Fact]
    public void A_heatmap_scale_goes_below_the_plot_with_a_bottom_legend()
    {
        var top = Heatmap(null);
        var bottom = Heatmap(Bottom);

        Assert.True(ScaleY(top) < top.Plot.Y);
        Assert.True(ScaleY(bottom) > bottom.Plot.Bottom);
        Assert.True(bottom.Plot.Y < top.Plot.Y); // the rows start higher, the room moved to the bottom
    }

    [Fact]
    public void A_heatmap_with_color_bands_puts_the_bands_below_too()
    {
        var spec = new ChartDefinition
        {
            Legend = Bottom,
            Series = [new SeriesOptions { Id = "Mon", Type = SeriesType.Heatmap, ColorBands = [new() { Color = "#3ca951" }, new() { Above = 15, Color = "#e11d48" }] }],
        };
        var layout = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["Mon"] = SeriesData.FromCategories(["08", "12"], [10, 20]) }, Options);

        Assert.True(ScaleY(layout) > layout.Plot.Bottom);
    }

    [Fact]
    public void A_calendar_scale_goes_below_with_a_bottom_legend()
    {
        var days = Enumerable.Range(0, 60).Select(i => new DateTime(2026, 1, 1).AddDays(i)).ToArray();
        var data = SeriesData.From(days, [.. days.Select((_, i) => (double)(i % 7))]);
        var layout = ChartLayout.Compute(new ChartDefinition { Legend = Bottom, Series = [new SeriesOptions { Id = "c", Type = SeriesType.Calendar }] },
            new Dictionary<string, SeriesData> { ["c"] = data }, Options);

        var cells = All(layout.Scene.Series).OfType<RectNode>().ToList();
        Assert.True(ScaleY(layout) > cells.Max(c => c.Bounds.Bottom));
    }

    [Fact]
    public void A_map_scale_goes_below_with_a_bottom_legend()
    {
        var layout = ChartLayout.Compute(new ChartDefinition { Legend = Bottom, Series = [new SeriesOptions { Id = "m", Type = SeriesType.Map }] },
            new Dictionary<string, SeriesData> { ["m"] = SeriesData.FromCategories(["SE", "NO", "DE"], [12.0, 3, 4]) }, Options);

        Assert.True(ScaleY(layout) > layout.Plot.Bottom);
    }

    // ---------- TickBase ----------

    private static string Mebibytes(double v) => (v / 1024 / 1024).ToString("0.#", CultureInfo.InvariantCulture) + " MB";

    [Fact]
    public void Ticks_on_a_1024_base_read_round_in_binary_units()
    {
        double[] bytes = [12 * 1048576.0, 95 * 1048576.0, 60 * 1048576.0];
        AxisOptions Axis(double? tickBase) => new() { LabelFormatter = Mebibytes, TickBase = tickBase };
        List<string> Labels(AxisOptions axis) =>
        [
            .. All(ChartLayout.Compute(new ChartDefinition { Y = axis, Series = [new SeriesOptions { Id = "s", Type = SeriesType.Bar }] },
                new Dictionary<string, SeriesData> { ["s"] = SeriesData.FromCategories(["a", "b", "c"], bytes) }, Options).Scene.Static)
                .Where(n => n.Class == "rio-axis-y").SelectMany(All).OfType<TextNode>().Select(t => t.Text),
        ];

        Assert.Contains(Labels(Axis(null)), l => l.Contains('.')); // decimal steps: 19.1 MB, 38.1 MB, ...
        var binary = Labels(Axis(1024));
        Assert.All(binary, l => Assert.DoesNotContain(".", l));
        Assert.Contains("20 MB", binary);
    }

    [Fact]
    public void A_tick_base_picks_the_unit_below_the_raw_step()
    {
        Assert.Equal(20 * 1048576.0, NiceTicks.Step(100 * 1048576.0, 5, 1024));
        Assert.Equal(200 * 1024.0, NiceTicks.Step(1000 * 1024.0, 5, 1024));
        Assert.Equal(200, NiceTicks.Step(1000, 5, 1024)); // below one unit: plain steps
        Assert.Equal(NiceTicks.Step(1e9, 5), NiceTicks.Step(1e9, 5, 0));
    }
}
