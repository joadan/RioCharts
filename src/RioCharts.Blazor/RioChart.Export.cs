using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using RioCharts.Core.Data;

namespace RioCharts.Blazor;

/// <summary>Options for exporting a chart as an image in the browser.</summary>
public sealed record ChartImageOptions
{
    /// <summary>PNG only: pixels per CSS pixel. Default 2 (sharp on high-density screens and in print).</summary>
    public double Scale { get; init; } = 2;

    /// <summary>
    /// Background color (any CSS color). Default: the chart's surface color (<c>--rio-surface</c>),
    /// so the image reads the same outside the page. "transparent" for none.
    /// </summary>
    public string? Background { get; init; }
}

/// <summary>An export format.</summary>
public enum ExportFormat
{
    Png,
    Svg,
    Csv,
}

/// <summary>The formats offered in the download menu (<c>Exportable</c>).</summary>
[Flags]
public enum ExportFormats
{
    None = 0,
    Png = 1,
    Svg = 2,
    Csv = 4,
    All = Png | Svg | Csv,
}

/// <summary>
/// Raised when the user picks a format in the download menu, before the file is saved. Change
/// <see cref="FileName"/> or the options, or set <see cref="Cancel"/> and handle the export yourself
/// (e.g. upload it) with <see cref="GetPngAsync"/>, <see cref="GetSvgAsync"/> or <see cref="GetCsv"/>.
/// The content is made only when one of those is called, so a handler that only renames costs nothing.
/// </summary>
public sealed class ChartExportingEventArgs
{
    private readonly Func<ChartImageOptions, Task<byte[]>> _png;
    private readonly Func<ChartImageOptions, Task<string>> _svg;
    private readonly Func<CsvOptions?, string> _csv;

    internal ChartExportingEventArgs(ExportFormat format, string fileName, Func<ChartImageOptions, Task<byte[]>> png,
        Func<ChartImageOptions, Task<string>> svg, Func<CsvOptions?, string> csv)
    {
        Format = format;
        FileName = fileName;
        _png = png;
        _svg = svg;
        _csv = csv;
    }

    /// <summary>The format the user picked.</summary>
    public ExportFormat Format { get; }

    /// <summary>The file name with extension; change it to save under another name.</summary>
    public string FileName { get; set; }

    /// <summary>Options for PNG and SVG (used by the download and by GetPngAsync/GetSvgAsync).</summary>
    public ChartImageOptions ImageOptions { get; set; } = new();

    /// <summary>Options for CSV; null: the defaults, with the x axis title as the x header.</summary>
    public CsvOptions? CsvOptions { get; set; }

    /// <summary>Set to true to skip the browser download (after handling the export yourself, or to block it).</summary>
    public bool Cancel { get; set; }

    /// <summary>The chart as PNG bytes, with <see cref="ImageOptions"/>.</summary>
    public Task<byte[]> GetPngAsync() => _png(ImageOptions);

    /// <summary>The chart as an SVG document, with <see cref="ImageOptions"/>.</summary>
    public Task<string> GetSvgAsync() => _svg(ImageOptions);

    /// <summary>The data as CSV, with <see cref="CsvOptions"/>.</summary>
    public string GetCsv() => _csv(CsvOptions);
}

// Client-side export: the browser turns the chart as drawn (with the page's CSS theme resolved)
// into SVG or PNG; CSV comes from the chart's data in .NET.
public partial class RioChart<TItem>
{
    private const long MaxExportBytes = 256L * 1024 * 1024;

    /// <summary>
    /// Shows a download button in the chart's top-right corner (on hover or focus) with a menu for
    /// SVG, PNG and CSV. The same exports are available from code: <see cref="DownloadPngAsync"/> etc.
    /// </summary>
    [Parameter] public bool Exportable { get; set; }

    /// <summary>
    /// The formats in the download menu. Default all. With a single format the button saves it
    /// directly, without a menu; with none there is no button.
    /// </summary>
    [Parameter] public ExportFormats ExportFormats { get; set; } = ExportFormats.All;

    /// <summary>
    /// Raised when the user picks a format in the download menu, before the file is saved: rename
    /// it, change the options, or cancel the download and handle the content yourself. Not raised
    /// for the export methods called from code.
    /// </summary>
    [Parameter] public EventCallback<ChartExportingEventArgs> OnExporting { get; set; }

    /// <summary>File name without extension for downloads. Default: the title, else "chart".</summary>
    [Parameter] public string? ExportFileName { get; set; }

