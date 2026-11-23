using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RioCharts.AI;
using RioCharts.Core.Definitions;

namespace RioCharts.Mcp;

/// <summary>The chart tool and MCP tools of the local server.</summary>
public static class LocalServer
{
    /// <summary>The chart tool for these options: datasets from the data folder, images saved to the output folder.</summary>
    public static ChartTool CreateChartTool(ServerOptions options)
    {
        IChartDataSource source = options.DataFolder is { } folder
            ? new FolderDataSource(folder)
            : new InMemoryDataSource([]);
        return new ChartTool(source, new ChartToolOptions
        {
            Export = options.Export,
            OutputDirectory = Path.GetFullPath(options.OutputFolder),
        });
    }

    /// <summary>
    /// render_chart, list_datasets, get_chart_schema, get_razor and get_usage. create_chart is left
    /// out: there is no app here to show a chart to the user, so the agent gets the image (and the
    /// file) itself. get_razor and get_usage are for agents writing RioCharts code in an app.
    /// </summary>
    public static IReadOnlyList<McpServerTool> CreateTools(ChartTool tool)
    {
        var tools = new RioChartsMcpTools(tool);
        return
        [
            McpServerTool.Create(
                ([Description("The chart definition as a JSON object.")] JsonElement definition,
                 [Description("png (default) or svg.")] ChartImageFormat format = ChartImageFormat.Png,
                 [Description("Width in pixels, 100 to 4000.")] int? width = null,
                 [Description("Height in pixels, 100 to 4000.")] int? height = null,
                 [Description("light or dark.")] string? theme = null,
                 [Description("File name to save as (no folder). Default: from the title.")] string? fileName = null,
                 CancellationToken cancellationToken = default) =>
                    tools.RenderChart(definition, format, width, height, theme, fileName, cancellationToken),
                new McpServerToolCreateOptions
                {
                    Name = ChartTool.RenderToolName,
                    Title = "Render chart",
                    Description = ChartTool.StandaloneRenderDescription,
                }),
            McpServerTool.Create(
                () => tools.ListDatasets(),
                new McpServerToolCreateOptions
                {
                    Name = "list_datasets",
                    Title = "List datasets",
                    Description = "Lists the datasets (CSV and JSON files in the data folder) that charts can use, with their fields and types. Files are read again when they change.",
                    ReadOnly = true,
                }),
            McpServerTool.Create(
                () => tools.GetChartSchema(),
                new McpServerToolCreateOptions
                {
                    Name = "get_chart_schema",
                    Title = "Chart definition schema",
                    Description = "Returns the JSON schema of a chart definition, with a description of every option.",
                    ReadOnly = true,
                }),
            McpServerTool.Create(
                async ([Description("The chart definition as a JSON object (the same definition as render_chart).")] JsonElement definition,
                       [Description("The C# type of the rows, e.g. \"Sale\". Default: from the dataset name.")] string? itemType = null,
                       [Description("The variable or expression that holds the rows, e.g. \"sales\". Default: the dataset name.")] string? itemsName = null,
                       CancellationToken cancellationToken = default) =>
                {
                    var result = await tool.GetRazorAsync(definition, itemType, itemsName, cancellationToken).ConfigureAwait(false);
                    return new CallToolResult { Content = [new TextContentBlock { Text = result.ToMarkdown() }], IsError = !result.Success };
                },
                new McpServerToolCreateOptions
                {
                    Name = "get_razor",
                    Title = "Chart definition to Blazor code",
                    Description = "Turns a chart definition into code for a Blazor app with the RioCharts.Blazor package: the <RioChart> markup, "
                        + "a C# record for the rows (from the dataset's fields) and, for inline data, the rows as a list. "
                        + "Design the chart with render_chart and look at it first, then call this with the same definition. "
                        + "For writing RioCharts code by hand, call get_usage.",
                    ReadOnly = true,
                }),
            McpServerTool.Create(
                ([Description("Optional: one section, e.g. a component (\"RioChart\", \"LineSeries\"), a type (\"AxisOptions\") or a heading (\"Common mistakes\"). Empty: the overview.")] string? topic = null) =>
                    RioChartsUsage.Get(topic),
                new McpServerToolCreateOptions
                {
                    Name = "get_usage",
                    Title = "RioCharts usage guide",
                    Description = "How to use RioCharts in Blazor code: setup (package and usings), rules, which series to use, compiled examples "
                        + "and common mistakes; with a topic, the parameters of one component or type. Call before writing <RioChart> markup.",
                    ReadOnly = true,
                }),
        ];
    }
}
