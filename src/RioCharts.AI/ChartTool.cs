using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using RioCharts.Core;
using RioCharts.Core.Layout;
using RioCharts.Core.Specs;
using RioCharts.Skia;

namespace RioCharts.AI;

/// <summary>How the tool renders charts.</summary>
public sealed record ChartToolOptions
{
    /// <summary>Size, theme and culture of the rendered image. Default 800 × 400, light theme.</summary>
    public ExportOptions Export { get; init; } = new() { Width = 800, Height = 400 };

    /// <summary>Also render SVG (in <see cref="ChartToolResult.Svg"/>). Default false.</summary>
    public bool RenderSvg { get; init; }

    /// <summary>
    /// Where render_chart saves its images. When set, each image is also written to a file here and
    /// its path is returned to the agent (for local agents that put charts in reports or documents).
    /// Default null: images are only returned inline.
    /// </summary>
    public string? OutputDirectory { get; init; }

    /// <summary>Largest width or height render_chart accepts from the agent. Default 4000.</summary>
    public int MaxImageSize { get; init; } = 4000;
}

/// <summary>Image format of render_chart.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChartImageFormat>))]
public enum ChartImageFormat
{
    /// <summary>A PNG image.</summary>
    Png,
    /// <summary>An SVG document.</summary>
    Svg,
}

/// <summary>What the agent asks of a rendered image (render_chart). Unset values come from <see cref="ChartToolOptions.Export"/>.</summary>
public sealed record ChartImageRequest
{
    /// <summary>PNG or SVG. Default PNG.</summary>
    public ChartImageFormat Format { get; init; } = ChartImageFormat.Png;

    /// <summary>Width in chart units (PNG pixels are this times <see cref="ExportOptions.Scale"/>).</summary>
    public int? Width { get; init; }

    /// <inheritdoc cref="Width"/>
    public int? Height { get; init; }

    /// <summary>"light" or "dark".</summary>
    public string? Theme { get; init; }

    /// <summary>File name (no folder) when <see cref="ChartToolOptions.OutputDirectory"/> is set. Default: from the title.</summary>
    public string? FileName { get; init; }
}

/// <summary>
/// What the tools return to the agent (as JSON): success, errors to fix, notes and a description
/// of the chart. The image and the bound spec are left out of the JSON: create_chart gives them to
/// the app, render_chart returns the image as a separate content block.
/// </summary>
public sealed record ChartToolResult
{
    /// <summary>True when the chart was created; false when <see cref="Errors"/> lists what to fix.</summary>
    public bool Success { get; init; }

    /// <summary>Problems with the spec, each with its path and what to do ("series[0].y: field 'temp' not found; available: ...").</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Things the tool assumed or adjusted, e.g. "series[0].type: 'column' read as bar".</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>A text description of the chart (series, ranges, trend) the agent can use in its answer.</summary>
    public string? Summary { get; init; }

    /// <summary>The spec as understood (normalized JSON), for saving or editing later.</summary>
    public string? Spec { get; init; }

    /// <summary>render_chart: "png" or "svg".</summary>
    public string? Format { get; init; }

    /// <summary>render_chart: the image size in chart units.</summary>
    public int? Width { get; init; }

    /// <inheritdoc cref="Width"/>
    public int? Height { get; init; }

    /// <summary>render_chart: the file the image was saved to, when the tool has an <see cref="ChartToolOptions.OutputDirectory"/>.</summary>
    public string? File { get; init; }

    /// <summary>The chart as PNG (create_chart, or render_chart with format png).</summary>
    [JsonIgnore] public byte[]? Png { get; init; }

    /// <summary>The chart as SVG (create_chart with <see cref="ChartToolOptions.RenderSvg"/>, or render_chart with format svg).</summary>
    [JsonIgnore] public string? Svg { get; init; }

    /// <summary>The spec with concrete series (types resolved, groups expanded).</summary>
    [JsonIgnore] public ChartSpec? BoundSpec { get; init; }

    /// <summary>The data of each bound series, e.g. to show the chart with the Blazor component.</summary>
    [JsonIgnore] public IReadOnlyDictionary<string, Core.Data.SeriesData>? Data { get; init; }
}

