<h1><img src="assets/icon.svg" alt="" width="40" height="40" align="top" /> RioCharts</h1>

[![NuGet](https://img.shields.io/nuget/vpre/RioCharts.Blazor?label=NuGet)](https://www.nuget.org/packages/RioCharts.Blazor)
[![Tests](https://github.com/joadan/RioCharts/actions/workflows/test.yml/badge.svg)](https://github.com/joadan/RioCharts/actions/workflows/test.yml)
[![.NET 10 | 11](https://img.shields.io/badge/.NET-10%20%7C%2011-512bd4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![Docs](https://img.shields.io/badge/docs-riocharts.com-4269d0)](https://riocharts.com/)

**Charts for .NET.** One chart model, drawn everywhere: interactive in Blazor, PNG and SVG on the
server, and created by AI agents. C# does the math and layout; in Blazor, Razor renders SVG and a
tiny JS module only measures text and follows the pointer.

![Four charts made with RioCharts: sessions per day as an area and a line against a goal, orders per quarter as stacked bars, revenue by channel as a donut, and sessions per hour and weekday as a heatmap.](assets/readme-charts-light.png#gh-light-mode-only)
![Four charts made with RioCharts: sessions per day as an area and a line against a goal, orders per quarter as stacked bars, revenue by channel as a donut, and sessions per hour and weekday as a heatmap.](assets/readme-charts-dark.png#gh-dark-mode-only)

<sub>Drawn by RioCharts.Skia itself (<a href="tools/ReadmeImage.cs">tools/ReadmeImage.cs</a>).
**[See them live, with tooltips and zoom →](https://riocharts.com/)**</sub>

> **Status: 1.0 release candidate.** The 1.0 API is frozen: until 1.0 ships only fixes and non-breaking
> additions go in; the [changelog](CHANGELOG.md) lists every change. From 1.0, updates within 1.x never break code that uses the public API or saved chart definitions
> ([versions and support](https://riocharts.com/versions)). It is built for our own teams' needs; issues are
> welcome, but there is no roadmap promise.
> Human-led, AI-built: the design is ours ([Design.md](Design.md)); most code is written by AI
> coding agents and tested and snapshot-checked before it ships.

## Blazor

```
dotnet add package RioCharts.Blazor --prerelease
```

Add `@using RioCharts.Blazor` and `@using RioCharts.Core` to `_Imports.razor`. That is all: no
services to register, and the styles come with your app's `{App}.styles.css` bundle.

```razor
<RioChart Items="orders" Title="Orders per day">
    <ReferenceLine Value="1000" Label="Goal" />
    <LineSeries Name="Web" XValue="o => o.Day" YValue="o => o.Web" />
    <BarSeries Name="App" XValue="o => o.Day" YValue="o => o.App" />
</RioChart>

@code {
    record DailyOrders(DateTime Day, double Web, double App);
    List<DailyOrders> orders = ...;
}
```

The axes, ticks, number and date formats, legend and colors come from the data; options are typed
parameters. Change `Items` and the chart draws again. It works on Server, WebAssembly and Auto, with
static rendering and prerendering, trimmed and native AOT, and in .NET MAUI Blazor Hybrid apps.

**Series:** line, area, range area, bar (grouped, stacked, horizontal), scatter with trend lines, bubble,
heatmap, box plot, histogram, dumbbell, range bar (timeline), candlestick, waterfall, pie, donut, radial bar, gauge, bullet, funnel, radar, treemap and world map.

**Also in the box:** tooltips and crosshair, zoom and pan, linked charts, annotations, error bars, data labels,
click events, live data, large data (LTTB and a canvas layer), time zones, light and dark themes
through CSS variables, PNG/SVG/CSV download, keyboard navigation, screen reader descriptions and
built-in texts in eight languages.

Coming from Blazor-ApexCharts? Both libraries have the same creator, and the concepts map closely: see
[the mapping](https://riocharts.com/from-apexcharts). Blazor-ApexCharts is production ready and
battle-proven; RioCharts is a release candidate for 1.0. It is an alternative
approach, not an attempt to match Blazor-ApexCharts feature for feature.

## Images on the server

`RioCharts.Skia` draws the same charts as PNG or SVG without a browser, for reports, emails and
bots, with real font metrics and light or dark themes:

```csharp
var spec = new ChartDefinition { Title = "Sessions", Series = [new SeriesOptions { Id = "web", Name = "Web" }] };
var data = new Dictionary<string, SeriesData> { ["web"] = SeriesData.From(times, values) };
byte[] png = ChartExport.ToPng(spec, data, new ExportOptions { Width = 640, Height = 320 });
```

## AI agents

**In your app.** `RioCharts.AI` gives your agent `create_chart` and `render_chart` tools, as
Microsoft.Extensions.AI functions or MCP server tools. The agent writes a small JSON definition against
your datasets; the tool validates it (errors name the path and the valid options), renders it and
describes it:

```csharp
var tool = new ChartTool(new InMemoryDataSource([ChartDataset.FromItems("shop", rows, "Online store traffic per channel, hourly")]));
var chat = new ChatOptions { Tools = tool.AsAIFunctions() };
tool.ChartCreated += result => Show(result.Png);
```

**On your machine.** `riocharts-mcp` is a local MCP server for Claude Code, Claude Desktop, GitHub
Copilot and other agents. It renders charts from CSV and JSON files to PNG or SVG, and turns a
chart into `<RioChart>` markup for your app:

```
dotnet tool install -g RioCharts.Mcp --prerelease
```

```json
{ "mcpServers": { "riocharts": { "command": "riocharts-mcp", "args": ["--data", "data", "--out", "charts"] } } }
```

Try it in [samples/RioCharts.Mcp.Sample](samples/RioCharts.Mcp.Sample): sample data, agent
configuration and example prompts.

**Coding agents.** Models do not know RioCharts yet. Paste this into your repository's
`AGENTS.md` (or `CLAUDE.md`, `.github/copilot-instructions.md`):

```markdown
## Charts

Charts use RioCharts.Blazor (1.0 release candidate, .NET 10 and 11). Before writing chart code, read
https://riocharts.com/llms-full.txt, or call get_usage if the riocharts MCP server is set up.

- Setup: the RioCharts.Blazor package, and `@using RioCharts.Blazor` and `@using RioCharts.Core` in `_Imports.razor`. Nothing to register.
- Set `TItem` on `<RioChart>` when it has event handlers (`OnPointClick`, ...).
- A chart updates when `Items` is a new list or `DataVersion` changes, not when a list is changed in place.
- Tooltips, zoom and clicks need an interactive render mode on the page.
```

## Packages

All target .NET 10 (LTS) and .NET 11, and are release candidates until 1.0: add `--prerelease`.

| Package | For |
| --- | --- |
| [RioCharts.Core](https://www.nuget.org/packages/RioCharts.Core) | The chart model, data types and JSON chart definitions, with no UI dependencies (the other packages include it) |
| [RioCharts.Blazor](https://www.nuget.org/packages/RioCharts.Blazor) | The `<RioChart>` component and its series (includes Core) |
| [RioCharts.Skia](https://www.nuget.org/packages/RioCharts.Skia) | PNG and SVG on the server |
| [RioCharts.AI](https://www.nuget.org/packages/RioCharts.AI) | `create_chart` and `render_chart` tools for AI agents |
| [RioCharts.Mcp](https://www.nuget.org/packages/RioCharts.Mcp) | `riocharts-mcp`, a local MCP server (a .NET tool) |

## Learn more

- [Docs and live examples](https://riocharts.com/): every chart type and feature, running in your browser
- [Chart definitions](https://riocharts.com/definitions): the chart model as JSON, with a playground
- [CHANGELOG.md](CHANGELOG.md): what changed, and breaking changes
- [CONTRIBUTING.md](CONTRIBUTING.md): building, testing and the project layout
- [Design.md](Design.md): the design behind it

Named after Rio, and *río* means river: fitting for flowing lines and time series.

## License

[MIT](LICENSE) © 2026 Joakim Dangården
