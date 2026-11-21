using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using SkiaSharp;

namespace RioCharts.Skia;

/// <summary>Size, look and format of an exported chart.</summary>
public sealed record ExportOptions
{
    /// <summary>Width in pixels (chart units). Default 800.</summary>
    public double Width { get; init; } = 800;

    /// <summary>Height in pixels (chart units). Default 400.</summary>
    public double Height { get; init; } = 400;

    /// <summary>Pixel density of PNG output (2 = sharp on high-density screens). Default 2.</summary>
    public double Scale { get; init; } = 2;

    /// <summary>Colors for the theme tokens. Default <see cref="ExportTheme.Light"/>.</summary>
    public ExportTheme Theme { get; init; } = ExportTheme.Light;

    /// <summary>Background color or token; null for transparent. Default: the theme's surface color.</summary>
    public string? Background { get; init; } = ThemeTokens.Surface;

    /// <summary>Font families in order of preference (the first installed one is used for PNG and measurement).</summary>
    public string FontFamily { get; init; } = "Segoe UI, Inter, Roboto, Helvetica Neue, Helvetica, Arial, sans-serif";

    /// <summary>Culture for number and date labels. Default: the current culture.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;

    /// <summary>The chart's own texts ("No data available", "Total", "Other", the description). Null: <see cref="ChartTexts.For"/> the culture.</summary>
    public ChartTexts? Texts { get; init; }

    /// <summary>Base font size. Default 12.</summary>
    public double FontSize { get; init; } = 12;

    /// <summary>Series hidden as in the legend (ids; category names for pies).</summary>
    public IReadOnlySet<string>? HiddenSeries { get; init; }

    /// <summary>Visible x range (zoom) in data units, milliseconds for time axes.</summary>
    public (double Min, double Max)? XRange { get; init; }

    /// <summary>The viewer's time zone, for charts with TimeZone "local".</summary>
    public string? ViewerTimeZone { get; init; }

    /// <summary>SVG only: write theme colors as CSS variables with the theme's colors as fallback, so the page can restyle it.</summary>
    public bool CssVariables { get; init; }
}

/// <summary>
/// Server-side chart rendering: the same <see cref="ChartSpec"/> and data as the Blazor
/// components, laid out with real font metrics and written as SVG or PNG. No browser needed.
/// </summary>
/// <example>
/// <code>
/// var spec = new ChartSpec { Title = "Sessions", Series = [new SeriesSpec { Id = "web", Name = "Web" }] };
/// var data = new Dictionary&lt;string, SeriesData&gt; { ["web"] = SeriesData.From(times, values) };
/// byte[] png = ChartExport.ToPng(spec, data, new ExportOptions { Width = 640, Height = 320 });
/// </code>
/// </example>
public static class ChartExport
{
    private static readonly Dictionary<string, SkiaFonts> Fonts = new(StringComparer.Ordinal);
    private static readonly Lock FontsLock = new();

    /// <summary>Lays out the chart with Skia text measurement. The result holds the scene, scales and warnings.</summary>
    public static ChartLayoutResult Layout(ChartSpec spec, IReadOnlyDictionary<string, SeriesData> data, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        return ChartLayout.Compute(spec, data, new LayoutOptions
        {
            Width = options.Width,
            Height = options.Height,
            Measurer = new SkiaTextMeasurer(FontsFor(options)),
            Culture = options.Culture,
            Texts = options.Texts,
            FontSize = options.FontSize,
            TitleFontSize = options.FontSize * 1.25,
            HiddenSeries = options.HiddenSeries,
            XRange = options.XRange,
            ViewerTimeZone = options.ViewerTimeZone,
        });
    }

    /// <summary>The chart as a standalone SVG document.</summary>
    public static string ToSvg(ChartSpec spec, IReadOnlyDictionary<string, SeriesData> data, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        return ToSvg(Layout(spec, data, options).Scene, options);
    }

    /// <summary>A scene (e.g. from <see cref="Layout"/> or a Blazor chart's <c>Layout</c>) as a standalone SVG document.</summary>
    public static string ToSvg(Scene scene, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        return SvgExporter.Write(scene, options.Theme, options.FontFamily, options.Background, options.CssVariables);
    }

    /// <summary>The chart as a PNG image, <see cref="ExportOptions.Scale"/> times the chart size.</summary>
    public static byte[] ToPng(ChartSpec spec, IReadOnlyDictionary<string, SeriesData> data, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        return ToPng(Layout(spec, data, options).Scene, options);
    }

    /// <summary>A scene as a PNG image.</summary>
    public static byte[] ToPng(Scene scene, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        return new SkiaRenderer(options.Theme, FontsFor(options)).Encode(scene, options.Scale, options.Background);
    }

    /// <summary>Writes the chart as PNG to a stream (e.g. an HTTP response).</summary>
    public static async Task WritePngAsync(Stream output, ChartSpec spec, IReadOnlyDictionary<string, SeriesData> data, ExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var png = ToPng(spec, data, options);
        await output.WriteAsync(png, cancellationToken).ConfigureAwait(false);
    }

    private static SkiaFonts FontsFor(ExportOptions options)
    {
        lock (FontsLock)
        {
            if (!Fonts.TryGetValue(options.FontFamily, out var fonts))
            {
                fonts = new SkiaFonts(options.FontFamily);
                Fonts[options.FontFamily] = fonts;
            }

            return fonts;
        }
    }
}
