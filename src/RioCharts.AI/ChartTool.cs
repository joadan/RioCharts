using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using RioCharts.Core;
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
}

/// <summary>
/// What <c>create_chart</c> returns to the agent (as JSON): success, errors to fix, notes and a
/// description of the chart. The image and the bound spec are for the app and are not sent to the
/// model (they are left out of the JSON).
/// </summary>
public sealed record ChartToolResult
{
    public bool Success { get; init; }

    /// <summary>Problems with the spec, each with its path and what to do ("series[0].y: field 'temp' not found; available: ...").</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Things the tool assumed or adjusted, e.g. "series[0].type: 'column' read as bar".</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>A text description of the chart (series, ranges, trend) the agent can use in its answer.</summary>
    public string? Summary { get; init; }

    /// <summary>The spec as understood (normalized JSON), for saving or editing later.</summary>
    public string? Spec { get; init; }

    [JsonIgnore] public byte[]? Png { get; init; }

    [JsonIgnore] public string? Svg { get; init; }

    /// <summary>The spec with concrete series (types resolved, groups expanded).</summary>
    [JsonIgnore] public ChartSpec? BoundSpec { get; init; }

    /// <summary>The data of each bound series, e.g. to show the chart with the Blazor component.</summary>
    [JsonIgnore] public IReadOnlyDictionary<string, Core.Data.SeriesData>? Data { get; init; }
}

/// <summary>
/// The <c>create_chart</c> tool for AI agents: validates a JSON chart spec against the app's
/// datasets (errors name the path and list the valid options, so the model can fix the spec and
/// call again), renders it on the server and returns a description. Use it as a
/// Microsoft.Extensions.AI <see cref="AIFunction"/> (<see cref="AsAIFunctions"/>) or through MCP
/// (<see cref="RioChartsMcpTools"/>).
/// </summary>
public sealed class ChartTool(IChartDataSource dataSource, ChartToolOptions? options = null)
{
    public const string ToolName = "create_chart";

    private readonly ChartToolOptions _options = options ?? new ChartToolOptions();

    /// <summary>Raised after every successful chart, with the image and bound spec (e.g. to show it to the user).</summary>
    public event Action<ChartToolResult>? ChartCreated;

    /// <summary>The tool description for the model: purpose, the datasets and their fields, the spec format and examples.</summary>
    public string Description => BuildDescription(dataSource.Describe());

    /// <summary>Validates, binds and renders a spec (a JSON object, or a string holding one).</summary>
    public async Task<ChartToolResult> CreateChartAsync(string spec, CancellationToken cancellationToken = default)
    {
        var parsed = ChartSpecJson.Parse(spec);
        if (!parsed.Success)
        {
            return Failure(parsed.Errors, parsed.Warnings);
        }

        var bound = await ChartSpecBinder.BindAsync(parsed.Spec!, dataSource, cancellationToken).ConfigureAwait(false);
        var warnings = parsed.Warnings.Concat(bound.Warnings).ToList();
        if (!bound.Success)
        {
            return Failure(bound.Errors, warnings);
        }

        var layout = ChartExport.Layout(bound.Spec, bound.Data, _options.Export);
        warnings.AddRange(layout.Warnings);
        var result = new ChartToolResult
        {
            Success = true,
            Warnings = warnings,
            Summary = layout.Scene.Description,
            Spec = ChartSpecJson.Serialize(parsed.Spec!),
            Png = ChartExport.ToPng(layout.Scene, _options.Export),
            Svg = _options.RenderSvg ? ChartExport.ToSvg(layout.Scene, _options.Export) : null,
            BoundSpec = bound.Spec,
            Data = bound.Data,
        };
        ChartCreated?.Invoke(result);
        return result;
    }

    /// <inheritdoc cref="CreateChartAsync(string, CancellationToken)"/>
    public Task<ChartToolResult> CreateChartAsync(JsonElement spec, CancellationToken cancellationToken = default) =>
        CreateChartAsync(spec.ValueKind == JsonValueKind.String ? spec.GetString() ?? "" : spec.GetRawText(), cancellationToken);

    /// <summary>The datasets and fields the model can chart, as text.</summary>
    public string ListDatasets() => DescribeDatasets(dataSource.Describe());

    /// <summary>The JSON schema of a spec, for models that want the full detail.</summary>
    public static string GetSchema() => ChartSpecJson.Schema().ToJsonString(new JsonSerializerOptions { WriteIndented = false });