    /// <summary>
    /// The chart as a standalone SVG document, as currently drawn (size, zoom, hidden series and
    /// theme colors resolved). Needs an interactive render mode.
    /// </summary>
    public async Task<string> ExportSvgAsync(ChartImageOptions? options = null)
    {
        var bytes = await ExportBytesAsync("exportSvg", options);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>The chart as a PNG image, as currently drawn. Needs an interactive render mode.</summary>
    public Task<byte[]> ExportPngAsync(ChartImageOptions? options = null) => ExportBytesAsync("exportPng", options);

    /// <summary>
    /// The data of every series (hidden ones too) as CSV: an x column and one column per series.
    /// Works without a browser. See <see cref="ChartCsv"/>.
    /// </summary>
    public string ExportCsv(CsvOptions? options = null)
    {
        EnsureLayout();
        var series = CurrentSpec().Series
            .Select(s => (s.DisplayName, DataOf(s.Id) ?? SeriesData.Empty))
            .ToList();
        return ChartCsv.Write(series, options ?? new CsvOptions { XHeader = CurrentSpec().X.Title ?? "x" });
    }

    /// <summary>Saves the chart as an SVG file in the browser (file name without extension; default <see cref="ExportFileName"/>).</summary>
    public Task DownloadSvgAsync(string? fileName = null, ChartImageOptions? options = null) =>
        Module().InvokeVoidAsync("download", _container, "svg", FileName(fileName, "svg"), ImageOptions(options)).AsTask();

    /// <summary>Saves the chart as a PNG file in the browser.</summary>
    public Task DownloadPngAsync(string? fileName = null, ChartImageOptions? options = null) =>
        Module().InvokeVoidAsync("download", _container, "png", FileName(fileName, "png"), ImageOptions(options)).AsTask();

    /// <summary>Saves the data as a CSV file in the browser.</summary>
    public Task DownloadCsvAsync(string? fileName = null, CsvOptions? options = null) =>
        Module().InvokeVoidAsync("downloadText", FileName(fileName, "csv"), ExportCsv(options), "text/csv").AsTask();

    private async Task<byte[]> ExportBytesAsync(string function, ChartImageOptions? options)
    {
        // A stream, not a byte[] result: on Blazor Server results from JS are limited to the
        // hub's MaximumReceiveMessageSize (32 KB by default).
        await using var reference = await Module().InvokeAsync<IJSStreamReference>(function, _container, ImageOptions(options));
        await using var stream = await reference.OpenReadStreamAsync(MaxExportBytes);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private IJSObjectReference Module() => _module
        ?? throw new InvalidOperationException("Exporting needs the browser: call it after the chart has rendered in an interactive render mode.");

    private static object ImageOptions(ChartImageOptions? options)
    {
        options ??= new ChartImageOptions();
        return new { scale = options.Scale > 0 && double.IsFinite(options.Scale) ? options.Scale : 2, background = options.Background };
    }

    private string FileName(string? name, string extension)
    {
        var baseName = !string.IsNullOrWhiteSpace(name) ? name
            : !string.IsNullOrWhiteSpace(ExportFileName) ? ExportFileName
            : !string.IsNullOrWhiteSpace(Title) ? Title
            : "chart";
        var safe = new string(baseName.Trim().Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c is '/' or '\\' or ':' ? '-' : c).ToArray());
        return safe.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase) ? safe : $"{safe}.{extension}";
    }

    // ---------- The download menu ----------

    private bool _exportMenuOpen;
    private ElementReference _exportFirstItem;
    private bool _focusExportMenu;

    private void ToggleExportMenu()
    {
        _exportMenuOpen = !_exportMenuOpen;
        _focusExportMenu = _exportMenuOpen;
    }

    private void CloseExportMenu() => _exportMenuOpen = false;

    private void OnExportMenuKey(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            _exportMenuOpen = false;
        }
    }

    private static readonly (ExportFormats Flag, ExportFormat Format, string Label)[] MenuFormats =
    [
        (ExportFormats.Png, ExportFormat.Png, "PNG"),
        (ExportFormats.Svg, ExportFormat.Svg, "SVG"),
        (ExportFormats.Csv, ExportFormat.Csv, "CSV"),
    ];

    private List<(ExportFormat Format, string Label)> OfferedFormats() =>
        MenuFormats.Where(f => ExportFormats.HasFlag(f.Flag)).Select(f => (f.Format, f.Label)).ToList();

    private async Task OnExportButtonClick()
    {
        var offered = OfferedFormats();
        if (offered.Count == 1)
        {
            await ExportFromMenu(offered[0].Format);
            return;
        }

        ToggleExportMenu();
    }

    private async Task ExportFromMenu(ExportFormat format)
    {
        _exportMenuOpen = false;
        try
        {
            var extension = format.ToString().ToLowerInvariant();
            var args = new ChartExportingEventArgs(format, FileName(null, extension), ExportPngAsync, ExportSvgAsync, ExportCsv);
            if (OnExporting.HasDelegate)
            {
                await OnExporting.InvokeAsync(args);
                if (args.Cancel)
                {
                    return;
                }
            }

            var fileName = FileName(args.FileName, extension);
            await (format switch
            {
                ExportFormat.Png => DownloadPngAsync(fileName, args.ImageOptions),
                ExportFormat.Svg => DownloadSvgAsync(fileName, args.ImageOptions),
                _ => DownloadCsvAsync(fileName, args.CsvOptions),
            });
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException)
        {
            // Not interactive yet.
        }
    }

    private async Task FocusExportMenuAsync()
    {
        if (_focusExportMenu && _exportMenuOpen)
        {
            _focusExportMenu = false;
            try
            {
                await _exportFirstItem.FocusAsync();
            }
            catch (JSException)
            {
            }
        }
    }
}
