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

public class CompactTests
{
    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static IEnumerable<Node> All(Scene scene) => All(scene.Static).Concat(All(scene.Series)).Concat(All(scene.Overlay));

    private static readonly DateTime Start = new(2026, 11, 16);

    /// <summary>The start of each subpath (a dot starts at its top).</summary>
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

    private static SeriesData Temps(params double[] values) =>
        SeriesData.From(values.Select((_, i) => Start.AddHours(i)).ToArray(), values);

    private static ChartLayoutResult Layout(ChartDefinition spec, Dictionary<string, SeriesData> data, double width = 160, double height = 32) =>
        ChartLayout.Compute(spec, data, new LayoutOptions { Width = width, Height = height, Culture = CultureInfo.InvariantCulture });

    private static ChartDefinition Sparkline(SeriesType type = SeriesType.Line, PointHighlight highlight = PointHighlight.None, string? format = null) => new()
    {
        Compact = true,
        Title = "Temperature",
        Y = new AxisOptions { Title = "°C" },
        Series = [new SeriesOptions { Id = "t", Name = "London", Type = type, Highlight = highlight, Format = format }],
    };

    [Fact]
    public void Compact_has_no_text_axes_grid_or_legend_and_fills_the_box()
    {
        var layout = Layout(Sparkline(), new() { ["t"] = Temps(18.2, 19.5, 21, 20.4) });

        var nodes = All(layout.Scene).ToList();
        Assert.DoesNotContain(nodes, n => n is TextNode);
        Assert.DoesNotContain(nodes, n => n.Class is "rio-grid" && n is GroupNode { Children.Count: > 0 });
        Assert.DoesNotContain(nodes, n => n.Class is "rio-baseline" or "rio-legend" or "rio-axis-x");
        Assert.Equal(32, layout.Scene.Height); // not clamped to the normal 50 px minimum
        Assert.Equal(1.5f, layout.Plot.X);
        Assert.Equal(1.5f, layout.Plot.Y);
        Assert.Equal(160 - 3, layout.Plot.Width, 0.01);
        Assert.Equal(32 - 3, layout.Plot.Height, 0.01);
    }

    [Fact]
    public void Compact_y_range_fits_the_data_without_zero_or_nice_ticks()
    {
        var layout = Layout(Sparkline(), new() { ["t"] = Temps(18.2, 19.5, 21, 20.4) });

        Assert.Equal(18.2, layout.YScale.DomainMin, 6);
        Assert.Equal(21, layout.YScale.DomainMax, 6);
        var line = layout.Tooltip.Series[0];
        Assert.Equal(layout.Plot.Bottom, line.Y[0], 0.01); // lowest value on the bottom edge
        Assert.Equal(layout.Plot.Y, line.Y[2], 0.01);      // highest on the top edge
    }

    [Fact]
    public void Compact_areas_fit_the_data_but_bars_keep_zero()
    {
        var area = Layout(Sparkline(SeriesType.Area), new() { ["t"] = Temps(18, 21) });
        var bars = Layout(Sparkline(SeriesType.Bar), new() { ["t"] = Temps(18, 21) });

        Assert.Equal(18, area.YScale.DomainMin, 6);
        Assert.Equal(0, bars.YScale.DomainMin, 6);
        Assert.Equal(21, bars.YScale.DomainMax, 6);
    }

    [Fact]
    public void Equal_values_draw_a_flat_line_in_the_middle()
    {
        var layout = Layout(Sparkline(), new() { ["t"] = Temps(20, 20, 20) });

        var line = layout.Tooltip.Series[0];
        Assert.All(line.Y, y => Assert.Equal(layout.Plot.CenterY, y, 0.01));
    }

    [Fact]
    public void Single_point_gets_a_dot_and_empty_data_draws_nothing()
    {
        var single = Layout(Sparkline(), new() { ["t"] = Temps(20) });
        var empty = Layout(Sparkline(), new() { ["t"] = SeriesData.Empty });

        Assert.Contains(All(single.Scene), n => n.Class == "rio-point");
        Assert.False(empty.HasData);
        Assert.DoesNotContain(All(empty.Scene), n => n is TextNode); // no "No data" text in a table cell
    }

