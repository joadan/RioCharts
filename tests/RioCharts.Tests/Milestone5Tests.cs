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
        var supply = times.Select((t, i) => 19 + 1.5 * Math.Sin(i / 48.0 * 2 * Math.PI)).ToArray();
        var energy = times.Select((t, i) => 14 - 9 * Math.Sin(i / 48.0 * 2 * Math.PI)).ToArray();
        var spec = new ChartSpec
        {
            Title = "Air handling unit",
            Y = new AxisSpec { Title = "°C" },
            YAxes = [new AxisSpec { Id = "kwh", Title = "kWh" }],
            Series =
            [
                new SeriesSpec { Id = "energy", Name = "Energy", Type = SeriesType.Area, YAxisId = "kwh" },
                new SeriesSpec { Id = "supply", Name = "Supply", Curve = CurveType.Monotone },
            ],
            Annotations = [new AnnotationSpec { Value = 19, Label = "Setpoint" }],
        };
        return (spec, new() { ["supply"] = SeriesData.From(times, supply), ["energy"] = SeriesData.From(times, energy) });
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
        spec = spec with { Title = "Supply <AHU 1> & extract" };

        var svg = ChartExport.ToSvg(spec, data, Options);
        Save("temperature.svg", System.Text.Encoding.UTF8.GetBytes(svg));
        var doc = System.Xml.Linq.XDocument.Parse(svg); // well-formed XML

        Assert.Equal("svg", doc.Root!.Name.LocalName);
        Assert.Contains("Supply &lt;AHU 1&gt; &amp; extract", svg);
        Assert.Contains("<clipPath", svg);
        Assert.Contains("stroke-dasharray=\"4 3\"", svg); // the setpoint line
    }

    [Fact]
    public void Every_chart_type_renders_to_png()
    {
        var (temperature, temperatureData) = TemperatureChart();
        Save("temperature.png", ChartExport.ToPng(temperature, temperatureData, Options));

        var shares = SeriesData.FromCategories(["Heating", "Ventilation", "Cooling", "Lighting", "Pumps"], [412.0, 236, 148, 97, 31]);
        var donut = new ChartSpec { Title = "Energy use", Series = [new SeriesSpec { Id = "p", Type = SeriesType.Pie, InnerRadius = 0.6 }] };
        var donutPng = ChartExport.ToPng(donut, new Dictionary<string, SeriesData> { ["p"] = shares }, Options);
        Save("donut.png", donutPng);

        var radial = new ChartSpec { Series = [new SeriesSpec { Id = "r", Type = SeriesType.RadialBar, Max = 100 }] };
        var radialPng = ChartExport.ToPng(radial, new Dictionary<string, SeriesData> { ["r"] = SeriesData.FromCategories(["Fan", "Filter", "Pump"], [87.0, 41, 66]) }, Options);
        Save("radial.png", radialPng);

        var empty = ChartExport.ToPng(new ChartSpec { Series = [new SeriesSpec { Id = "e" }] }, new Dictionary<string, SeriesData>(), Options);
        Save("empty.png", empty);

        Assert.All([donutPng, radialPng, empty], png => Assert.NotNull(SKBitmap.Decode(png)));
    }

    [Fact]
    public void Skia_measurer_gives_real_text_widths()
    {
        var measurer = new SkiaTextMeasurer(new SkiaFonts("Segoe UI, Arial, sans-serif"));

        var narrow = measurer.Measure("iiii", 12);
        var wide = measurer.Measure("WWWW", 12);

        Assert.True(narrow > 0);
        Assert.True(wide > narrow * 2);
        Assert.Equal(measurer.Measure("Supply", 24), measurer.Measure("Supply", 12) * 2, 1);
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
