using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Specs;

namespace RioCharts.Tests;

public class ChartToolTests
{
    private static readonly ChartDataset Weather = ChartDataset.FromRows("weather",
        Enumerable.Range(0, 72).Select(h => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["timestamp"] = new DateTime(2026, 11, 16).AddHours(h),
            ["station"] = h % 2 == 0 ? "Station 1" : "Station 2",
            ["airTemp"] = 19 + Math.Sin(h / 6.0),
            ["rain"] = 10.0 + h % 7,
        }),
        "Weather stations, hourly",
        new Dictionary<string, string> { ["airTemp"] = "air temperature in °C" });

    private static ChartTool Tool() => new(new InMemoryDataSource([Weather]));

    [Fact]
    public async Task Create_chart_renders_and_describes_a_valid_spec()
    {
        ChartToolResult? created = null;
        var tool = Tool();
        tool.ChartCreated += r => created = r;

        var result = await tool.CreateChartAsync("""{"dataset":"weather","title":"Air temperature","series":[{"type":"spline","x":"timestamp","y":"airTemp","groupBy":"station"}]}""");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.NotNull(result.Png);
        Assert.Contains("Air temperature", result.Summary);
        Assert.Contains(result.Warnings, w => w.Contains("spline", StringComparison.Ordinal));
        Assert.Equal(2, result.BoundSpec!.Series.Count);
        Assert.Same(result, created);
        Assert.Equal(ChartTool.ToolName, created!.Tool);
        var json = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
        Assert.DoesNotContain("png", json, StringComparison.OrdinalIgnoreCase); // the image is not sent to the model
    }

    [Fact]
    public async Task Create_chart_returns_errors_the_model_can_act_on()
    {
        var result = await Tool().CreateChartAsync("""{"dataset":"weather","series":[{"type":"line","x":"time","y":"airTemp"}]}""");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.StartsWith("series[0].x: field 'time' not found", StringComparison.Ordinal) && e.Contains("timestamp (time)", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("call create_chart again", StringComparison.Ordinal));
        Assert.Null(result.Png);
    }

    [Fact]
    public void Description_lists_the_datasets_fields_and_examples()
    {
        var description = Tool().Description;

        Assert.Contains("- weather: Weather stations, hourly (72 rows)", description);
        Assert.Contains("airTemp (number, air temperature in °C)", description);
        Assert.Contains("\"timeBucket\"", description);
    }

    [Fact]
    public async Task Works_as_an_ai_function_with_a_json_object_or_string_argument()
    {
        var functions = Tool().AsAIFunctions();
        var create = (AIFunction)functions.Single(f => f.Name == "create_chart");
        var spec = JsonDocument.Parse("""{"dataset":"weather","series":[{"type":"bar","x":"timestamp","y":"rain","timeBucket":"day","aggregate":"sum"}]}""").RootElement;

        var fromObject = await create.InvokeAsync(new AIFunctionArguments { ["spec"] = spec });
        var fromString = await create.InvokeAsync(new AIFunctionArguments { ["spec"] = spec.GetRawText() });

        Assert.Contains("\"success\":true", JsonSerializer.Serialize(fromObject));
        Assert.Contains("\"success\":true", JsonSerializer.Serialize(fromString));
        Assert.Contains("spec", create.JsonSchema.GetRawText());
        Assert.Equal(["create_chart", "render_chart", "list_datasets", "get_chart_schema"], functions.Select(f => f.Name));
    }

    [Fact]
    public async Task Mcp_tool_returns_the_image_and_flags_errors()
    {
        var tools = new RioChartsMcpTools(Tool());
        var ok = await tools.CreateChart(JsonDocument.Parse("""{"dataset":"weather","series":[{"x":"timestamp","y":"airTemp"}]}""").RootElement, default);
        var bad = await tools.CreateChart(JsonDocument.Parse("""{"dataset":"nope","series":[{"x":"a","y":"b"}]}""").RootElement, default);

        Assert.False(ok.IsError);
        Assert.Contains(ok.Content, c => c is ImageContentBlock { MimeType: "image/png" });
        Assert.True(bad.IsError);
        Assert.Contains("available: weather", ((TextContentBlock)bad.Content[0]).Text);
        Assert.Contains("\"title\"", tools.GetChartSchema());
    }

    [Fact]
    public async Task Render_chart_returns_png_or_svg_at_the_requested_size_and_theme()
    {
        var created = new List<ChartToolResult>();
        var tool = Tool();
        tool.ChartCreated += created.Add;
        const string spec = """{"dataset":"weather","series":[{"x":"timestamp","y":"airTemp"}]}""";

        var png = await tool.RenderChartAsync(spec, new ChartImageRequest { Width = 400, Height = 200 });
        var svg = await tool.RenderChartAsync(spec, new ChartImageRequest { Format = ChartImageFormat.Svg, Theme = "dark" });

        Assert.True(png.Success, string.Join("; ", png.Errors));
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png.Png![..4]);
        Assert.Equal(800, BitConverter.ToInt32([.. png.Png[16..20].Reverse()])); // 400 × scale 2
        Assert.Null(png.Svg);
        Assert.Equal("png", png.Format);
        Assert.Equal("svg", svg.Format);
        Assert.Null(svg.Png);
        Assert.StartsWith("<svg", svg.Svg!.TrimStart());
        Assert.Contains("width=\"800\"", svg.Svg);
        Assert.Equal([png, svg], created); // the app sees what the agent made too
        Assert.All(created, r => Assert.Equal(ChartTool.RenderToolName, r.Tool));
        Assert.Null(png.File);
    }

    [Fact]
    public async Task Render_chart_rejects_bad_options_and_names_itself_in_errors()
    {
        var tool = Tool();
        var created = 0;
        tool.ChartCreated += _ => created++;

        var bad = await tool.RenderChartAsync("""{"dataset":"weather","series":[{"x":"timestamp","y":"airTemp"}]}""",
            new ChartImageRequest { Width = 20000, Theme = "neon" });
        var badSpec = await tool.RenderChartAsync("""{"dataset":"nope","series":[{"x":"a","y":"b"}]}""");

        Assert.False(bad.Success);
        Assert.Contains(bad.Errors, e => e.StartsWith("width: 20000 is out of range", StringComparison.Ordinal));
        Assert.Contains(bad.Errors, e => e.StartsWith("theme: 'neon'", StringComparison.Ordinal));
        Assert.Contains(badSpec.Errors, e => e.Contains("call render_chart again", StringComparison.Ordinal));
        Assert.Equal(0, created); // only charts that were made
    }

    [Fact]
    public async Task Render_chart_saves_to_the_output_directory_with_a_safe_name()
    {
        var directory = Path.Combine(Path.GetTempPath(), "riocharts-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var tool = new ChartTool(new InMemoryDataSource([Weather]), new ChartToolOptions { OutputDirectory = directory });
            string? createdFile = null;
            tool.ChartCreated += r => createdFile ??= r.File;
            const string spec = """{"dataset":"weather","title":"Air temp / Station 1","series":[{"x":"timestamp","y":"airTemp"}]}""";

            var fromTitle = await tool.RenderChartAsync(spec);
            var named = await tool.RenderChartAsync(spec, new ChartImageRequest { Format = ChartImageFormat.Svg, FileName = "../../evil.png" });

            Assert.Equal(Path.Combine(directory, "air-temp---station-1.png"), fromTitle.File);
            Assert.Equal(fromTitle.File, createdFile); // raised after the file is written
            Assert.Equal(Path.Combine(directory, "evil.svg"), named.File);
            Assert.Equal(fromTitle.Png, await File.ReadAllBytesAsync(fromTitle.File!));
            Assert.Equal(named.Svg, await File.ReadAllTextAsync(named.File!));
            Assert.Contains("\"file\":", JsonSerializer.Serialize(fromTitle, JsonSerializerOptions.Web));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Render_chart_as_ai_function_and_mcp_tool_returns_the_image()
    {
        var tool = Tool();
        var render = (AIFunction)tool.AsAIFunctions().Single(f => f.Name == "render_chart");
        var spec = JsonDocument.Parse("""{"data":[{"category":"Shoes","share":45},{"category":"Jackets","share":55}],"series":[{"type":"auto","x":"category","y":"share"}]}""").RootElement;

        var aiResult = await render.InvokeAsync(new AIFunctionArguments { ["spec"] = spec, ["format"] = "svg" });
        var mcp = new RioChartsMcpTools(tool);
        var mcpPng = await mcp.RenderChart(spec, cancellationToken: default);
        var mcpSvg = await mcp.RenderChart(spec, ChartImageFormat.Svg, cancellationToken: default);

        var contents = Assert.IsAssignableFrom<IEnumerable<AIContent>>(aiResult).ToList();
        Assert.Contains("\"success\":true", ((TextContent)contents[0]).Text);
        Assert.Equal("image/svg+xml", Assert.IsType<DataContent>(contents[1]).MediaType);
        Assert.Contains(mcpPng.Content, c => c is ImageContentBlock { MimeType: "image/png" });
        var resource = Assert.IsType<TextResourceContents>(mcpSvg.Content.OfType<EmbeddedResourceBlock>().Single().Resource);
        Assert.Equal("image/svg+xml", resource.MimeType);
        Assert.StartsWith("<svg", resource.Text.TrimStart());
    }

    [Fact]
    public void Specs_export_to_razor_markup()
    {
        var spec = ChartSpecJson.Parse("""
            { "title": "Weather \"now\"", "y": { "title": "°C" },
              "series": [ { "type": "line", "x": "timestamp", "y": "air_temp", "name": "Temperature", "curve": "smooth", "groupBy": "station" },
                          { "type": "doughnut", "x": "category", "y": "share" } ],
              "annotations": [ { "value": 21, "label": "Average" }, { "type": "band", "axis": "x", "from": "2026-11-16T06:00", "to": "2026-11-16T09:00" } ] }
            """).Spec!;

        var razor = ChartRazor.FromSpec(spec, "Reading", "readings");

        Assert.StartsWith("<RioChart TItem=\"Reading\" Items=\"readings\" Title=\"Weather &quot;now&quot;\"", razor);
        Assert.Contains("<LineSeries Name=\"Temperature\" XValue=\"r => r.Timestamp\" YValue=\"r => r.AirTemp\" Curve=\"CurveType.Monotone\" />", razor);
        Assert.Contains("<DonutSeries XValue=\"r => r.Category\" YValue=\"r => r.Share\" />", razor);
        Assert.Contains("<ReferenceLine Value=\"21\" Label=\"Average\" />", razor);
        Assert.Contains("@* one series per station", razor);
        Assert.Contains("DateTime.Parse(\"2026-11-16T06:00\")", razor);
    }
}