    /// <summary>The tools as Microsoft.Extensions.AI functions: create_chart, list_datasets and get_chart_schema.</summary>
    public IList<AITool> AsAIFunctions() =>
    [
        AsAIFunction(),
        AIFunctionFactory.Create(ListDatasets, "list_datasets", "Lists the datasets that charts can use, with their fields and types."),
        AIFunctionFactory.Create(GetSchema, "get_chart_schema", "Returns the full JSON schema of a chart spec (every option with a description)."),
    ];

    /// <summary>create_chart as a Microsoft.Extensions.AI function.</summary>
    public AIFunction AsAIFunction() => AIFunctionFactory.Create(
        ([Description("The chart spec: a JSON object as described in the tool description.")] JsonElement spec, CancellationToken cancellationToken) =>
            CreateChartAsync(spec, cancellationToken),
        ToolName,
        Description);

    private static ChartToolResult Failure(IEnumerable<SpecError> errors, IEnumerable<string> warnings) => new()
    {
        Success = false,
        Errors = [.. errors.Select(e => e.ToString()), "Fix these and call create_chart again with the corrected spec."],
        Warnings = [.. warnings],
    };

    internal static string BuildDescription(IReadOnlyList<ChartDataset> datasets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Creates a chart from a JSON spec. The chart is shown to the user; you get back a short description of it, or errors to fix (then call again).");
        sb.AppendLine();
        sb.AppendLine("Datasets you can chart:");
        sb.Append(DescribeDatasets(datasets));
        sb.AppendLine();
        sb.AppendLine("Spec (only series with x and y are required; everything else has good defaults):");
        sb.AppendLine("{ \"dataset\": \"<id>\", \"title\": \"...\", \"series\": [ { \"type\": \"line|area|bar|pie|radialBar|auto\", \"x\": \"<field>\", \"y\": \"<numeric field>\",");
        sb.AppendLine("    \"name\"?, \"groupBy\"?: \"<field: one series per value>\", \"aggregate\"?: \"sum|average|min|max|count|last\",");
        sb.AppendLine("    \"timeBucket\"?: \"hour|day|week|month|quarter|year\", \"filter\"?: [ { \"field\", \"op\": \"eq|ne|gt|gte|lt|lte|contains\", \"value\" } ],");
        sb.AppendLine("    \"sort\"?: \"ascending|descending\", \"limit\"?: n, \"stack\"?: \"<group>\", \"yAxisId\"?, \"innerRadius\"?: 0.6 (donut), \"dataLabels\"? } ],");
        sb.AppendLine("  \"y\"?: { \"title\": \"°C\" }, \"yAxes\"?: [ { \"id\": \"kwh\", \"title\": \"kWh\" } ],");
        sb.AppendLine("  \"annotations\"?: [ { \"type\": \"line|band|point|callout\", \"axis\": \"x|y\", \"value\" | \"from\"+\"to\" | \"x\"+\"y\", \"label\" } ],");
        sb.AppendLine("  \"data\"?: [ { ...small inline rows instead of a dataset... } ] }");
        sb.AppendLine();
        sb.AppendLine("Examples:");
        sb.AppendLine("{\"dataset\":\"ahu\",\"series\":[{\"type\":\"line\",\"x\":\"timestamp\",\"y\":\"supplyTemp\",\"groupBy\":\"unit\"}],\"annotations\":[{\"value\":21,\"label\":\"Setpoint\"}]}");
        sb.AppendLine("{\"dataset\":\"ahu\",\"title\":\"Energy per day\",\"series\":[{\"type\":\"bar\",\"x\":\"timestamp\",\"y\":\"energy\",\"timeBucket\":\"day\",\"aggregate\":\"sum\"}]}");
        sb.AppendLine("{\"data\":[{\"system\":\"Heating\",\"share\":45},{\"system\":\"Fans\",\"share\":55}],\"series\":[{\"type\":\"auto\",\"x\":\"system\",\"y\":\"share\"}]}");
        sb.AppendLine();
        sb.AppendLine("Tips: time on x gives a line; categories give bars; shares of a whole give a donut (\"auto\" picks). Use timeBucket + aggregate for long time ranges, sort + limit for top-N, groupBy for one line per unit. Call get_chart_schema for every option.");
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
            sb.Append("  fields: ").AppendLine(string.Join(", ", d.Fields.Select(f =>
                $"{f.Name} ({f.Type.ToString().ToLowerInvariant()}{(string.IsNullOrWhiteSpace(f.Description) ? "" : ", " + f.Description)})")));
        }

        return sb.ToString();
    }
}
