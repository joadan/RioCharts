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
        // /export/traffic.png?theme=dark&width=640&height=320
        app.MapGet("/export/traffic.{format}", (string format, string? theme, int? width, int? height) =>
        {
            var (spec, data) = Traffic();
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

    private static (ChartDefinition Spec, Dictionary<string, SeriesData> Data) Traffic()
    {
        var start = new DateTime(2026, 11, 16);
        var times = Enumerable.Range(0, 2 * 96).Select(i => start.AddMinutes(15 * i)).ToArray();
        double Cycle(int i) => Math.Sin((times[i].Hour + times[i].Minute / 60.0 - 14) / 24 * 2 * Math.PI);
        var web = times.Select((_, i) => Math.Round(150 + 110 * Cycle(i))).ToArray();
        var app = times.Select((_, i) => Math.Round(98 + 75 * Cycle(i))).ToArray();
        var marketplace = times.Select((_, i) => Math.Round(55 + 35 * Cycle(i))).ToArray();

        var spec = new ChartDefinition
        {
            Title = "Sessions per channel (rendered on the server)",
            Y = new AxisOptions { Title = "Sessions" },
            Series =
            [
                new SeriesOptions { Id = "web", Name = "Web", Curve = CurveType.Monotone },
                new SeriesOptions { Id = "app", Name = "App", Curve = CurveType.Monotone },
                new SeriesOptions { Id = "marketplace", Name = "Marketplace" },
            ],
            Annotations =
            [
                new Annotation { Type = AnnotationType.Band, From = 200, To = 260, Label = "High traffic" },
                new Annotation { Value = 150, Label = "Campaign goal 150" },
            ],
        };

        return (spec, new()
        {
            ["web"] = SeriesData.From(times, web),
            ["app"] = SeriesData.From(times, app),
            ["marketplace"] = SeriesData.From(times, marketplace),
        });
    }
}
