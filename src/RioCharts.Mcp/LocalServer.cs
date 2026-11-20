using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RioCharts.AI;
using RioCharts.Core.Specs;

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
    /// render_chart, list_datasets and get_chart_schema. create_chart is left out: there is no app
    /// here to show a chart to the user, so the agent gets the image (and the file) itself.
    /// </summary>
    public static IReadOnlyList<McpServerTool> CreateTools(ChartTool tool)
    {
        var tools = new RioChartsMcpTools(tool);
        return
        [
            McpServerTool.Create(
                ([Description("The chart spec as a JSON object.")] JsonElement spec,
                 [Description("png (default) or svg.")] ChartImageFormat format = ChartImageFormat.Png,
                 [Description("Width in pixels, 100 to 4000.")] int? width = null,
                 [Description("Height in pixels, 100 to 4000.")] int? height = null,
                 [Description("light or dark.")] string? theme = null,
                 [Description("File name to save as (no folder). Default: from the title.")] string? fileName = null,
                 CancellationToken cancellationToken = default) =>
                    tools.RenderChart(spec, format, width, height, theme, fileName, cancellationToken),
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
                    Title = "Chart spec schema",
                    Description = "Returns the JSON schema of a chart spec, with a description of every option.",
                    ReadOnly = true,
                }),
        ];
    }
}
