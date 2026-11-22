using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using RioCharts.AI;
using RioCharts.Core.Specs;
using RioCharts.Mcp;

namespace RioCharts.Tests;

public sealed class LocalMcpServerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "riocharts-mcp-" + Guid.NewGuid().ToString("N"));

    public LocalMcpServerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Csv_reads_types_quotes_and_decimal_commas()
    {
        var comma = ChartDataset.FromCsv("a", "﻿time,station,temp\r\n2026-11-16T08:00,\"Station, 1\",21.5\r\n2026-11-16T09:00,\"Station \"\"2\"\"\",\r\n");
        var semicolon = ChartDataset.FromCsv("b", "station;temp;;temp\nStation 1;21,5;x;1\n\n");

        Assert.Equal(FieldType.Time, comma.Field("time")!.Type);
        Assert.Equal(["Station, 1", "Station \"2\""], comma.Field("station")!.Values);
        Assert.Equal(FieldType.Number, comma.Field("temp")!.Type);
        Assert.Equal([21.5, null], comma.Field("temp")!.Values);
        Assert.Equal(1, semicolon.RowCount);
        Assert.Equal(["station", "temp", "column3", "temp_2"], semicolon.Fields.Select(f => f.Name));
        Assert.Equal([21.5], semicolon.Field("temp")!.Values);
    }

    [Fact]
    public void Csv_edge_cases_do_not_throw()
    {
        Assert.Empty(ChartDataset.FromCsv("e", "").Fields);
        Assert.Equal(["x", "y"], ChartDataset.FromCsv("h", "x,y").Fields.Select(f => f.Name));
        Assert.Equal(FieldType.Text, ChartDataset.FromCsv("m", "v\n1\nabc").Field("v")!.Type);
        Assert.Equal(FieldType.Text, ChartDataset.FromCsv("c", "a,b\n\"1,5\",2").Field("a")!.Type); // decimal comma only with ; or tab
    }

    [Theory]
    [InlineData("2026-03-01")]
    [InlineData("2026-03-01T12:30")]
    [InlineData("2026-03-01 12:30:45")]
    [InlineData("2026-03-01T12:30:45.5")]
    [InlineData("2026-03-01T12:30:45.1234567")]
    [InlineData("2024-02-29T23:59:59")]
    public void Csv_times_read_like_DateTime_parse(string cell)
    {
        var dataset = ChartDataset.FromCsv("t", "time\n" + cell);

        Assert.Equal(DateTime.Parse(cell, CultureInfo.InvariantCulture), Assert.IsType<DateTime>(dataset.Field("time")!.Values[0]));
    }

    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("2026-13-01")]
    [InlineData("2026-03-01T24:00")]
    public void Csv_impossible_dates_stay_text(string cell) =>
        Assert.Equal(FieldType.Text, ChartDataset.FromCsv("t", "time\n" + cell).Field("time")!.Type);

    [Fact]
    public async Task Folder_lists_files_rereads_changes_and_reports_bad_files()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "energy.csv"), "day,kwh\n2026-11-16,10\n2026-11-17,12\n");
        await File.WriteAllTextAsync(Path.Combine(_root, "stations.json"), """{ "rows": [ { "station": "Station 1", "temp": 21 } ] }""");
        await File.WriteAllTextAsync(Path.Combine(_root, "broken.json"), "{ nope");
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.txt"), "ignored");
        var source = new FolderDataSource(_root);

        Assert.Equal(["broken", "energy", "stations"], source.Describe().Select(d => d.Name));
        Assert.Contains("could not be read", source.Describe()[0].Description);
        Assert.Null(await source.LoadAsync("broken"));
        Assert.Equal(2, (await source.LoadAsync("ENERGY"))!.RowCount);

        var file = Path.Combine(_root, "energy.csv");
        await File.WriteAllTextAsync(file, "day,kwh\n2026-11-16,10\n2026-11-17,12\n2026-11-18,9\n");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(3, (await source.LoadAsync("energy"))!.RowCount);
        Assert.Empty(new FolderDataSource(Path.Combine(_root, "missing")).Describe());
    }

    [Fact]
    public void Options_parse_and_report_errors()
    {
        var (options, error) = ServerOptions.Parse(["--data", "d", "--out", "o", "--width", "640", "--theme", "Dark", "--culture", "sv-SE"]);

        Assert.Null(error);
        Assert.Equal("d", options!.DataFolder);
        Assert.Equal("o", options.OutputFolder);
        Assert.Equal(640, options.Export.Width);
        Assert.True(options.Dark);
        Assert.Equal(CultureInfo.GetCultureInfo("sv-SE"), options.Export.Culture);
        Assert.True(ServerOptions.Parse(["--help"]).Options!.Help);
        Assert.Contains("100 to 4000", ServerOptions.Parse(["--width", "5"]).Error);
        Assert.Contains("Unknown", ServerOptions.Parse(["--bogus", "1"]).Error);
        Assert.Contains("incomplete", ServerOptions.Parse(["--data"]).Error);
        Assert.Equal("charts", ServerOptions.Parse([]).Options!.OutputFolder);
    }

    [Fact]
    public async Task Server_tools_render_files_from_folder_data()
    {
        var data = Directory.CreateDirectory(Path.Combine(_root, "data")).FullName;
        await File.WriteAllTextAsync(Path.Combine(data, "energy.csv"), "day;kwh\n2026-11-16;10,5\n2026-11-17;12\n");
        var options = new ServerOptions { DataFolder = data, OutputFolder = Path.Combine(_root, "out") };
        var tools = LocalServer.CreateTools(LocalServer.CreateChartTool(options));

        Assert.Equal(["render_chart", "list_datasets", "get_chart_schema"], tools.Select(t => t.ProtocolTool.Name));
        Assert.Contains("list_datasets", tools[0].ProtocolTool.Description);
        Assert.DoesNotContain("create_chart", tools[0].ProtocolTool.Description);

        var tool = new RioChartsMcpTools(LocalServer.CreateChartTool(options));
        Assert.Contains("energy: From energy.csv (2 rows)", tool.ListDatasets());
        var spec = JsonDocument.Parse("""{"dataset":"energy","title":"Energy","series":[{"type":"bar","x":"day","y":"kwh"}]}""").RootElement;
        var result = await tool.RenderChart(spec, ChartImageFormat.Svg, cancellationToken: default);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement;
        var file = json.GetProperty("file").GetString()!;
        Assert.Equal(Path.Combine(_root, "out", "energy.svg"), file);
        Assert.StartsWith("<svg", (await File.ReadAllTextAsync(file)).TrimStart());
    }
}
