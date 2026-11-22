using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Specs;
using RioCharts.Skia;
using SkiaSharp;

namespace RioCharts.Tests;

public class ScatterTests
{
    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static IEnumerable<Node> All(Scene scene) => All(scene.Static).Concat(All(scene.Series)).Concat(All(scene.Overlay));

    private static readonly LayoutOptions Options = new() { Width = 640, Height = 360, Culture = CultureInfo.InvariantCulture };

    private static List<(float X, float Y)> MoveTos(PathData path)
    {
        var result = new List<(float, float)>();
        var k = 0;
        foreach (var command in path.Commands)
        {
            if (command == PathCommand.MoveTo)
            {
                result.Add((path.Coords[k], path.Coords[k + 1]));
            }

            k += PathData.CoordCount(command);
        }

        return result;
    }

    /// <summary>Hot drinks sold (thousands) against outdoor temperature: the colder outside, the more sold (unordered input).</summary>
    private static readonly double[] Outdoor = [12.5, -3.2, 4.1, 21.7, -8.4, 0.6, 15.3, 8.8];

    private static SeriesData SalesVsOutdoor() => SeriesData.From(Outdoor, Outdoor.Select(t => Math.Round(22 - 0.25 * t, 1)).ToArray());

    private static ChartSpec Spec(bool trend = false, double? size = null) => new()
    {
        X = new AxisSpec { Title = "Outdoor °C" },
        Series = [new SeriesSpec { Id = "s", Name = "Hot drinks", Type = SeriesType.Scatter, TrendLine = trend, MarkerSize = size, Format = "0.0 k" }],
    };

