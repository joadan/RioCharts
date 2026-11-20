using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RioCharts.Mcp;

var (options, error) = ServerOptions.Parse(args);
if (error is not null)
{
    Console.Error.WriteLine(error);
    return 2;
}

if (options!.Help)
{
    Console.Error.WriteLine(ServerOptions.Usage);
    return 0;
}

var builder = Host.CreateApplicationBuilder();

// stdout carries the MCP protocol, so all logging goes to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var tool = LocalServer.CreateChartTool(options);
builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "riocharts", Version = typeof(LocalServer).Assembly.GetName().Version?.ToString(3) ?? "0.1.0" })
    .WithStdioServerTransport()
    .WithTools(LocalServer.CreateTools(tool));

await builder.Build().RunAsync();
return 0;
