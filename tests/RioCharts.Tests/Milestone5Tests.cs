using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Scene;
using RioCharts.Skia;
using SkiaSharp;

namespace RioCharts.Tests;

public class Milestone5Tests
{
    private static readonly ExportOptions Options = new() { Width = 600, Height = 300, Scale = 2, Culture = CultureInfo.InvariantCulture };

    /// <summary>Set RIOCHARTS_EXPORT_DIR to look at the images the tests render.</summary>
    private static void Save(string name, byte[] bytes)
    {
        if (Environment.GetEnvironmentVariable("RIOCHARTS_EXPORT_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name), bytes);
        }
    }

    private static (ChartSpec Spec, Dictionary<string, SeriesData> Data) TemperatureChart()
    {
        var start = new DateTime(2026, 11, 16);
        var times = Enumerable.Range(0, 96).Select(i => start.AddMinutes(30 * i)).ToArray();
        var london = times.Select((t, i) => 19 + 1.5 * Math.Sin(i / 48.0 * 2 * Math.PI)).ToArray();
        var rain = times.Select((t, i) => 14 - 9 * Math.Sin(i / 48.0 * 2 * Math.PI)).ToArray();
        var spec = new ChartSpec
        {
            Title = "Weather station",
            Y = new AxisSpec { Title = "°C" },
            YAxes = [new AxisSpec { Id = "mm", Title = "mm" }],
            Series =
            [
                new SeriesSpec { Id = "rain", Name = "Rain", Type = SeriesType.Area, YAxisId = "mm" },
                new SeriesSpec { Id = "london", Name = "London", Curve = CurveType.Monotone },
            ],
            Annotations = [new AnnotationSpec { Value = 19, Label = "Average" }],
        };
        return (spec, new() { ["london"] = SeriesData.From(times, london), ["rain"] = SeriesData.From(times, rain) });
    }

    [Fact]
    public void Png_has_the_requested_size_and_draws_the_series_colors()
    {
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a", Type = SeriesType.Bar }] };
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.FromCategories(["A", "B", "C"], [3.0, 5, 4]) };

        var png = ChartExport.ToPng(spec, data, Options);
        Save("bars.png", png);
        using var bitmap = SKBitmap.Decode(png);
        var layout = ChartExport.Layout(spec, data, Options);
        var bar = layout.Tooltip.Series[0];

        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png.Take(4).Select(b => (int)b));
        Assert.Equal(1200, bitmap.Width);
        Assert.Equal(600, bitmap.Height);
        Assert.Equal(SKColor.Parse("#ffffff"), bitmap.GetPixel(2, 2)); // the surface background
        var inside = bitmap.GetPixel((int)((bar.X[1] + bar.BarOffset) * 2), (int)((bar.Y[1] + layout.Plot.Bottom) / 2 * 2));
        Assert.Equal(SKColor.Parse("#4269d0"), inside); // series 1 of the light theme
    }

    [Fact]
    public void Dark_theme_and_overrides_change_the_colors()
    {
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a", Type = SeriesType.Bar }] };
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.FromCategories(["A"], [1.0]) };
        var options = Options with { Theme = ExportTheme.Dark.With(ThemeTokens.Series(0), "#0b6e4f") };

        var svg = ChartExport.ToSvg(spec, data, options);

        Assert.Contains("fill=\"#16191d\"", svg); // dark surface
        Assert.Contains("#0b6e4f", svg);
        Assert.DoesNotContain("var(", svg);
        Assert.Contains("var(--rio-series-1, #0b6e4f)", ChartExport.ToSvg(spec, data, options with { CssVariables = true }));
    }

    [Fact]
    public void Svg_is_a_standalone_document_with_escaped_text()
    {
        var (spec, data) = TemperatureChart();
        spec = spec with { Title = "London <Station 1> & Toronto" };

        var svg = ChartExport.ToSvg(spec, data, Options);
        Save("temperature.svg", System.Text.Encoding.UTF8.GetBytes(svg));
        var doc = System.Xml.Linq.XDocument.Parse(svg); // well-formed XML

        Assert.Equal("svg", doc.Root!.Name.LocalName);
        Assert.Contains("London &lt;Station 1&gt; &amp; Toronto", svg);
        Assert.Contains("<clipPath", svg);
        Assert.Contains("stroke-dasharray=\"4 3\"", svg); // the reference line
    }

    [Fact]
    public void Every_chart_type_renders_to_png()
    {
        var (temperature, temperatureData) = TemperatureChart();
        Save("temperature.png", ChartExport.ToPng(temperature, temperatureData, Options));

        var shares = SeriesData.FromCategories(["Industry", "Homes", "Transport", "Offices", "Shops"], [412.0, 236, 148, 97, 31]);
        var donut = new ChartSpec { Title = "Energy use", Series = [new SeriesSpec { Id = "p", Type = SeriesType.Pie, InnerRadius = 0.6 }] };
        var donutPng = ChartExport.ToPng(donut, new Dictionary<string, SeriesData> { ["p"] = shares }, Options);
        Save("donut.png", donutPng);

        var radial = new ChartSpec { Series = [new SeriesSpec { Id = "r", Type = SeriesType.RadialBar, Max = 100 }] };
        var radialPng = ChartExport.ToPng(radial, new Dictionary<string, SeriesData> { ["r"] = SeriesData.FromCategories(["North", "South", "East"], [87.0, 41, 66]) }, Options);
        Save("radial.png", radialPng);

        var empty = ChartExport.ToPng(new ChartSpec { Series = [new SeriesSpec { Id = "e" }] }, new Dictionary<string, SeriesData>(), Options);
        Save("empty.png", empty);

        Assert.All([donutPng, radialPng, empty], png => Assert.NotNull(SKBitmap.Decode(png)));
    }

    [Fact]
    public void Inside_slice_labels_stay_within_a_thin_donut_ring()
    {
        static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];
        var shares = SeriesData.FromCategories(["Industry", "Homes", "Transport", "Offices", "Shops", "Farms", "Rail"], [412.0, 236, 148, 97, 31, 9, 6]);
        var donut = new ChartSpec { Legend = new LegendSpec { Mode = LegendMode.Hide }, Series = [new SeriesSpec { Id = "p", Type = SeriesType.Pie, InnerRadius = 0.6 }] };
        var measurer = new SkiaTextMeasurer(new SkiaFonts("Segoe UI, Arial, sans-serif"));
        var inside = 0;

        // Small tiles, where the ring is barely thicker than a label is tall.
        for (var height = 140; height <= 200; height += 5)
        {
            var layout = ChartExport.Layout(donut, new Dictionary<string, SeriesData> { ["p"] = shares }, Options with { Width = 360, Height = height });
            var labels = new[] { layout.Scene.Static, layout.Scene.Series, layout.Scene.Overlay }.SelectMany(All).OfType<TextNode>()
                .Where(t => t.Style.Name == "data-label-inverse").ToList();
            var ring = layout.Tooltip.Slices[0];
            inside += labels.Count;
            Assert.All(labels, t =>
            {
                var halfW = measurer.Measure(t.Text, 11) / 2;
                foreach (var (dx, dy) in new[] { (-halfW, -4.0), (halfW, -4.0), (-halfW, 4.0), (halfW, 4.0) })
                {
                    var d = Math.Sqrt(Math.Pow(t.At.X + dx - ring.Cx, 2) + Math.Pow(t.At.Y + dy - ring.Cy, 2));
                    Assert.InRange(d, ring.R0, ring.R1);
                }
            });
        }

        Assert.True(inside > 0); // larger tiles still keep labels inside
    }

    [Fact]
    public void Leader_lines_of_a_label_stack_never_cross()
    {
        static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];
        static bool Cross((float X, float Y) a, (float X, float Y) b, (float X, float Y) c, (float X, float Y) d)
        {
            static double Side((float X, float Y) p, (float X, float Y) q, (float X, float Y) r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
            return Side(a, b, c) * Side(a, b, d) < 0 && Side(c, d, a) * Side(c, d, b) < 0;
        }

        // Small slices at the end of the sweep share the top of the left side; their labels stack downward.
        var shares = SeriesData.FromCategories(["Industry", "Homes", "Transport", "Offices", "Shops", "Farms", "Rail"], [412.0, 236, 148, 97, 31, 9, 6]);
        foreach (var (inner, height) in new[] { 0, 0.6 }.SelectMany(inner => new[] { 150, 170, 220, 300 }.Select(h => (inner, h))))
        {
            var donut = new ChartSpec { Legend = new LegendSpec { Mode = LegendMode.Hide }, Series = [new SeriesSpec { Id = "p", Type = SeriesType.Pie, InnerRadius = inner }] };
            var layout = ChartExport.Layout(donut, new Dictionary<string, SeriesData> { ["p"] = shares }, Options with { Width = 360, Height = height });
            var leaders = new[] { layout.Scene.Static, layout.Scene.Series, layout.Scene.Overlay }.SelectMany(All).OfType<PathNode>()
                .Where(p => p.Class == "rio-leader")
                .Select(p => Enumerable.Range(0, p.Path.Coords.Count / 2).Select(i => (p.Path.Coords[2 * i], p.Path.Coords[2 * i + 1])).ToList())
                .ToList();

            Assert.True(leaders.Count >= 3);
            for (var i = 0; i < leaders.Count; i++)
            {
                for (var j = i + 1; j < leaders.Count; j++)
                {
                    for (var s = 1; s < leaders[i].Count; s++)
                    {
                        for (var t = 1; t < leaders[j].Count; t++)
                        {
                            Assert.False(Cross(leaders[i][s - 1], leaders[i][s], leaders[j][t - 1], leaders[j][t]), $"leaders {i} and {j} cross at height {height}, inner radius {inner}");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void Skia_measurer_gives_real_text_widths()
    {
        var measurer = new SkiaTextMeasurer(new SkiaFonts("Segoe UI, Arial, sans-serif"));

        var narrow = measurer.Measure("iiii", 12);
        var wide = measurer.Measure("WWWW", 12);

        Assert.True(narrow > 0);
        Assert.True(wide > narrow * 2);
        // Width scales with font size. Not exactly 2x: on machines without these fonts (Linux CI)
        // Skia falls back to a hinted font whose advances round differently at 12 and 24 px.
        var large = measurer.Measure("London", 24);
        var small = measurer.Measure("London", 12);
        Assert.InRange(large / (small * 2), 0.9, 1.1);
    }

    [Fact]
    public void Arcs_become_skia_paths_with_the_same_bounds()
    {
        var circle = new PathBuilder().Arc(50, 50, 20, 0, Math.PI * 2).Close().Build();

        using var path = SkiaRenderer.ToSkPath(circle);

        Assert.Equal(30, path.Bounds.Left, 0.5);
        Assert.Equal(70, path.Bounds.Right, 0.5);
        Assert.Equal(30, path.Bounds.Top, 0.5);
        Assert.Equal(70, path.Bounds.Bottom, 0.5);
    }
}
