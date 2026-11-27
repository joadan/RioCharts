using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Series;
using RioCharts.Skia;

namespace RioCharts.Tests;

/// <summary>Dashed and dotted lines (<see cref="SeriesOptions.Dash"/>) on line and area series, their legend swatch, JSON and Razor.</summary>
public class LineDashTests
{
    private static readonly LayoutOptions Options = new() { Width = 640, Height = 320, Culture = CultureInfo.InvariantCulture };

    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static readonly SeriesData Data = SeriesData.From([0.0, 1, 2, 3, 4], [3.0, 5, 4, 6, 5]);

    private static ChartLayoutResult Layout(params SeriesOptions[] series) =>
        ChartLayout.Compute(new ChartDefinition { Series = series }, series.ToDictionary(s => s.Id, _ => Data), Options);

    private static Style LineStyle(ChartLayoutResult r) =>
        r.Scene.Styles.Get(All(r.Scene.Series).OfType<PathNode>().Single(p => p.Class == "rio-line").Style);

    private static List<Style> LineStyles(ChartLayoutResult r) =>
        All(r.Scene.Series).OfType<PathNode>().Where(p => p.Class == "rio-line").Select(p => r.Scene.Styles.Get(p.Style)).ToList();

    private static Style Swatch(ChartLayoutResult r, string id) =>
        r.Scene.Styles.Get(All(r.Scene.Static).OfType<PathNode>().Single(p => p.Class == "rio-swatch rio-swatch-line" && p.Style.Name == "swatch-" + id).Style);

    [Fact]
    public void Lines_are_solid_by_default()
    {
        Assert.Equal(LineDash.Solid, new SeriesOptions().Dash);
        Assert.Null(LineStyle(Layout(new SeriesOptions { Id = "s" })).DashArray);
    }

    [Theory]
    [InlineData(LineDash.Dashed, 2.0, "6 6")]
    [InlineData(LineDash.Dotted, 2.0, "0 5")]
    [InlineData(LineDash.Dashed, 3.0, "9 9")]
    [InlineData(LineDash.Dotted, 1.5, "0 3.75")]
    public void The_pattern_scales_with_the_line_width(LineDash dash, double width, string expected)
    {
        Assert.Equal(expected, SeriesBuildContext.DashArray(dash, width));
        Assert.Equal(expected, LineStyle(Layout(new SeriesOptions { Id = "s", Dash = dash, StrokeWidth = width })).DashArray);
    }

    [Fact]
    public void An_area_dashes_its_top_line_and_keeps_the_fill()
    {
        var r = Layout(new SeriesOptions { Id = "s", Type = SeriesType.Area, Dash = LineDash.Dashed });

        Assert.Equal("6 6", LineStyle(r).DashArray);
        var area = All(r.Scene.Series).OfType<PathNode>().Single(p => p.Class == "rio-area");
        Assert.Null(r.Scene.Styles.Get(area.Style).DashArray);
    }

    [Fact]
    public void A_solid_and_a_dashed_line_in_the_same_color_keep_their_own_styles()
    {
        // Color bands share one style per color: a dashed line must not take over a solid line's style.
        var bands = new List<ColorBand> { new() { Above = 4.5, Color = "#c0392b" } };
        var r = Layout(
            new SeriesOptions { Id = "a", ColorBands = bands },
            new SeriesOptions { Id = "b", ColorBands = bands, Dash = LineDash.Dotted });

        var styles = LineStyles(r);
        Assert.Contains(styles, s => s.DashArray is null);
        Assert.Contains(styles, s => s.DashArray == "0 5");
    }

    [Fact]
    public void The_legend_swatch_shows_the_pattern()
    {
        var r = Layout(
            new SeriesOptions { Id = "actual", Name = "Actual" },
            new SeriesOptions { Id = "forecast", Name = "Forecast", Dash = LineDash.Dashed },
            new SeriesOptions { Id = "target", Name = "Target", Dash = LineDash.Dotted });

        Assert.Null(Swatch(r, "actual").DashArray);
        Assert.Equal("4 4", Swatch(r, "forecast").DashArray);
        Assert.Equal("0 4", Swatch(r, "target").DashArray);
    }

    [Fact]
    public void The_svg_export_writes_the_dasharray()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "s", Dash = LineDash.Dashed }] };
        var svg = ChartExport.ToSvg(spec, new Dictionary<string, SeriesData> { ["s"] = Data },
            new ExportOptions { Width = 400, Height = 200, Culture = CultureInfo.InvariantCulture });

        Assert.Contains("stroke-dasharray=\"6 6\"", svg, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dashed", LineDash.Dashed)]
    [InlineData("Dash", LineDash.Dashed)]
    [InlineData("dots", LineDash.Dotted)]
    [InlineData("dotted", LineDash.Dotted)]
    [InlineData("solid", LineDash.Solid)]
    public void Json_reads_the_dash_and_its_synonyms(string word, LineDash expected)
    {
        var result = ChartDefinitionJson.Parse($$"""{ "series": [ { "type": "line", "x": "t", "y": "v", "dash": "{{word}}" } ] }""");

        Assert.Empty(result.Errors);
        Assert.Equal(expected, result.Definition!.Series[0].Dash);
    }

    [Fact]
    public void Json_rejects_an_unknown_dash_with_the_valid_words()
    {
        var result = ChartDefinitionJson.Parse("""{ "series": [ { "type": "line", "x": "t", "y": "v", "dash": "wavy" } ] }""");

        var error = Assert.Single(result.Errors);
        Assert.Equal("series[0].dash", error.Path);
        Assert.Contains("dashed", error.Message, StringComparison.Ordinal);
        Assert.Contains("dotted", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_round_trips_the_dash_and_leaves_out_solid()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "a" }, new SeriesOptions { Id = "b", Dash = LineDash.Dotted }] };
        var json = ChartDefinitionJson.Serialize(spec);

        Assert.Contains("\"dotted\"", json, StringComparison.Ordinal);
        Assert.Equal(1, json.Split("\"dash\"").Length - 1); // the solid default is left out
        Assert.Equal(LineDash.Dotted, ChartDefinitionJson.Parse(json).Definition!.Series[1].Dash);
    }

    [Fact]
    public void Razor_writes_the_dash_for_line_and_area_series()
    {
        var spec = ChartDefinitionJson.Parse("""
            { "series": [ { "type": "line", "x": "time", "y": "actual" },
                          { "type": "line", "x": "time", "y": "forecast", "dash": "dashed" },
                          { "type": "area", "x": "time", "y": "plan", "dash": "dotted" } ] }
            """).Definition!;

        var razor = ChartRazor.FromDefinition(spec, "Row", "rows");

        Assert.Contains("Dash=\"LineDash.Dashed\"", razor, StringComparison.Ordinal);
        Assert.Contains("Dash=\"LineDash.Dotted\"", razor, StringComparison.Ordinal);
        Assert.Equal(2, razor.Split("Dash=").Length - 1);
    }
}