    private static ChartLayoutResult Layout(ChartSpec spec, SeriesData data, LayoutOptions? options = null) =>
        ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["s"] = data }, options ?? Options);

    [Fact]
    public void Each_point_gets_a_marker()
    {
        var layout = Layout(Spec(), SalesVsOutdoor());

        var markers = All(layout.Scene).OfType<PathNode>().Single(n => n.Class == "rio-marker");
        var starts = MoveTos(markers.Path);
        Assert.Equal(Outdoor.Length, starts.Count);
        var hover = layout.Tooltip.Series[0];
        Assert.Equal(Outdoor.Length, hover.X.Length);
        Assert.True(hover.X.Zip(hover.X.Skip(1)).All(p => p.First <= p.Second)); // ascending x for the tooltip search
        Assert.Contains(starts, p => Math.Abs(p.X - hover.X[0]) < 0.01 && Math.Abs(p.Y - (hover.Y[0] - 2.5)) < 0.01); // radius 2.5 (5 px)
        Assert.Equal("Outdoor °C: -8.4", hover.XLabels[0]); // the tooltip says what x is
    }

    [Fact]
    public void Axes_get_nice_ranges_so_points_are_not_on_the_edge()
    {
        var layout = Layout(Spec(), SalesVsOutdoor());

        Assert.True(layout.XScale!.DomainMin < -8.4 && layout.XScale.DomainMax > 21.7);
        Assert.Equal(0, layout.XScale.DomainMin % 5, 6); // a nice number
        Assert.True(layout.YScale.DomainMin > 0); // no forced zero
    }

    [Fact]
    public void The_tooltip_finds_the_nearest_point_not_the_nearest_x()
    {
        var withLine = Spec() with { Series = [.. Spec().Series, new SeriesSpec { Id = "l", Name = "Curve" }] };
        var data = new Dictionary<string, SeriesData> { ["s"] = SalesVsOutdoor(), ["l"] = SeriesData.From([-10.0, 20], [24.5, 17]) };

        var layout = ChartLayout.Compute(withLine, data, Options);

        Assert.False(layout.Tooltip.Shared);
        Assert.True(Layout(Spec() with { Series = [new SeriesSpec { Id = "s" }] }, SalesVsOutdoor()).Tooltip.Shared); // lines stay shared
    }

    [Fact]
    public void Trend_line_runs_through_the_fit_from_the_first_to_the_last_point()
    {
        var layout = Layout(Spec(trend: true), SalesVsOutdoor());

        var trend = All(layout.Scene).OfType<PathNode>().Single(n => n.Class == "rio-trend");
        var hover = layout.Tooltip.Series[0];
        var (x0, y0) = (trend.Path.Coords[0], trend.Path.Coords[1]);
        var (x1, y1) = (trend.Path.Coords[2], trend.Path.Coords[3]);
        Assert.Equal(hover.X[0], x0, 0.01);
        Assert.Equal(hover.X[^1], x1, 0.01);
        // The data lie exactly on a line (rounded to 0.1), so the fit passes through the end points.
        Assert.Equal(hover.Y[0], y0, 1.5);
        Assert.Equal(hover.Y[^1], y1, 1.5);
    }

    [Fact]
    public void Edge_cases_render_cleanly()
    {
        var single = Layout(Spec(trend: true), SeriesData.From([5.0], [20.0]));
        var gaps = Layout(Spec(), SeriesData.From([1.0, 2, 3], [20.0, double.NaN, 21]));
        var empty = Layout(Spec(trend: true), SeriesData.Empty);

        Assert.Single(MoveTos(All(single.Scene).OfType<PathNode>().Single(n => n.Class == "rio-marker").Path));
        Assert.DoesNotContain(All(single.Scene), n => n.Class == "rio-trend");
        Assert.Equal(2, MoveTos(All(gaps.Scene).OfType<PathNode>().Single(n => n.Class == "rio-marker").Path).Count);
        Assert.False(empty.HasData);
    }

    [Fact]
    public void Zooming_draws_only_the_visible_points()
    {
        var layout = Layout(Spec(), SalesVsOutdoor(), Options with { XRange = (0, 10) });

        Assert.Equal(3, MoveTos(All(layout.Scene).OfType<PathNode>().Single(n => n.Class == "rio-marker").Path).Count); // 0.6, 4.1, 8.8
    }

    [Fact]
    public void Legend_has_a_dot_and_a_summary()
    {
        var spec = Spec() with { Series = [.. Spec().Series, new SeriesSpec { Id = "l", Name = "Forecast" }] };
        var data = new Dictionary<string, SeriesData> { ["s"] = SalesVsOutdoor(), ["l"] = SeriesData.From([-10.0, 20], [24.5, 17]) };

        var layout = ChartLayout.Compute(spec, data, Options);

        Assert.Contains(All(layout.Scene), n => n.Class == "rio-swatch rio-swatch-dot");
        Assert.Equal("Hot drinks: 8 data points, 16.6 k to 24.1 k, falling", layout.Legend[0].Tooltip);
    }

    [Theory]
    [InlineData("scatter")]
    [InlineData("points")]
    [InlineData("scatterPlot")]
    public void Json_specs_read_scatter(string type)
    {
        var parsed = ChartSpecJson.Parse($$"""{ "series": [ { "type": "{{type}}", "x": "outdoor", "y": "sales", "markerSize": 3, "trendLine": true } ] }""");

        Assert.Empty(parsed.Errors);
        var s = parsed.Spec!.Series[0];
        Assert.Equal(SeriesType.Scatter, s.Type);
        Assert.Equal(3, s.MarkerSize);
        Assert.True(s.TrendLine);
        Assert.Contains("<ScatterSeries", ChartRazor.FromSpec(parsed.Spec, "Reading", "readings"), StringComparison.Ordinal);
        Assert.Contains(" MarkerSize=\"3\" TrendLine=\"true\"", ChartRazor.FromSpec(parsed.Spec, "Reading", "readings"), StringComparison.Ordinal);
    }

    [Fact]
    public void Skia_renders_scatter()
    {
        var png = ChartExport.ToPng(Spec(trend: true), new Dictionary<string, SeriesData> { ["s"] = SalesVsOutdoor() }, new ExportOptions { Width = 400, Height = 300, Scale = 1 });

        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(400, bitmap.Width);
    }
}
