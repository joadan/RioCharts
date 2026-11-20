using RioCharts.AI;
using RioCharts.Core.Specs;
using RioCharts.Demo;
using RioCharts.Demo.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// AI chart creation: the create_chart tool over the demo datasets, also as an MCP server at /mcp
// (point an MCP client such as Claude at http://localhost:5030/mcp).
var chartTool = new ChartTool(new InMemoryDataSource(DemoDatasets.All));
builder.Services.AddSingleton(chartTool);
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithRioChartsTools(chartTool);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapChartExport();
app.MapGet("/ai/schema.json", () => Results.Text(ChartTool.GetSchema(), "application/schema+json"));
app.MapGet("/ai/tool.txt", (ChartTool tool) => Results.Text(tool.Description, "text/plain; charset=utf-8"));
app.MapMcp("/mcp");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
