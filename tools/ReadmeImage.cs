#:project ../src/RioCharts.Skia/RioCharts.Skia.csproj
#:property PublishAot=false

// The picture at the top of README.md: four charts drawn by RioCharts.Skia on cards, in a light
// and a dark version (the README shows the one matching the reader's GitHub theme, with the
// #gh-light-mode-only and #gh-dark-mode-only URL suffixes). Run from the repository root:
//   dotnet run tools/ReadmeImage.cs
// It writes assets/readme-charts-light.png and assets/readme-charts-dark.png. Uses Segoe UI (Windows).

using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Scene;
using RioCharts.Skia;
using SkiaSharp;

const int ChartWidth = 460, ChartHeight = 260, Pad = 12, Gap = 16, Scale = 2;
var culture = CultureInfo.GetCultureInfo("en-US");

// 1. Sessions per day over two weeks, against a goal.
var days = Enumerable.Range(0, 14).Select(d => new DateTime(2026, 3, 2).AddDays(d)).ToArray();
var sessions = (new ChartDefinition
{
    Title = "Sessions per day",
    Series =
    [
        new SeriesOptions { Id = "web", Name = "Web", Type = SeriesType.Area, Curve = CurveType.Monotone },
        new SeriesOptions { Id = "app", Name = "App", Type = SeriesType.Line, Curve = CurveType.Monotone, StrokeWidth = 2.5 },
    ],
    Annotations = [new Annotation { Value = 1000.0, Label = "Goal" }],
}, new Dictionary<string, SeriesData>
{
    ["web"] = SeriesData.From(days, [820, 880, 940, 910, 1010, 760, 700, 960, 1040, 1120, 1080, 1210, 900, 860]),
    ["app"] = SeriesData.From(days, [410, 450, 470, 500, 540, 610, 640, 560, 600, 650, 690, 720, 780, 820]),
});

// 2. Orders per quarter, stacked by channel.
string[] quarters = ["Q1", "Q2", "Q3", "Q4"];
var orders = (new ChartDefinition
{
    Title = "Orders per quarter",
    Series =
    [
        new SeriesOptions { Id = "shop", Name = "Web shop", Type = SeriesType.Bar, Stack = "o" },
        new SeriesOptions { Id = "store", Name = "Stores", Type = SeriesType.Bar, Stack = "o" },
        new SeriesOptions { Id = "market", Name = "Marketplaces", Type = SeriesType.Bar, Stack = "o" },
    ],
}, new Dictionary<string, SeriesData>
{
    ["shop"] = SeriesData.FromCategories(quarters, [4200, 4800, 5100, 6900]),
    ["store"] = SeriesData.FromCategories(quarters, [3100, 3300, 2900, 4100]),
    ["market"] = SeriesData.FromCategories(quarters, [1500, 1700, 2200, 2600]),
});

// 3. Revenue by channel as a donut.
var revenue = (new ChartDefinition
{
    Title = "Revenue by channel",
    Series = [new SeriesOptions { Id = "r", Type = SeriesType.Pie, InnerRadius = 0.6, Format = "0 k€" }],
}, new Dictionary<string, SeriesData>
{
    ["r"] = SeriesData.FromCategories(["Web shop", "Stores", "Marketplaces", "Phone"], [420, 310, 150, 40]),
});

// 4. Sessions per hour and weekday as a heatmap.
string[] weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
var hours = Enumerable.Range(0, 24).Select(h => h.ToString("00", CultureInfo.InvariantCulture)).ToList();
var heatmap = (new ChartDefinition
{
    Title = "Sessions per hour",
    Series = [.. weekdays.Select(d => new SeriesOptions { Id = d, Type = SeriesType.Heatmap, Format = "#,0" })],
}, weekdays.Select((d, k) => (d, SeriesData.FromCategories(hours,
        Enumerable.Range(0, 24).Select(h => Math.Round(Math.Max(5, (k >= 5 ? 260 : 200) * Math.Max(0, Math.Sin((h - 6) / 18.0 * Math.PI)) + (h is >= 19 and <= 22 ? 90 : 0)))).ToArray())))
    .ToDictionary(t => t.d, t => t.Item2));

(ChartDefinition Spec, Dictionary<string, SeriesData> Data)[] charts = [sessions, orders, revenue, heatmap];

Write(ExportTheme.Light, "#d1d9e0", Path.Combine("assets", "readme-charts-light.png"));
Write(ExportTheme.Dark, "#3d444d", Path.Combine("assets", "readme-charts-dark.png"));

void Write(ExportTheme theme, string border, string output)
{
    const int CardWidth = ChartWidth + 2 * Pad, CardHeight = ChartHeight + 2 * Pad;
    const int Width = 2 * CardWidth + Gap + 2, Height = 2 * CardHeight + Gap + 2; // + 2: room for the borders
    var info = new SKImageInfo(Width * Scale, Height * Scale);
    using var surface = SKSurface.Create(info);
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent); // GitHub's own page color shows between the cards
    canvas.Scale(Scale);

    var surfaceColor = SKColor.Parse(theme.Resolve(ThemeTokens.Surface));
    for (var i = 0; i < charts.Length; i++)
    {
        var card = SKRect.Create(1 + i % 2 * (CardWidth + Gap), 1 + i / 2 * (CardHeight + Gap), CardWidth, CardHeight);
        using (var fill = new SKPaint { Color = surfaceColor, IsAntialias = true })
        {
            canvas.DrawRoundRect(card, 12, 12, fill);
        }

        using (var stroke = new SKPaint { Color = SKColor.Parse(border), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 })
        {
            canvas.DrawRoundRect(card, 12, 12, stroke);
        }

        var png = ChartExport.ToPng(charts[i].Spec, charts[i].Data, new ExportOptions
        {
            Width = ChartWidth, Height = ChartHeight, Scale = Scale, Theme = theme, Culture = culture, FontSize = 12,
        });
        using var image = SKImage.FromEncodedData(png);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(image, SKRect.Create(card.Left + Pad, card.Top + Pad, ChartWidth, ChartHeight),
            new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
    }

    using var snapshot = surface.Snapshot();
    using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(output, data.ToArray());
    Console.WriteLine($"Wrote {output} ({info.Width} × {info.Height})");
}
