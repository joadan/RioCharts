# RioCharts

A pure .NET chart library, with Blazor as the first target. C# does the math and layout, Razor
renders SVG, and a tiny JS module only measures the container and the font.

**Docs and live examples:** https://joadan.github.io/RioCharts/

> **Early preview.** APIs will change without notice before 1.0.

## Packages

| Package | What |
|---------|------|
| `RioCharts.Blazor` | `<RioChart>` with line, area, bar, pie, donut and radial bar series, tooltips, zoom, annotations, light and dark themes. |
| `RioCharts.Core` | The chart engine: scales, ticks, layout and a render-agnostic scene. No UI dependencies. |
| `RioCharts.Skia` | Server-side export to SVG and PNG with SkiaSharp, for reports, emails and agents. |
| `RioCharts.AI` | `create_chart` and `render_chart` tools for AI agents (Microsoft.Extensions.AI and MCP). |
| `RioCharts.Mcp` | `riocharts-mcp`: a local MCP server (.NET tool) that renders charts to PNG or SVG files. |

## Blazor

Reference `RioCharts.Blazor` and add `@using RioCharts.Blazor` and `@using RioCharts.Core` to
`_Imports.razor`. Styles come with `{YourApp}.styles.css` automatically.

```razor
<RioChart Items="traffic" YAxis="new AxisSpec { Title = "Sessions" }">
    <ReferenceLine Value="1000" Label="Campaign goal" />
    <LineSeries Name="Web" XValue="r => r.Timestamp" YValue="r => r.Sessions" />
</RioChart>
```

## Server-side export

```csharp
byte[] png = ChartExport.ToPng(spec, data, new ExportOptions { Width = 640, Height = 320 });
```

## MCP server for agents

```
dotnet tool install -g RioCharts.Mcp --prerelease
```

```json
{ "mcpServers": { "riocharts": { "command": "riocharts-mcp", "args": ["--data", "data", "--out", "charts"] } } }
```

Source, changelog and issues: https://github.com/joadan/RioCharts (MIT license).