/// <summary>
/// The chart tools for AI agents. <c>create_chart</c> validates a JSON chart spec against the
/// app's datasets (errors name the path and list the valid options, so the model can fix the spec
/// and call again), renders it for the app to show and returns a description. <c>render_chart</c>
/// takes the same spec and returns the image (PNG or SVG) to the agent itself. Use them as
/// Microsoft.Extensions.AI functions (<see cref="AsAIFunctions"/>) or through MCP
/// (<see cref="RioChartsMcpTools"/>).
/// </summary>
public sealed class ChartTool(IChartDataSource dataSource, ChartToolOptions? options = null)
{
    /// <summary>The name of the create_chart tool.</summary>
    public const string ToolName = "create_chart";

    /// <summary>The name of the render_chart tool.</summary>
    public const string RenderToolName = "render_chart";

    /// <summary>The render_chart description for the model.</summary>
    public const string RenderDescription =
        "Renders a chart from a JSON spec (the same spec as create_chart) and returns the image to you as PNG or SVG, "
        + "with a short description of the chart, or errors to fix (then call again). Use it when you need the image itself, "
        + "e.g. for a report, document, email or message; use create_chart to show a chart to the user. "
        + "Options: format (png|svg), width and height in pixels, theme (light|dark). "
        + "Use inline \"data\" rows in the spec to chart numbers that are not in a dataset.";

    private readonly ChartToolOptions _options = options ?? new ChartToolOptions();

    /// <summary>Raised after every successful create_chart, with the image and bound spec (e.g. to show it to the user).</summary>
    public event Action<ChartToolResult>? ChartCreated;

    /// <summary>The tool description for the model: purpose, the datasets and their fields, the spec format and examples.</summary>
    public string Description => BuildDescription(dataSource.Describe());

    /// <summary>Validates, binds and renders a spec (a JSON object, or a string holding one) for the app to show.</summary>
    public async Task<ChartToolResult> CreateChartAsync(string spec, CancellationToken cancellationToken = default)
    {
        var export = _options.Export;
        var (result, layout) = await BindAsync(spec, export, ToolName, cancellationToken).ConfigureAwait(false);
        if (layout is null)
        {
            return result;
        }

        result = result with
        {
            Png = ChartExport.ToPng(layout.Scene, export),
            Svg = _options.RenderSvg ? ChartExport.ToSvg(layout.Scene, export) : null,
        };
        ChartCreated?.Invoke(result);
        return result;
    }

    /// <inheritdoc cref="CreateChartAsync(string, CancellationToken)"/>
    public Task<ChartToolResult> CreateChartAsync(JsonElement spec, CancellationToken cancellationToken = default) =>
        CreateChartAsync(SpecText(spec), cancellationToken);

