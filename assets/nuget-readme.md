# RioCharts

A pure .NET chart library, with Blazor as the first target. C# does the math and layout, Razor
renders SVG, and a tiny JS module only measures the container and the font.

**Docs and live examples:** https://joadan.github.io/RioCharts/

> **Early preview.** APIs will change without notice before 1.0.

## Packages

| Package | What |
|---------|------|
| `RioCharts.Blazor` | `<RioChart>` with line, area, range area, bar (vertical or horizontal), scatter, heatmap, box plot, candlestick, pie, donut, radial bar, radar and treemap series; tooltips, zoom, annotations, export, keyboard and screen reader support, light and dark themes. |
| `RioCharts.Core` | The chart engine: scales, ticks, layout and a render-agnostic scene. No UI dependencies. |
| `RioCharts.Skia` | Server-side export to SVG and PNG with SkiaSharp, for reports, emails and agents. |
| `RioCharts.AI` | `create_chart` and `render_chart` tools for AI agents (Microsoft.Extensions.AI and MCP). |
| `RioCharts.Mcp` | `riocharts-mcp`: a local MCP server (.NET tool) that renders charts to PNG or SVG files and turns them into Blazor code. |

## Blazor

Reference `RioCharts.Blazor` and add `@using RioCharts.Blazor` and `@using RioCharts.Core` to
`_Imports.razor`. Styles come with `{YourApp}.styles.css` automatically.

```razor
<RioChart Items="traffic" YAxis="@(new AxisSpec { Title = "Sessions" })">
    <ReferenceLine Value="1000" Label="Campaign goal" />
    <LineSeries Name="Web" XValue="r => r.Timestamp" YValue="r => r.Sessions" />
</RioChart>
```

## Coding agents

Models do not know RioCharts yet. Paste this into your repository's `AGENTS.md` (or `CLAUDE.md`,
`.github/copilot-instructions.md`) so the agent reads the guide before it writes chart code:

```markdown
## Charts

Charts use RioCharts.Blazor (preview, .NET 10). Before writing chart code, read
https://joadan.github.io/RioCharts/llms-full.txt, or call get_usage if the riocharts MCP server is set up.

- Setup: the RioCharts.Blazor package, and `@using RioCharts.Blazor` and `@using RioCharts.Core` in `_Imports.razor`. Nothing to register.
- Set `TItem` on `<RioChart>` when it has event handlers (`OnPointClick`, ...).
- A chart updates when `Items` is a new list or `DataVersion` changes, not when a list is changed in place.
- Tooltips, zoom and clicks need an interactive render mode on the page.
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
