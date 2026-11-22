using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Specs;

namespace RioCharts.Tests;

public class AutoCompactTests
{
    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static IEnumerable<Node> All(Scene scene) => All(scene.Static).Concat(All(scene.Series)).Concat(All(scene.Overlay));

    private static readonly DateTime Start = new(2026, 11, 16);

    private static SeriesData Temps(double offset) =>
        SeriesData.From(Enumerable.Range(0, 24).Select(i => Start.AddHours(i)).ToArray(), Enumerable.Range(0, 24).Select(i => offset + Math.Sin(i / 3.0)).ToArray());

    private static readonly ChartSpec Full = new()
    {
        Title = "Weather stations",
        X = new AxisSpec { Title = "Time" },
        Y = new AxisSpec { Title = "°C" },
        Series = [new SeriesSpec { Id = "s", Name = "London" }, new SeriesSpec { Id = "e", Name = "Toronto" }],
    };

    private static readonly Dictionary<string, SeriesData> Data = new() { ["s"] = Temps(19), ["e"] = Temps(22) };

    private static ChartLayoutResult Layout(double width, double height, ChartSpec? spec = null, double font = 12) =>
        ChartLayout.Compute(spec ?? Full, Data, new LayoutOptions { Width = width, Height = height, FontSize = font, Culture = CultureInfo.InvariantCulture });

    /// <summary>What the chart shows: title, legend, x/y axis titles, x/y axis labels.</summary>
    private static string Shown(ChartLayoutResult layout)
    {
        var nodes = All(layout.Scene).ToList();
        bool Has(Func<Node, bool> f) => nodes.Any(f);
        var parts = new List<string>();
        if (Has(n => n.Class == "rio-title")) parts.Add("title");
        if (Has(n => n.Class == "rio-legend")) parts.Add("legend");
        if (Has(n => n is TextNode { Class: "rio-axis-title", Text: "Time" })) parts.Add("x-title");
        if (Has(n => n is TextNode { Class: "rio-axis-title", Text: "°C" })) parts.Add("y-title");
        if (Has(n => n is GroupNode { Class: "rio-axis-x", Children.Count: > 0 })) parts.Add("x-labels");
        if (Has(n => n is GroupNode { Children.Count: > 0 } g && g.Class?.StartsWith("rio-axis-y", StringComparison.Ordinal) == true)) parts.Add("y-labels");
        return string.Join(" ", parts);
    }

    [Theory]
    [InlineData(600, 300, "title legend x-title y-title x-labels y-labels")]
    [InlineData(600, 170, "title legend y-title x-labels y-labels")] // the x axis title goes first
    [InlineData(600, 140, "title x-labels y-labels")]                // then the legend and the y title
    [InlineData(600, 100, "x-labels y-labels")]                      // then the chart title
    [InlineData(600, 80, "y-labels")]                                // then the x labels
    [InlineData(180, 300, "title x-title x-labels")]                 // narrow: no y labels, y title or legend
    [InlineData(100, 300, "")]                                       // compact
    [InlineData(600, 50, "")]
    public void Chrome_is_shed_step_by_step_as_the_chart_gets_smaller(double width, double height, string expected)
    {
        Assert.Equal(expected, Shown(Layout(width, height)));
    }

    [Fact]
    public void Very_small_charts_are_laid_out_as_compact_charts()
    {
        var layout = Layout(100, 40);

        Assert.DoesNotContain(All(layout.Scene), n => n is TextNode);
        Assert.Equal(40, layout.Scene.Height); // not clamped to 50
        Assert.True(layout.Plot.Height > 30);  // the series fill the box
        Assert.StartsWith("Weather stations. London:", layout.Scene.Description, StringComparison.Ordinal); // the short summary
    }

    [Fact]
    public void Turned_off_everything_stays_at_any_size()
    {
        Assert.Equal("title legend x-title y-title x-labels y-labels", Shown(Layout(600, 120, Full with { AutoCompact = false })));
        Assert.Contains("x-labels", Shown(Layout(100, 60, Full with { AutoCompact = false })));
    }

    [Fact]
    public void A_legend_set_to_show_stays()
    {
        Assert.Contains("legend", Shown(Layout(600, 120, Full with { Legend = new LegendSpec { Mode = LegendMode.Show } })));
    }

    [Fact]
    public void A_right_legend_moves_to_the_top_on_narrow_charts()
    {
        var spec = Full with { Legend = new LegendSpec { Position = LegendPosition.Right } };

        var wide = Layout(800, 300, spec);
        var narrow = Layout(350, 300, spec);

        Assert.True(wide.Plot.Right < 800 - 60);   // room for the legend on the right
        Assert.True(narrow.Plot.Right > 350 - 30); // the legend is above the plot
        Assert.Contains("legend", Shown(narrow));
    }

    [Fact]
    public void A_legend_taller_than_a_third_of_the_chart_is_left_out()
    {
        var series = Enumerable.Range(0, 12).Select(i => new SeriesSpec { Id = $"s{i}", Name = $"Weather station {i + 1}" }).ToList();
        var data = series.ToDictionary(s => s.Id, s => Temps(18));

        var layout = ChartLayout.Compute(new ChartSpec { Series = series }, data, new LayoutOptions { Width = 320, Height = 260 });

        Assert.DoesNotContain(All(layout.Scene), n => n.Class == "rio-legend");
    }

    [Fact]
    public void Sizes_scale_with_the_font()
    {
        Assert.Contains("legend", Shown(Layout(600, 170)));
        Assert.DoesNotContain("legend", Shown(Layout(600, 170, font: 16))); // 160 px at 12 px is 213 px at 16 px
    }

    [Fact]
    public void Small_pies_leave_out_the_legend_and_then_the_title()
    {
        var spec = new ChartSpec { Title = "Sales", Series = [new SeriesSpec { Id = "p", Type = SeriesType.Pie }] };
        var data = new Dictionary<string, SeriesData> { ["p"] = SeriesData.FromCategories(["Shoes", "Jackets", "Hats"], [50, 30, 20]) };

        var big = ChartLayout.Compute(spec, data, new LayoutOptions { Width = 400, Height = 300 });
        var small = ChartLayout.Compute(spec, data, new LayoutOptions { Width = 180, Height = 140 });
        var tiny = ChartLayout.Compute(spec, data, new LayoutOptions { Width = 110, Height = 110 });

        Assert.Contains(All(big.Scene), n => n.Class == "rio-legend");
        Assert.DoesNotContain(All(small.Scene), n => n.Class == "rio-legend");
        Assert.Contains(All(small.Scene), n => n.Class == "rio-title");
        Assert.DoesNotContain(All(tiny.Scene), n => n.Class == "rio-title");
    }

    [Fact]
    public void Json_and_razor_carry_the_setting()
    {
        var parsed = ChartSpecJson.Parse("""{ "autoCompact": false, "series": [ { "x": "t", "y": "v" } ] }""").Spec!;

        Assert.False(parsed.AutoCompact);
        Assert.True(ChartSpecJson.Parse("""{ "series": [ { "x": "t", "y": "v" } ] }""").Spec!.AutoCompact); // default on
        Assert.DoesNotContain("autoCompact", ChartSpecJson.Serialize(new ChartSpec()), StringComparison.Ordinal);
        Assert.Contains("\"autoCompact\": false", ChartSpecJson.Serialize(parsed), StringComparison.Ordinal);
        Assert.Contains(" AutoCompact=\"false\"", ChartRazor.FromSpec(parsed, "Reading", "readings"), StringComparison.Ordinal);
    }
}