    /// <summary>
    /// Validates, binds and renders a spec as an image for the agent (render_chart): PNG or SVG at
    /// the requested size and theme, also saved to <see cref="ChartToolOptions.OutputDirectory"/>
    /// when that is set. Does not raise <see cref="ChartCreated"/>.
    /// </summary>
    public async Task<ChartToolResult> RenderChartAsync(string spec, ChartImageRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new ChartImageRequest();
        var requestErrors = CheckRequest(request);
        if (requestErrors.Count > 0)
        {
            return Failure(requestErrors, [], RenderToolName);
        }

        var export = ExportFor(request);
        var (result, layout) = await BindAsync(spec, export, RenderToolName, cancellationToken).ConfigureAwait(false);
        if (layout is null)
        {
            return result;
        }

        var svg = request.Format == ChartImageFormat.Svg ? ChartExport.ToSvg(layout.Scene, export) : null;
        var png = svg is null ? ChartExport.ToPng(layout.Scene, export) : null;

        string? file = null;
        if (_options.OutputDirectory is { } directory)
        {
            Directory.CreateDirectory(directory);
            file = Path.GetFullPath(Path.Combine(directory, FileNameFor(request, result.BoundSpec?.Title)));
            if (png is not null)
            {
                await System.IO.File.WriteAllBytesAsync(file, png, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await System.IO.File.WriteAllTextAsync(file, svg, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            }
        }

        return result with
        {
            Png = png,
            Svg = svg,
            Format = svg is null ? "png" : "svg",
            Width = (int)Math.Round(export.Width),
            Height = (int)Math.Round(export.Height),
            File = file,
        };
    }

    /// <inheritdoc cref="RenderChartAsync(string, ChartImageRequest?, CancellationToken)"/>
    public Task<ChartToolResult> RenderChartAsync(JsonElement spec, ChartImageRequest? request = null, CancellationToken cancellationToken = default) =>
        RenderChartAsync(SpecText(spec), request, cancellationToken);

    /// <summary>The datasets and fields the model can chart, as text.</summary>
    public string ListDatasets() => DescribeDatasets(dataSource.Describe());

    /// <summary>The JSON schema of a spec, for models that want the full detail.</summary>
    public static string GetSchema() => ChartSpecJson.Schema().ToJsonString(new JsonSerializerOptions { WriteIndented = false });

    /// <summary>The tools as Microsoft.Extensions.AI functions: create_chart, render_chart, list_datasets and get_chart_schema.</summary>
    public IList<AITool> AsAIFunctions() =>
    [
        AsAIFunction(),
        AsRenderFunction(),
        AIFunctionFactory.Create(ListDatasets, "list_datasets", "Lists the datasets that charts can use, with their fields and types."),
        AIFunctionFactory.Create(GetSchema, "get_chart_schema", "Returns the full JSON schema of a chart spec (every option with a description)."),
    ];

    /// <summary>create_chart as a Microsoft.Extensions.AI function.</summary>
    public AIFunction AsAIFunction() => AIFunctionFactory.Create(
        ([Description("The chart spec: a JSON object as described in the tool description.")] JsonElement spec, CancellationToken cancellationToken) =>
            CreateChartAsync(spec, cancellationToken),
        ToolName,
        Description);

    /// <summary>
    /// render_chart as a Microsoft.Extensions.AI function. It returns the result JSON as text plus
    /// the image as <see cref="DataContent"/> (image/png or image/svg+xml), so the agent gets the
    /// image itself, e.g. to attach to an answer, a report or an email.
    /// </summary>
    public AIFunction AsRenderFunction() => AIFunctionFactory.Create(
        async ([Description("The chart spec: a JSON object as described for create_chart.")] JsonElement spec,
               [Description("png (default) or svg.")] ChartImageFormat format = ChartImageFormat.Png,
               [Description("Width in pixels, 100 to 4000. Default 800.")] int? width = null,
               [Description("Height in pixels, 100 to 4000. Default 400.")] int? height = null,
               [Description("light (default) or dark.")] string? theme = null,
               [Description("File name to save as, when the app saves images to files.")] string? fileName = null,
               CancellationToken cancellationToken = default) =>
        {
            var request = new ChartImageRequest { Format = format, Width = width, Height = height, Theme = theme, FileName = fileName };
            var result = await RenderChartAsync(spec, request, cancellationToken).ConfigureAwait(false);
            return (IList<AIContent>)ToAIContents(result);
        },
        RenderToolName,
        RenderDescription);

    /// <summary>A render_chart result as AI content: the result JSON and, on success, the image.</summary>
    public static List<AIContent> ToAIContents(ChartToolResult result)
    {
        var contents = new List<AIContent> { new TextContent(JsonSerializer.Serialize(result, ChartToolJsonContext.Default.ChartToolResult)) };
        if (result.Png is { } png)
        {
            contents.Add(new DataContent(png, "image/png") { Name = ImageName(result, "chart.png") });
        }
        else if (result.Svg is { } svg)
        {
            contents.Add(new DataContent(Encoding.UTF8.GetBytes(svg), "image/svg+xml") { Name = ImageName(result, "chart.svg") });
        }

        return contents;
    }

    private static string ImageName(ChartToolResult result, string fallback) =>
        result.File is null ? fallback : Path.GetFileName(result.File);

    private async Task<(ChartToolResult Result, ChartLayoutResult? Layout)> BindAsync(
        string spec, ExportOptions export, string toolName, CancellationToken cancellationToken)
    {
        var parsed = ChartSpecJson.Parse(spec);
        if (!parsed.Success)
        {
            return (Failure(parsed.Errors.Select(e => e.ToString()), parsed.Warnings, toolName), null);
        }

        var bound = await ChartSpecBinder.BindAsync(parsed.Spec!, dataSource, cancellationToken).ConfigureAwait(false);
        var warnings = parsed.Warnings.Concat(bound.Warnings).ToList();
        if (!bound.Success)
        {
            return (Failure(bound.Errors.Select(e => e.ToString()), warnings, toolName), null);
        }

        var layout = ChartExport.Layout(bound.Spec, bound.Data, export);
        warnings.AddRange(layout.Warnings);
        var result = new ChartToolResult
        {
            Success = true,
            Warnings = warnings,
            Summary = layout.Scene.Description,
            Spec = ChartSpecJson.Serialize(parsed.Spec!),
            BoundSpec = bound.Spec,
            Data = bound.Data,
        };
        return (result, layout);
    }

    private ExportOptions ExportFor(ChartImageRequest request)
    {
        var export = _options.Export with
        {
            Width = request.Width ?? _options.Export.Width,
            Height = request.Height ?? _options.Export.Height,
        };
        return request.Theme?.Trim().ToLowerInvariant() switch
        {
            "dark" => export with { Theme = ExportTheme.Dark },
            "light" => export with { Theme = ExportTheme.Light },
            _ => export,
        };
    }

    private List<string> CheckRequest(ChartImageRequest request)
    {
        var errors = new List<string>();
        if (request.Width is { } w && (w < 100 || w > _options.MaxImageSize))
        {
            errors.Add($"width: {w} is out of range; use 100 to {_options.MaxImageSize}.");
        }

        if (request.Height is { } h && (h < 100 || h > _options.MaxImageSize))
        {
            errors.Add($"height: {h} is out of range; use 100 to {_options.MaxImageSize}.");
        }

        if (request.Theme is { } t && t.Trim().ToLowerInvariant() is not ("light" or "dark" or ""))
        {
            errors.Add($"theme: '{t}' is not valid; use light or dark.");
        }

        return errors;
    }

    /// <summary>A safe file name: the requested name or the title, without folders, with the format's extension.</summary>
    internal static string FileNameFor(ChartImageRequest request, string? title)
    {
        var extension = request.Format == ChartImageFormat.Svg ? ".svg" : ".png";
        var requested = (request.FileName ?? "").Replace('\\', '/');
        var name = Path.GetFileNameWithoutExtension(requested[(requested.LastIndexOf('/') + 1)..]);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = string.IsNullOrWhiteSpace(title) ? "chart" : title;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in name.Trim())
        {
            sb.Append(invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : char.ToLowerInvariant(c));
        }

        var clean = sb.ToString().Trim('-', '.');
        if (clean.Length == 0)
        {
            clean = "chart";
        }

        return (clean.Length > 80 ? clean[..80] : clean) + extension;
    }

    private static string SpecText(JsonElement spec) =>
        spec.ValueKind == JsonValueKind.String ? spec.GetString() ?? "" : spec.GetRawText();

    private static ChartToolResult Failure(IEnumerable<string> errors, IEnumerable<string> warnings, string toolName) => new()
    {
        Success = false,
        Errors = [.. errors, $"Fix these and call {toolName} again with the corrected spec."],
        Warnings = [.. warnings],
    };

    /// <summary>
    /// A self-contained description of render_chart for servers that offer it without create_chart
    /// (e.g. the riocharts-mcp command-line server): the spec format and examples, and a pointer to
    /// list_datasets instead of a dataset list (datasets may change while the server runs).
    /// </summary>
    public static string StandaloneRenderDescription { get; } = BuildDescription(null, renderOnly: true);

    internal static string BuildDescription(IReadOnlyList<ChartDataset>? datasets, bool renderOnly = false)
    {
        var sb = new StringBuilder();
        if (renderOnly)
        {
            sb.AppendLine("Renders a chart from a JSON spec as a PNG or SVG image, returns it to you with a short description of the chart, and saves it as a file (the path is in \"file\"). On errors, fix the spec and call again.");
            sb.AppendLine("Arguments: spec, and optionally format (png|svg, default png), width and height in pixels (default 800 × 400), theme (light|dark), fileName.");
            sb.AppendLine();
            sb.AppendLine("Data: call list_datasets for the datasets (CSV and JSON files) and their fields, or put small data inline in the spec as \"data\" rows.");
        }
        else
        {
            sb.AppendLine("Creates a chart from a JSON spec. The chart is shown to the user; you get back a short description of it, or errors to fix (then call again).");
            sb.AppendLine();
            sb.AppendLine("Datasets you can chart:");
            sb.Append(DescribeDatasets(datasets ?? []));
        }

        sb.AppendLine();
        sb.AppendLine("Spec (only series with x and y are required; everything else has good defaults):");
        sb.AppendLine("{ \"dataset\": \"<id>\", \"title\": \"...\", \"series\": [ { \"type\": \"line|area|bar|pie|radialBar|auto\", \"x\": \"<field>\", \"y\": \"<numeric field>\",");
        sb.AppendLine("    \"name\"?, \"groupBy\"?: \"<field: one series per value>\", \"aggregate\"?: \"sum|average|min|max|count|last\",");
        sb.AppendLine("    \"timeBucket\"?: \"hour|day|week|month|quarter|year\", \"filter\"?: [ { \"field\", \"op\": \"eq|ne|gt|gte|lt|lte|contains\", \"value\" } ],");
        sb.AppendLine("    \"sort\"?: \"ascending|descending\", \"limit\"?: n, \"stack\"?: \"<group>\", \"yAxisId\"?, \"innerRadius\"?: 0.6 (donut), \"dataLabels\"? } ],");
        sb.AppendLine("  \"y\"?: { \"title\": \"Sessions\" }, \"yAxes\"?: [ { \"id\": \"eur\", \"title\": \"€\" } ],");
        sb.AppendLine("  \"annotations\"?: [ { \"type\": \"line|band|point|callout\", \"axis\": \"x|y\", \"value\" | \"from\"+\"to\" | \"x\"+\"y\", \"label\" } ],");
        sb.AppendLine("  \"data\"?: [ { ...small inline rows instead of a dataset... } ] }");
        sb.AppendLine();
        sb.AppendLine("Examples:");
        sb.AppendLine("{\"dataset\":\"shop\",\"series\":[{\"type\":\"line\",\"x\":\"timestamp\",\"y\":\"sessions\",\"groupBy\":\"channel\"}],\"annotations\":[{\"value\":600,\"label\":\"Goal\"}]}");
        sb.AppendLine("{\"dataset\":\"shop\",\"title\":\"Revenue per day\",\"series\":[{\"type\":\"bar\",\"x\":\"timestamp\",\"y\":\"revenue\",\"timeBucket\":\"day\",\"aggregate\":\"sum\"}]}");
        sb.AppendLine("{\"data\":[{\"category\":\"Shoes\",\"share\":45},{\"category\":\"Clothing\",\"share\":55}],\"series\":[{\"type\":\"auto\",\"x\":\"category\",\"y\":\"share\"}]}");
        sb.AppendLine();
        sb.AppendLine("Tips: time on x gives a line; categories give bars; shares of a whole give a donut (\"auto\" picks). Use timeBucket + aggregate for long time ranges, sort + limit for top-N, groupBy for one line per channel or category. Call get_chart_schema for every option.");
        if (!renderOnly)
        {
            sb.AppendLine("To get the image yourself (PNG or SVG, e.g. for a report), call render_chart with the same spec.");
        }

        return sb.ToString();
    }

    private static string DescribeDatasets(IReadOnlyList<ChartDataset> datasets)
    {
        if (datasets.Count == 0)
        {
            return "(none: use inline \"data\" rows)" + Environment.NewLine;
        }

        var sb = new StringBuilder();
        foreach (var d in datasets)
        {
            sb.Append("- ").Append(d.Name);
            if (!string.IsNullOrWhiteSpace(d.Description))
            {
                sb.Append(": ").Append(d.Description);
            }

            if (d.RowCount > 0)
            {
                sb.Append(" (").Append(d.RowCount.ToString(CultureInfo.InvariantCulture)).Append(" rows)");
            }

            sb.AppendLine();
            if (d.Fields.Count == 0)
            {
                continue; // unreadable or empty: the description says why
            }

            sb.Append("  fields: ").AppendLine(string.Join(", ", d.Fields.Select(f =>
                $"{f.Name} ({f.Type.ToString().ToLowerInvariant()}{(string.IsNullOrWhiteSpace(f.Description) ? "" : ", " + f.Description)})")));
        }

        return sb.ToString();
    }
}

/// <summary>Source-generated JSON for tool results (trimming and native AOT safe).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ChartToolResult))]
internal sealed partial class ChartToolJsonContext : JsonSerializerContext;
