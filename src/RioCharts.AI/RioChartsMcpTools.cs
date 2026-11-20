using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RioCharts.AI;

/// <summary>
/// The chart tools for an MCP server: create_chart (returns the PNG and a description, or errors
/// to fix), list_datasets and get_chart_schema.
/// <code>builder.Services.AddMcpServer().WithHttpTransport().WithRioChartsTools(new ChartTool(myDataSource));</code>
/// </summary>
[McpServerToolType]
public sealed class RioChartsMcpTools(ChartTool tool)
{
    [McpServerTool(Name = ChartTool.ToolName, Title = "Create chart")]
    [Description("Creates a chart from a JSON spec and returns it as a PNG with a short description, or errors to fix (then call again). "
        + "Call list_datasets first to see the datasets and fields. Minimal spec: "
        + "{\"dataset\":\"<id>\",\"series\":[{\"type\":\"line\",\"x\":\"<time field>\",\"y\":\"<numeric field>\"}]}. "
        + "Types: line, area, bar, pie, radialBar, auto. Series options: groupBy, aggregate (sum|average|min|max|count|last), "
        + "timeBucket (hour|day|week|month|quarter|year), filter, sort, limit, stack. Call get_chart_schema for every option.")]
    public async Task<CallToolResult> CreateChart(
        [Description("The chart spec as a JSON object.")] JsonElement spec,
        CancellationToken cancellationToken)
    {
        var result = await tool.CreateChartAsync(spec, cancellationToken).ConfigureAwait(false);
        var text = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
        var content = new List<ContentBlock> { new TextContentBlock { Text = text } };
        if (result.Png is { } png)
        {
            content.Add(ImageContentBlock.FromBytes(png, "image/png"));
        }

        return new CallToolResult { Content = content, IsError = !result.Success };
    }

    [McpServerTool(Name = "list_datasets", Title = "List datasets", ReadOnly = true)]
    [Description("Lists the datasets that charts can use, with their fields and types.")]
    public string ListDatasets() => tool.ListDatasets();

    [McpServerTool(Name = "get_chart_schema", Title = "Chart spec schema", ReadOnly = true)]
    [Description("Returns the JSON schema of a chart spec, with a description of every option.")]
    public string GetChartSchema() => ChartTool.GetSchema();
}

public static class RioChartsMcpServerBuilderExtensions
{
    /// <summary>Adds create_chart, list_datasets and get_chart_schema to an MCP server.</summary>
    public static IMcpServerBuilder WithRioChartsTools(this IMcpServerBuilder builder, ChartTool tool) =>
        builder.WithTools(new RioChartsMcpTools(tool));
}
