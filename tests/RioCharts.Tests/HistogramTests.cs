using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class HistogramTests
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

    // Response times in ms of 20 requests.
    private static readonly double[] Times = [112, 118, 124, 131, 133, 137, 141, 142, 144, 146, 148, 149, 155, 158, 163, 171, 176, 188, 204, 236];

    // ---------- Bins ----------

    [Fact]
    public void A_bin_width_gives_bins_on_its_multiples()
    {
        var bins = Bins.For(Times, width: 25);

        Assert.Equal(new Bins(100, 25, 6), bins); // 100 to 250
        Assert.Equal(0, bins.IndexOf(112));
        Assert.Equal(1, bins.IndexOf(125)); // an edge starts the next bin
        Assert.Equal(-1, bins.IndexOf(99));
        Assert.Equal(-1, bins.IndexOf(double.NaN));
    }

    [Fact]
    public void A_bin_count_is_rounded_to_a_nice_width()
    {
        var bins = Bins.For([.. Enumerable.Range(0, 101).Select(i => (double)i)], count: 10);

        Assert.Equal(new Bins(0, 10, 10), bins);
        Assert.Equal(9, bins.IndexOf(100)); // the largest value is in the last bin, not a bin of its own
    }

    [Fact]
    public void Values_on_edges_land_in_the_bin_they_start()
    {
        var bins = Bins.For([0.0, 0.3, 0.7], width: 0.1);

        Assert.Equal(3, bins.IndexOf(0.3));
        Assert.Equal(0.3, bins.Edge(3));
    }

    [Fact]
    public void Automatic_bins_cover_the_data_with_a_nice_width()
    {
        var random = new Random(4);
        var values = Enumerable.Range(0, 1000).Select(_ => 150 + 30 * (random.NextDouble() + random.NextDouble() + random.NextDouble() - 1.5)).ToArray();

        var bins = Bins.For(values);

        Assert.InRange(bins.Count, 8, 40);
        Assert.True(bins.Start <= values.Min() && bins.Edge(bins.Count) >= values.Max());
        Assert.Contains(bins.Width / Math.Pow(10, Math.Floor(Math.Log10(bins.Width))), new[] { 1.0, 2, 5 });
    }

    [Fact]
    public void Odd_values_give_clean_bins()
    {
        Assert.Equal(0, Bins.For([]).Count);
        Assert.Equal(0, Bins.For([double.NaN]).Count);
        var same = Bins.For([42.0, 42, 42]);
        Assert.Equal(1, same.Count);
        Assert.Equal(0, same.IndexOf(42));
        Assert.True(Bins.For([0.0, 1e9], width: 1).Count <= Bins.MaxCount); // too narrow: widened
    }

    [Fact]
    public void Items_become_one_point_per_bin_with_empty_bins()
    {
        var data = SeriesData.FromBins(Times, t => t, 25, null, out var groups, out var bins);

        Assert.Equal([112.5, 137.5, 162.5, 187.5, 212.5, 237.5], data.X);
        Assert.Equal([3.0, 9, 4, 2, 1, 1], data.Y);
        Assert.Equal([0, 1, 2], groups[0]);
        Assert.Equal(25, bins.Width);
        Assert.Equal(0, SeriesData.FromBins(Array.Empty<double>(), t => t, null, null, out _, out _).Count);
    }

    // ---------- Layout ----------

    private static ChartLayoutResult Layout(SeriesData data, LayoutOptions? options = null, params SeriesOptions[] series) =>
        ChartLayout.Compute(new ChartDefinition { Series = series.Length > 0 ? series : [new SeriesOptions { Id = "h", Name = "Response time", Type = SeriesType.Histogram, BinWidth = 25 }] },
            new Dictionary<string, SeriesData> { ["h"] = data }, options ?? Options);

    private static SeriesData Binned() => SeriesData.FromBins(Times, t => t, 25, null, out _, out _);

    private static List<RectNode> Bars(ChartLayoutResult layout) =>
        All<RectNode>(layout.Scene.Series).Where(n => n.Class == "rio-bin").ToList();

    [Fact]
    public void Bins_touch_from_edge_to_edge()
    {
        var layout = Layout(Binned());
        var bars = Bars(layout);

        Assert.Equal(6, bars.Count);
        Assert.InRange(bars[0].Bounds.X - layout.XScale!.Map(100), 0, 1); // half the hairline on each side
        Assert.InRange(layout.XScale.Map(250) - bars[^1].Bounds.Right, 0, 1);
        for (var i = 1; i < bars.Count; i++)
        {
            Assert.InRange(bars[i].Bounds.X - bars[i - 1].Bounds.Right, 0.5, 1.5); // a hairline, not a bar gap
        }

        Assert.Equal(layout.YScale.Map(9), bars[1].Bounds.Y, 1);
        Assert.Empty(layout.Warnings);
    }

    [Fact]
    public void Tooltip_shows_the_range_and_the_count()
    {
        var s = Layout(Binned()).Tooltip.Series.Single();

        Assert.Equal("125 – 150", s.XLabels[1]);
        Assert.Equal("9", s.Values[1]);
    }

    [Fact]
    public void Empty_bins_draw_nothing_but_take_hovers()
    {
        var data = SeriesData.FromBins(new double[] { 1, 2, 9 }, t => t, 2, null, out _, out _);

        var layout = Layout(data, null, new SeriesOptions { Id = "h", Type = SeriesType.Histogram, BinWidth = 2 });

        Assert.Equal(3, Bars(layout).Count); // 0–2, 2–4 and 8–10
        Assert.Equal(5, layout.Tooltip.Series.Single().X.Length);
    }

    [Fact]
    public void A_single_bin_is_one_bin_wide()
    {
        var data = SeriesData.FromBins(new double[] { 42, 42 }, t => t, null, null, out _, out var bins);

        var layout = Layout(data, null, new SeriesOptions { Id = "h", Type = SeriesType.Histogram, BinWidth = bins.Width });

        var bar = Bars(layout).Single();
        Assert.InRange(bar.Bounds.X - layout.XScale!.Map(bins.Start), 0, 1);
        Assert.InRange(layout.XScale.Map(bins.Edge(1)) - bar.Bounds.Right, 0, 1);
    }

    [Fact]
    public void Without_a_bin_width_the_distance_between_bins_is_used()
    {
        var layout = Layout(Binned(), null, new SeriesOptions { Id = "h", Type = SeriesType.Histogram });

        Assert.Equal("125 – 150", layout.Tooltip.Series.Single().XLabels[1]);
    }

    [Fact]
    public void Two_histograms_overlay_see_through()
    {
        var other = SeriesData.FromBins(Times.Select(t => t + 30), t => t, 25, null, out _, out _);
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "a", Type = SeriesType.Histogram, BinWidth = 25 }, new SeriesOptions { Id = "b", Type = SeriesType.Histogram, BinWidth = 25 }],
        }, new Dictionary<string, SeriesData> { ["a"] = Binned(), ["b"] = other }, Options);

        var bars = Bars(layout);
        Assert.All(bars, b => Assert.Equal(0.55f, layout.Scene.Styles.Get(b.Style).FillOpacity));
        var a = bars.First(b => Math.Abs(b.Bounds.X - layout.XScale!.Map(150)) < 1); // a's third bin and b's second share the x
        Assert.Equal(2, bars.Count(b => Math.Abs(b.Bounds.X - a.Bounds.X) < 0.5));
    }

    [Fact]
    public void A_histogram_is_described_by_its_count_and_fullest_bin()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Legend = new LegendOptions { Mode = LegendMode.Show },
            Series = [new SeriesOptions { Id = "h", Name = "Response time", Type = SeriesType.Histogram, BinWidth = 25 }],
        }, new Dictionary<string, SeriesData> { ["h"] = Binned() }, Options);

        Assert.Equal("Response time: 20 values, most from 125 to 150", layout.Legend.Single().Tooltip);
        Assert.Contains("Svarstid: 20 värden, flest från 125 till 150", ChartLayout.Compute(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "h", Name = "Svarstid", Type = SeriesType.Histogram, BinWidth = 25 }],
        }, new Dictionary<string, SeriesData> { ["h"] = Binned() }, Options with { Culture = CultureInfo.GetCultureInfo("sv-SE") }).Scene.Description);
    }

    [Fact]
    public void A_histogram_is_not_stacked()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Y = new AxisOptions { Stacking = StackMode.Percent },
            Series = [new SeriesOptions { Id = "h", Type = SeriesType.Histogram, BinWidth = 25 }],
        }, new Dictionary<string, SeriesData> { ["h"] = Binned() }, Options);

        Assert.Contains(layout.Warnings, w => w.Contains("percent axis", StringComparison.Ordinal));
        Assert.Equal(layout.YScale.Map(9), Bars(layout)[1].Bounds.Y, 1);
    }

    [Fact]
    public void Odd_data_renders_cleanly()
    {
        foreach (var data in new[] { SeriesData.Empty, SeriesData.FromCategories(["a", "b"], [1.0, 2]), SeriesData.From([5.0], [0.0]) })
        {
            Assert.NotNull(Layout(data).Scene);
        }
    }

    // ---------- JSON definitions ----------

    private static Dictionary<string, ChartDataset> Requests() => new()
    {
        ["d"] = ChartDataset.FromRows("d", Times.Select((t, i) => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["ms"] = t, ["region"] = i % 2 == 0 ? "EU" : "US", ["path"] = "/api",
        })),
    };

    [Fact]
    public void A_json_histogram_counts_its_x_values()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "histogram", "x": "ms", "binWidth": 25 } ] }""");

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, Requests());

        Assert.Empty(bound.Errors);
        Assert.Equal([3.0, 9, 4, 2, 1, 1], bound.Data.Values.Single().Y);
        Assert.Equal(25, bound.Definition!.Series.Single().BinWidth);
        Assert.Contains("<HistogramSeries", ChartRazor.FromDefinition(parsed.Definition!, "Request", "requests"), StringComparison.Ordinal);
        Assert.Contains(" XValue=\"r => r.Ms\" BinWidth=\"25\"", ChartRazor.FromDefinition(parsed.Definition!, "Request", "requests"), StringComparison.Ordinal);
        Assert.DoesNotContain("YValue", ChartRazor.FromDefinition(parsed.Definition!, "Request", "requests"), StringComparison.Ordinal);
    }

    [Fact]
    public void Json_histograms_per_group_share_their_bin_width()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "distribution", "x": "ms", "groupBy": "region" } ] }""");

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, Requests());

        Assert.Empty(bound.Errors);
        Assert.Equal(2, bound.Definition!.Series.Count);
        Assert.Single(bound.Definition.Series.Select(s => s.BinWidth).Distinct());
        Assert.Equal(20, bound.Data.Values.Sum(d => d.Y.Sum()));
    }

    [Fact]
    public void A_json_histogram_reads_its_values_from_y_and_rejects_text()
    {
        var fromY = ChartDefinitionBinder.Bind(ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "histogram", "y": "ms" } ] }""").Definition!, Requests());
        Assert.Empty(fromY.Errors);
        Assert.Contains(fromY.Warnings, w => w.Contains("counts the x values", StringComparison.Ordinal));

        var text = ChartDefinitionBinder.Bind(ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "histogram", "x": "region" } ] }""").Definition!, Requests());
        Assert.Contains(text.Errors, e => e.Path == "series[0].x");
    }
}
