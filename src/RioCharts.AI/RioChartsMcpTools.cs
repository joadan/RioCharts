using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RioCharts.AI;

/// <summary>
/// The chart tools for an MCP server: create_chart (returns the PNG and a description, or errors
/// to fix), render_chart (returns the chart as a PNG or SVG for the agent to use), list_datasets
/// and get_chart_schema.
/// <code>builder.Services.AddMcpServer().WithHttpTransport().WithRioChartsTools(new ChartTool(myDataSource));</code>
/// </summary>
[McpServerToolType]
public sealed class RioChartsMcpTools(ChartTool tool)
{
    /// <summary>create_chart: validates and renders a spec, returning the result JSON and the PNG.</summary>
    [McpServerTool(Name = ChartTool.ToolName, Title = "Create chart")]
    [Description("Creates a chart from a JSON spec and returns it as a PNG with a short description, or errors to fix (then call again). "
        + "Call list_datasets first to see the datasets and fields. Minimal spec: "
        + "{\"dataset\":\"<id>\",\"series\":[{\"type\":\"line\",\"x\":\"<time field>\",\"y\":\"<numeric field>\"}]}. "
        + "Types: line, area, bar, pie, radialBar, auto. Series options: groupBy, aggregate (sum|average|min|max|count|last), "
        + "timeBucket (minute|hour|day|week|month|quarter|year|auto), filter, sort, limit, stack. Inline \"data\" rows work instead of a dataset. "
        + "Call get_chart_schema for every option.")]
    public async Task<CallToolResult> CreateChart(
        [Description("The chart spec as a JSON object.")] JsonElement spec,
        CancellationToken cancellationToken)
    {
        var result = await tool.CreateChartAsync(spec, cancellationToken).ConfigureAwait(false);
        return ToCallToolResult(result);
    }

    /// <summary>render_chart: renders a spec as a PNG or SVG image for the agent.</summary>
    [McpServerTool(Name = ChartTool.RenderToolName, Title = "Render chart image")]
    [Description(ChartTool.RenderDescription)]
    public async Task<CallToolResult> RenderChart(
        [Description("The chart spec as a JSON object (the same spec as create_chart).")] JsonElement spec,
        [Description("png (default) or svg.")] ChartImageFormat format = ChartImageFormat.Png,
        [Description("Width in pixels, 100 to 4000. Default 800.")] int? width = null,
        [Description("Height in pixels, 100 to 4000. Default 400.")] int? height = null,
        [Description("light (default) or dark.")] string? theme = null,
        [Description("File name to save as, when the server saves images to files.")] string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ChartImageRequest { Format = format, Width = width, Height = height, Theme = theme, FileName = fileName };
        var result = await tool.RenderChartAsync(spec, request, cancellationToken).ConfigureAwait(false);
        return ToCallToolResult(result);
    }

    /// <summary>Lists the datasets that charts can use, with their fields and types.</summary>
    [McpServerTool(Name = "list_datasets", Title = "List datasets", ReadOnly = true)]
    [Description("Lists the datasets that charts can use, with their fields and types.")]
    public string ListDatasets() => tool.ListDatasets();

    /// <summary>Returns the JSON schema of a chart spec, with a description of every option.</summary>
    [McpServerTool(Name = "get_chart_schema", Title = "Chart spec schema", ReadOnly = true)]
    [Description("Returns the JSON schema of a chart spec, with a description of every option.")]
    public string GetChartSchema() => ChartTool.GetSchema();

    /// <summary>
    /// The result JSON as text, then the image: PNG as an image block, SVG as an embedded text
    /// resource (image/svg+xml), since MCP image blocks are for raster images.
    /// </summary>
    internal static CallToolResult ToCallToolResult(ChartToolResult result)
    {
        var text = JsonSerializer.Serialize(result, ChartToolJsonContext.Default.ChartToolResult);
        var content = new List<ContentBlock> { new TextContentBlock { Text = text } };
        if (result.Png is { } png)
        {
            content.Add(ImageContentBlock.FromBytes(png, "image/png"));
        }
        else if (result.Svg is { } svg)
        {
            var name = result.File is null ? "chart.svg" : Path.GetFileName(result.File);
            content.Add(new EmbeddedResourceBlock
            {
                Resource = new TextResourceContents { Uri = "riocharts://charts/" + Uri.EscapeDataString(name), MimeType = "image/svg+xml", Text = svg },
            });
        }

        return new CallToolResult { Content = content, IsError = !result.Success };
    }
}

/// <summary>Registers the RioCharts chart tools with an MCP server.</summary>
public static class RioChartsMcpServerBuilderExtensions
{
    /// <summary>Adds create_chart, render_chart, list_datasets and get_chart_schema to an MCP server.</summary>
    public static IMcpServerBuilder WithRioChartsTools(this IMcpServerBuilder builder, ChartTool tool) =>
        builder.WithTools(new RioChartsMcpTools(tool));
}