    [Fact]
    public void Annotations_keep_their_shape_but_lose_their_text()
    {
        var spec = Sparkline() with
        {
            Annotations = [new Annotation { Type = AnnotationType.Band, From = 20, To = 23, Label = "Normal" }],
        };

        var layout = Layout(spec, new() { ["t"] = Temps(18, 21) });

        Assert.DoesNotContain(All(layout.Scene), n => n is TextNode);
        Assert.Contains(All(layout.Scene), n => n.Class?.Contains("rio-annotation", StringComparison.Ordinal) == true);
        Assert.Equal(21, layout.YScale.DomainMax, 6); // bands do not widen the range (ExpandsDomain defaults to false)
    }

    [Fact]
    public void Highlight_marks_the_chosen_points_from_the_data()
    {
        var layout = Layout(Sparkline(highlight: PointHighlight.Last | PointHighlight.Max), new() { ["t"] = Temps(18, 22, 19, 20) });

        var dots = All(layout.Scene).OfType<PathNode>().Single(n => n.Class == "rio-highlight");
        var starts = MoveTos(dots.Path);
        Assert.Equal(2, starts.Count);
        var line = layout.Tooltip.Series[0];
        const double radius = 2.5; // Math.Max(2.5, 1.25 * stroke width 2)
        Assert.Contains(starts, p => Math.Abs(p.X - line.X[1]) < 0.01 && Math.Abs(p.Y - (line.Y[1] - radius)) < 0.01); // max
        Assert.Contains(starts, p => Math.Abs(p.X - line.X[3]) < 0.01 && Math.Abs(p.Y - (line.Y[3] - radius)) < 0.01); // last
        Assert.Equal(radius + 1.5, layout.Plot.X, 0.01); // room for the dot and its outline
    }

