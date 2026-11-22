#:project ../src/RioCharts.Skia/RioCharts.Skia.csproj
#:property PublishAot=false

// The link preview image of the docs site (Open Graph og:image, 1200 × 630): the logo, the name
// and a chart drawn by RioCharts.Skia. Run from the repository root:
//   dotnet run tools/SocialCard.cs
// It writes samples/RioCharts.Docs/wwwroot/social-card.png. Uses Segoe UI (Windows).
//
// Everything is centered: Teams (and others) show a small, nearly square thumbnail cut from the
// middle, while Slack, LinkedIn and X show the whole card. The logo and name sit in the middle
// third so they survive the crop.

using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Skia;
using SkiaSharp;

const int Width = 1200, Height = 630;
const string Ink = "#1f2328", Blue = "#4269d0";
var output = Path.Combine("samples", "RioCharts.Docs", "wwwroot", "social-card.png");

// A chart drawn by the library itself: web sessions over two weeks against a goal.
var days = Enumerable.Range(0, 14).Select(d => new DateTime(2026, 3, 2).AddDays(d)).ToArray();
double[] web = [820, 880, 940, 910, 1010, 760, 700, 960, 1040, 1120, 1080, 1210, 900, 860];
double[] app = [410, 450, 470, 500, 540, 610, 640, 560, 600, 650, 690, 720, 780, 820];
var spec = new ChartSpec
{
    Series =
    [
        new SeriesSpec { Id = "web", Name = "Web", Type = SeriesType.Area, Curve = CurveType.Monotone },
        new SeriesSpec { Id = "app", Name = "App", Type = SeriesType.Line, Curve = CurveType.Monotone, StrokeWidth = 3 },
    ],
    Annotations = [new AnnotationSpec { Value = 1000.0, Label = "Goal" }],
};
var data = new Dictionary<string, SeriesData> { ["web"] = SeriesData.From(days, web), ["app"] = SeriesData.From(days, app) };
const int ChartWidth = 700, ChartHeight = 250;
var chartPng = ChartExport.ToPng(spec, data, new ExportOptions
{
    Width = ChartWidth, Height = ChartHeight, Scale = 2, FontSize = 14, Culture = CultureInfo.GetCultureInfo("en-US"),
});

var info = new SKImageInfo(Width, Height);
using var surface = SKSurface.Create(info);
var canvas = surface.Canvas;

// Background: a soft diagonal gradient.
using (var paint = new SKPaint())
{
    paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(Width, Height),
        [SKColor.Parse("#f8f9fd"), SKColor.Parse("#e9eefb")], SKShaderTileMode.Clamp);
    canvas.DrawRect(0, 0, Width, Height, paint);
}

static SKFont Font(int size, SKFontStyleWeight weight) =>
    new(SKTypeface.FromFamilyName("Segoe UI", weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), size);

void Text(string text, float x, float y, SKFont font, string color)
{
    using var paint = new SKPaint { Color = SKColor.Parse(color), IsAntialias = true };
    canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
}

// Logo and name on one centered row.
const float LogoSize = 116, Gap = 26, RowTop = 56;
using var nameRegular = Font(100, SKFontStyleWeight.Medium);
using var nameBold = Font(100, SKFontStyleWeight.ExtraBold);
var rio = nameRegular.MeasureText("Rio");
var nameWidth = rio + nameBold.MeasureText("Charts");
var rowLeft = (Width - (LogoSize + Gap + nameWidth)) / 2;

canvas.Save();
canvas.Translate(rowLeft, RowTop);
canvas.Scale(LogoSize / 256);
DrawLogo(canvas);
canvas.Restore();

var baseline = RowTop + LogoSize / 2 + 34; // the name's capitals centered on the logo
Text("Rio", rowLeft + LogoSize + Gap, baseline, nameRegular, Ink);
Text("Charts", rowLeft + LogoSize + Gap + rio, baseline, nameBold, Ink);

// Tagline, centered: "Charts for Blazor in pure .NET" with the last part in blue.
using var tagline = Font(40, SKFontStyleWeight.SemiBold);
const string First = "Charts for Blazor ", Second = "in pure .NET";
var taglineLeft = (Width - tagline.MeasureText(First + Second)) / 2;
Text(First, taglineLeft, 246, tagline, Ink);
Text(Second, taglineLeft + tagline.MeasureText(First), 246, tagline, Blue);

// The chart on a white card with a soft shadow, centered below.
var card = SKRect.Create((Width - ChartWidth - 40) / 2f, 292, ChartWidth + 40, ChartHeight + 40);
using (var shadow = new SKPaint { Color = SKColor.Parse(Ink).WithAlpha(40), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 18) })
{
    canvas.DrawRoundRect(new SKRect(card.Left, card.Top + 10, card.Right, card.Bottom + 10), 24, 24, shadow);
}

using (var white = new SKPaint { Color = SKColors.White, IsAntialias = true })
{
    canvas.DrawRoundRect(card, 24, 24, white);
}

using (var chart = SKImage.FromEncodedData(chartPng))
using (var sampling = new SKPaint { IsAntialias = true })
{
    canvas.DrawImage(chart, SKRect.Create(card.Left + 20, card.Top + 20, ChartWidth, ChartHeight),
        new SKSamplingOptions(SKCubicResampler.Mitchell), sampling);
}

using var image = surface.Snapshot();
using var png = image.Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes(output, png.ToArray());
Console.WriteLine($"Wrote {output} ({Width} × {Height})");

static void DrawLogo(SKCanvas canvas)
{
    void Fill(string path, string color)
    {
        using var p = SKPath.ParseSvgPathData(path);
        using var paint = new SKPaint { Color = SKColor.Parse(color), IsAntialias = true };
        canvas.DrawPath(p, paint);
    }

    using (var plate = new SKPaint { Color = SKColor.Parse("#efb118"), IsAntialias = true })
    {
        canvas.DrawRoundRect(new SKRect(0, 0, 256, 256), 56, 56, plate);
    }

    using (var line = SKPath.ParseSvgPathData("M30 212 L64 180 L90 196 L126 160"))
    using (var stroke = new SKPaint
    {
        Color = SKColor.Parse("#4269d0"), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 20,
        StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
    })
    {
        canvas.DrawPath(line, stroke);
    }

    Fill("M100 178 C68 144,80 66,140 60 C178 56,198 74,204 96 L198 148 C176 164,146 166,114 182 Z", "#4269d0");
    Fill("M196 90 C236 86,248 140,220 176 C214 152,206 142,190 138 Z", "#1f2328");
    Fill("M190 138 C204 140,212 150,214 164 C200 162,192 154,188 146 Z", "#1f2328");
    using (var eye = new SKPaint { Color = SKColors.White, IsAntialias = true })
    {
        canvas.DrawOval(new SKRect(164 - 26, 104 - 22, 164 + 26, 104 + 22), eye);
    }

    using (var pupil = new SKPaint { Color = SKColor.Parse("#1f2328"), IsAntialias = true })
    {
        canvas.DrawCircle(167, 102, 8.5f, pupil);
    }

    using (var glint = new SKPaint { Color = SKColors.White, IsAntialias = true })
    {
        canvas.DrawCircle(170, 99, 2.5f, glint);
    }
}
