using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Skia;

namespace RioCharts.Demo;

/// <summary>
/// Server-side export: the same chart model as the Blazor components, rendered to PNG or SVG
/// without a browser (e.g. for an email, a report or a chat message).
/// </summary>
public static class ExportEndpoints
{
    public static void MapChartExport(this IEndpointRouteBuilder app)
    {
        // /export/ahu.png?theme=dark&width=640&height=320
        app.MapGet("/export/ahu.{format}", (string format, string? theme, int? width, int? height) =>
        {
            var (spec, data) = AirHandlingUnit();
            var options = new ExportOptions
            {
                Width = Math.Clamp(width ?? 720, 200, 2000),
                Height = Math.Clamp(height ?? 320, 120, 1200),
                Theme = theme == "dark" ? ExportTheme.Dark : ExportTheme.Light,
                Culture = CultureInfo.GetCultureInfo("sv-SE"),
            };

            return format switch
            {
                "png" => Results.File(ChartExport.ToPng(spec, data, options), "image/png"),
                "svg" => Results.Text(ChartExport.ToSvg(spec, data, options), "image/svg+xml"),
                _ => Results.NotFound(),
            };
        });
    }

    private static (ChartSpec Spec, Dictionary<string, SeriesData> Data) AirHandlingUnit()
    {
        var start = new DateTime(2026, 11, 16);
        var times = Enumerable.Range(0, 2 * 96).Select(i => start.AddMinutes(15 * i)).ToArray();
        double Cycle(int i) => Math.Sin((times[i].Hour + times[i].Minute / 60.0 - 9) / 24 * 2 * Math.PI);
        var supply = times.Select((_, i) => Math.Round(19 + 1.5 * Cycle(i), 1)).ToArray();
        var extract = times.Select((_, i) => Math.Round(22 + 0.8 * Cycle(i), 1)).ToArray();
        var outdoor = times.Select((_, i) => Math.Round(4 + 5 * Cycle(i), 1)).ToArray();

        var spec = new ChartSpec
        {
            Title = "Air handling unit (rendered on the server)",
            Y = new AxisSpec { Title = "°C" },
            Series =
            [
                new SeriesSpec { Id = "supply", Name = "Supply", Curve = CurveType.Monotone },
                new SeriesSpec { Id = "extract", Name = "Extract", Curve = CurveType.Monotone },
                new SeriesSpec { Id = "outdoor", Name = "Outdoor" },
            ],
            Annotations =
            [
                new AnnotationSpec { Type = AnnotationType.Band, From = 20, To = 24, Label = "Comfort zone" },
                new AnnotationSpec { Value = 21, Label = "Setpoint 21 °C" },
            ],
        };

        return (spec, new()
        {
            ["supply"] = SeriesData.From(times, supply),
            ["extract"] = SeriesData.From(times, extract),
            ["outdoor"] = SeriesData.From(times, outdoor),
        });
    }
}