    [Fact]
    public void Highlight_works_on_normal_charts_and_is_one_dot_when_points_coincide()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "t", Highlight = PointHighlight.Last | PointHighlight.Max }] };

        var layout = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["t"] = Temps(18, 19, 22) }, new LayoutOptions { Width = 600, Height = 300 });

        var dots = All(layout.Scene).OfType<PathNode>().Single(n => n.Class == "rio-highlight");
        Assert.Single(MoveTos(dots.Path));
        Assert.Contains(All(layout.Scene), n => n is TextNode); // still a normal chart
    }

    [Fact]
    public void Compact_summary_gives_range_last_value_and_trend()
    {
        var layout = Layout(Sparkline(format: "0.0 °C"), new() { ["t"] = Temps(18.2, 19.5, 20.1, 20.8) });
        var flat = Layout(Sparkline(format: "0.0 °C") with { Title = null }, new() { ["t"] = Temps(20, 20) });
        var none = Layout(Sparkline(), new() { ["t"] = SeriesData.Empty });

        Assert.Equal("Temperature. London: 18.2 °C to 20.8 °C, last 20.8 °C, rising.", layout.Scene.Description);
        Assert.Equal("London: 20.0 °C, last 20.0 °C, stable.", flat.Scene.Description);
        Assert.Equal("Temperature. No data.", none.Scene.Description);
    }

    [Theory]
    [InlineData("\"last\"", PointHighlight.Last)]
    [InlineData("\"Last, MAX\"", PointHighlight.Last | PointHighlight.Max)]
    [InlineData("[\"min\", \"max\"]", PointHighlight.Min | PointHighlight.Max)]
    public void Json_spec_reads_compact_and_highlight(string highlight, PointHighlight expected)
    {
        var result = ChartDefinitionJson.Parse($$"""{ "compact": true, "series": [ { "type": "line", "x": "time", "y": "temp", "highlight": {{highlight}} } ] }""");

        Assert.Empty(result.Errors);
        Assert.True(result.Definition!.Compact);
        Assert.Equal(expected, result.Definition.Series[0].Highlight);
    }

    [Fact]
    public void Json_spec_explains_an_unknown_highlight()
    {
        var result = ChartDefinitionJson.Parse("""{ "series": [ { "type": "line", "x": "time", "y": "temp", "highlight": "latest" } ] }""");

        var error = Assert.Single(result.Errors);
        Assert.Equal("series[0].highlight", error.Path);
        Assert.Contains("last, first, min, max", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Razor_export_writes_compact_and_highlight()
    {
        var spec = ChartDefinitionJson.Parse("""{ "compact": true, "series": [ { "type": "line", "x": "time", "y": "temp", "highlight": "last, max" } ] }""").Definition!;

        var razor = ChartRazor.FromDefinition(spec, "Reading", "readings");

        Assert.Contains(" Compact=\"true\" Height=\"32\"", razor);
        Assert.Contains(" Highlight=\"PointHighlight.Last | PointHighlight.Max\"", razor);
    }

    [Fact]
    public void Skia_exports_a_compact_chart_at_sparkline_size()
    {
        var spec = Sparkline(highlight: PointHighlight.Last);
        var data = new Dictionary<string, SeriesData> { ["t"] = Temps(18.2, 19.5, 21, 20.4) };

        var png = ChartExport.ToPng(spec, data, new ExportOptions { Width = 160, Height = 32, Scale = 2 });

        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(320, bitmap.Width);
        Assert.Equal(64, bitmap.Height);
    }
    public static TheoryData<SeriesType, double?> ChartsWithoutAxes => new()
    {
        { SeriesType.Pie, null },
        { SeriesType.Pie, 0.6 },
        { SeriesType.RadialBar, null },
        { SeriesType.Radar, null },
        { SeriesType.Treemap, null },
    };

    [Theory]
    [MemberData(nameof(ChartsWithoutAxes))]
    public void Pies_radial_bars_radars_and_treemaps_draw_only_the_shapes_when_compact(SeriesType type, double? innerRadius)
    {
        var spec = new ChartDefinition
        {
            Compact = true,
            Title = "Sales",
            Legend = new LegendOptions { Mode = LegendMode.Show },
            Series = [new SeriesOptions { Id = "s", Name = "Sales", Type = type, InnerRadius = innerRadius }],
        };
        var data = type == SeriesType.Treemap
            ? SeriesData.FromCategories(["Shoes/Running", "Shoes/Hiking", "Jackets/Rain", "Jackets/Down", "Hats/Wool"], [50, 30, 25, 20, 10])
            : SeriesData.FromCategories(["Shoes", "Jackets", "Hats", "Bags", "Socks"], [50, 30, 20, 15, 10]);

        var layout = Layout(spec, new() { ["s"] = data }, width: 40, height: 40);

        var nodes = All(layout.Scene).ToList();
        Assert.True(layout.HasData);
        Assert.DoesNotContain(nodes, n => n is TextNode); // no title, legend, labels, center total or group headers
        Assert.DoesNotContain(nodes, n => n.Class is "rio-legend" or "rio-title");
        Assert.Equal(40, layout.Scene.Width); // not clamped to the normal 50 px minimum
        Assert.Equal(1, layout.Plot.X);
        Assert.Equal(38, layout.Plot.Width, 0.01);
        Assert.False(string.IsNullOrEmpty(layout.Scene.Description));
    }

    [Fact]
    public void A_compact_pie_fills_the_box_and_keeps_its_tooltip()
    {
        var spec = new ChartDefinition { Compact = true, Series = [new SeriesOptions { Id = "s", Type = SeriesType.Pie }] };

        var layout = Layout(spec, new() { ["s"] = SeriesData.FromCategories(["Shoes", "Jackets"], [60, 40]) }, width: 60, height: 32);

        var slices = layout.Tooltip.Slices;
        Assert.Equal(2, slices.Count);
        Assert.Equal(15, slices[0].R1, 0.01); // (32 - 2) / 2
        Assert.Equal(30, slices[0].Cx, 0.01);
        Assert.Equal(16, slices[0].Cy, 0.01);
    }

    [Fact]
    public void Small_pies_become_compact_and_empty_compact_pies_draw_nothing()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "s", Type = SeriesType.Pie }] };
        var small = Layout(spec, new() { ["s"] = SeriesData.FromCategories(["Shoes", "Jackets", "Hats"], [50, 30, 20]) }, width: 100, height: 100);
        var empty = Layout(spec with { Compact = true }, new() { ["s"] = SeriesData.Empty }, width: 40, height: 40);

        Assert.DoesNotContain(All(small.Scene), n => n is TextNode); // auto-compact below 120 px: no slice labels
        Assert.False(empty.HasData);
        Assert.DoesNotContain(All(empty.Scene), n => n is TextNode);
    }
}
