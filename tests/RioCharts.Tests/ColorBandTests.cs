using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Definitions;

namespace RioCharts.Tests;

/// <summary>Colors by value (SeriesOptions.ColorBands): lines change color where they cross a band edge.</summary>
public class ColorBandTests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };
    private const string Red = "--rio-negative";
    private const string Amber = "#f2a516";
    private static readonly string[] Hours = ["08", "09", "10", "11", "12", "13"];

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    // Response times in ms: over 120 is slow, under 20 suspiciously fast.
    private static ChartLayoutResult Chart(SeriesType type, params ColorBand[] bands) => ChartLayout.Compute(
        new ChartDefinition { Series = [new SeriesOptions { Id = "s", Type = type, ColorBands = bands }] },
        new Dictionary<string, SeriesData> { ["s"] = SeriesData.FromCategories(Hours, [80.0, 140, 95, 10, 130, 60]) },
        Options);

    [Fact]
    public void Ranges_cut_the_value_axis_into_colors()
    {
        var series = ThemeTokens.Series(0);

        var ranges = RioCharts.Core.Series.ColorBands.Ranges([new() { Above = 120, Color = Red }, new() { Below = 20, Color = Amber }], series);

        Assert.Equal([(double.NegativeInfinity, 20, Amber), (20, 120, series), (120, double.PositiveInfinity, Red)], ranges);
        Assert.Equal(Red, RioCharts.Core.Series.ColorBands.ColorOf([new() { Above = 0, Color = "x" }, new() { Above = 100, Color = Red }], 150)); // later wins
        Assert.Null(RioCharts.Core.Series.ColorBands.ColorOf([new() { Above = 120, Color = Red }], 120)); // above, not at
    }

    [Fact]
    public void A_line_is_drawn_once_per_band_clipped_to_its_values()
    {
        var result = Chart(SeriesType.Line, new ColorBand { Above = 120, Color = Red });

        var bands = All<GroupNode>(result.Scene.Series).Where(g => g.Class == "rio-color-band").ToList();
        Assert.Equal(2, bands.Count);
        var red = bands.Single(b => result.Scene.Styles.Get(((PathNode)b.Children[0]).Style).Stroke == Red);
        var y120 = (float)result.YScale.Map(120);
        Assert.Equal(y120, red.Clip!.Value.Bottom, 0.5); // the red copy shows only above 120
        Assert.True(red.Clip.Value.Y < result.Plot.Y);
        Assert.Equal(y120, bands.Single(b => b != red).Clip!.Value.Y, 0.5);
        var tip = result.Tooltip.Series[0];
        Assert.Equal([ThemeTokens.Series(0), Red, ThemeTokens.Series(0), ThemeTokens.Series(0), Red, ThemeTokens.Series(0)], tip.PointColors!); // tooltip dots
    }

    [Fact]
    public void Areas_band_both_fill_and_line()
    {
        var result = Chart(SeriesType.Area, new ColorBand { Below = 20, Color = Amber });

        var bands = All<GroupNode>(result.Scene.Series).Where(g => g.Class == "rio-color-band").ToList();
        Assert.Equal(4, bands.Count); // fill and line, each in two ranges
        Assert.Contains(bands, b => result.Scene.Styles.Get(((PathNode)b.Children[0]).Style).Fill == Amber);
    }

    [Fact]
    public void Bars_and_scatter_take_their_bands_color_and_own_colors_win()
    {
        var bars = Chart(SeriesType.Bar, new ColorBand { Above = 120, Color = Red });
        var own = ChartLayout.Compute(
            new ChartDefinition { Series = [new SeriesOptions { Id = "s", Type = SeriesType.Bar, ColorBands = [new() { Above = 120, Color = Red }] }] },
            new Dictionary<string, SeriesData> { ["s"] = SeriesData.FromCategories(Hours, [80.0, 140, 95, 10, 130, 60]).WithColors([null, "#000", null, null, null, null]) },
            Options);

        string?[] Fills(ChartLayoutResult r) => [.. All<RectNode>(r.Scene.Series).Where(n => n.Class == "rio-bar").Select(n => r.Scene.Styles.Get(n.Style).Fill)];
        Assert.Equal(2, Fills(bars).Count(f => f == Red));
        Assert.Equal("#000", Fills(own)[1]); // ColorValue wins over the band
        Assert.Equal(Red, Fills(own)[4]);
    }

    [Fact]
    public void No_bands_draw_as_before_and_a_band_off_the_axis_adds_nothing()
    {
        var plain = Chart(SeriesType.Line);
        var off = Chart(SeriesType.Line, new ColorBand { Above = 10_000, Color = Red });

        Assert.DoesNotContain(All<GroupNode>(plain.Scene.Series), g => g.Class == "rio-color-band");
        Assert.Single(All<PathNode>(plain.Scene.Series), p => p.Class == "rio-line");
        Assert.Single(All<GroupNode>(off.Scene.Series), g => g.Class == "rio-color-band"); // the red range is above the axis
    }

    [Fact]
    public void A_json_spec_takes_colorBands()
    {
        var parsed = ChartDefinitionJson.Parse("""
            { "data": [ { "hour": "08", "ms": 80 }, { "hour": "09", "ms": 140 } ],
              "series": [ { "type": "line", "x": "hour", "y": "ms", "colorBands": [ { "above": 120, "color": "--rio-negative" } ] } ] }
            """);

        Assert.Empty(parsed.Errors);
        var band = Assert.Single(parsed.Definition!.Series[0].ColorBands!);
        Assert.Equal((120.0, Red), (band.Above, band.Color));
        Assert.Contains("ColorBands=\"@(new ColorBand[] { new() { Above = 120, Color = \"--rio-negative\" } })\"",
            ChartRazor.FromDefinition(parsed.Definition!, "Row", "rows"), StringComparison.Ordinal);
    }
}
