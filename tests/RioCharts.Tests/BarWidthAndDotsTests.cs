using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

/// <summary>Bar width caps (a lone bar does not fill the plot, MaxBarWidth) and dots on every point of a line.</summary>
public class BarWidthAndDotsTests
{
    private static readonly LayoutOptions Options = new() { Width = 640, Height = 320, Culture = CultureInfo.InvariantCulture };

    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static List<RectNode> Bars(ChartLayoutResult r, string id) =>
        All(r.Scene.Series).Where(n => n is GroupNode g && g.Key == "series:" + id).SelectMany(All).OfType<RectNode>().ToList();

    private static int Dots(ChartLayoutResult r) =>
        All(r.Scene.Series).OfType<PathNode>().Where(p => p.Class == "rio-highlight").Sum(p => p.Path.Commands.Count(c => c == PathCommand.MoveTo));

    private static ChartLayoutResult Layout(ChartDefinition spec, Dictionary<string, SeriesData> data, bool horizontal = false) =>
        ChartLayout.Compute(spec with { Horizontal = horizontal }, data, Options);

    // ---------- Bar width ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_lone_bar_takes_at_most_a_fifth_of_the_plot_and_stays_centered(bool horizontal)
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "s", Type = SeriesType.Bar }] };

        var result = Layout(spec, new() { ["s"] = SeriesData.FromCategories(["Sweden"], [42.0]) }, horizontal);

        var bar = Assert.Single(Bars(result, "s")).Bounds;
        if (horizontal)
        {
            Assert.True(bar.Height <= result.Plot.Height * 0.2 + 0.5, $"bar {bar.Height} px of {result.Plot.Height}");
            Assert.Equal(result.Plot.CenterY, bar.CenterY, 0.5);
        }
        else
        {
            Assert.True(bar.Width <= result.Plot.Width * 0.2 + 0.5, $"bar {bar.Width} px of {result.Plot.Width}");
            Assert.Equal(result.Plot.CenterX, bar.CenterX, 0.5);
        }
    }

    [Fact]
    public void Four_or_more_categories_keep_the_full_band()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "s", Type = SeriesType.Bar }] };
        var data = new Dictionary<string, SeriesData> { ["s"] = SeriesData.FromCategories(["Q1", "Q2", "Q3", "Q4"], [1.0, 2, 3, 4]) };

        var result = Layout(spec, data);

        // Band: plot / (4 - 0.2 + 0.2) × 0.8; the bar leaves a 2 px gap.
        Assert.All(Bars(result, "s"), b => Assert.Equal(result.Plot.Width * 0.2 - 2, b.Bounds.Width, 0.5));
    }

    [Fact]
    public void MaxBarWidth_caps_each_bar_and_keeps_groups_together()
    {
        var spec = new ChartDefinition
        {
            Series =
            [
                new SeriesOptions { Id = "a", Type = SeriesType.Bar, MaxBarWidth = 24 },
                new SeriesOptions { Id = "b", Type = SeriesType.Bar },
            ],
        };
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(["North", "South", "East", "West", "Central"], [1.0, 2, 3, 4, 5]),
            ["b"] = SeriesData.FromCategories(["North", "South", "East", "West", "Central"], [2.0, 3, 4, 5, 6]),
        };

        var result = Layout(spec, data);

        var (a, b) = (Bars(result, "a"), Bars(result, "b"));
        Assert.All(a.Concat(b), r => Assert.Equal(24, r.Bounds.Width, 0.5));
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(2, b[i].Bounds.X - a[i].Bounds.Right, 0.5); // side by side, 2 px apart
        }
    }

    [Fact]
    public void Histograms_keep_touching_bins()
    {
        // One bin: not narrowed by the bar cap, nor by MaxBarWidth (bins touch, a bin is as wide as its range).
        var data = SeriesData.FromBins(new double[] { 42, 42 }, t => t, null, null, out _, out _);
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "h", Type = SeriesType.Histogram, MaxBarWidth = 5 }] };

        var capped = Bars(Layout(spec, new() { ["h"] = data }), "h").Single().Bounds.Width;
        var plain = Bars(Layout(spec with { Series = [spec.Series[0] with { MaxBarWidth = null }] }, new() { ["h"] = data }), "h").Single().Bounds.Width;

        Assert.Equal(plain, capped);
        Assert.True(capped > 50);
    }

    // ---------- Dots ----------

    private static readonly double[] Sparse = [21.5, 22.0, 20.8, 23.1, 22.4, 21.9];

    private static ChartLayoutResult Line(PointHighlight highlight, double? markerSize = null, int count = 6)
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "s", Highlight = highlight, MarkerSize = markerSize }] };
        var xs = Enumerable.Range(0, count).Select(i => (double)i).ToArray();
        var ys = xs.Select(x => Sparse[(int)x % Sparse.Length]).ToArray();
        return Layout(spec, new() { ["s"] = SeriesData.From(xs, ys) });
    }

    [Fact]
    public void Highlight_all_puts_a_dot_on_every_point()
    {
        Assert.Equal(6, Dots(Line(PointHighlight.All)));
        Assert.Equal(2, Dots(Line(PointHighlight.Min | PointHighlight.Max)));
    }

    [Fact]
    public void Highlight_all_skips_dots_beyond_a_thousand_points()
    {
        Assert.Equal(0, Dots(Line(PointHighlight.All, count: 5000)));
        Assert.Equal(1, Dots(Line(PointHighlight.All | PointHighlight.Last, count: 5000)));
    }

    [Fact]
    public void MarkerSize_sets_the_dot_diameter()
    {
        // Each dot starts at its top (MoveTo x, y - radius): compare the first dot's top.
        var small = Line(PointHighlight.All);
        var large = Line(PointHighlight.All, markerSize: 12);
        var smallTop = All(small.Scene.Series).OfType<PathNode>().First(p => p.Class == "rio-highlight").Path.Coords[1];
        var largeTop = All(large.Scene.Series).OfType<PathNode>().First(p => p.Class == "rio-highlight").Path.Coords[1];

        // The same first point: the larger dot's top is 2.5 px higher (radius 6 instead of the default 3.5).
        Assert.Equal(2.5, smallTop - largeTop, 0.1);
    }

    [Fact]
    public void Json_and_razor_carry_the_new_settings()
    {
        var spec = ChartDefinitionJson.Parse("""
            { "x": { "labelRotation": 45 },
              "series": [ { "type": "line", "x": "time", "y": "value", "highlight": "all", "markerSize": 8 },
                          { "type": "bar", "x": "country", "y": "issues", "maxBarWidth": 40 } ] }
            """).Definition!;

        Assert.Equal(PointHighlight.All, spec.Series[0].Highlight);
        Assert.Equal(40, spec.Series[1].MaxBarWidth);

        var razor = ChartRazor.FromDefinition(spec, "Row", "rows");
        Assert.Contains("Highlight=\"PointHighlight.All\" MarkerSize=\"8\"", razor, StringComparison.Ordinal);
        Assert.Contains("MaxBarWidth=\"40\"", razor, StringComparison.Ordinal);
        Assert.Contains("XAxis=\"@(new AxisOptions { LabelRotation = 45 })\"", razor, StringComparison.Ordinal);
    }
}
