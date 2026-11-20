<img src="assets/icon.svg" alt="" width="64" height="64" align="left" />

# RioCharts

<br clear="left" />

A pure .NET chart library, with Blazor as the first target. C# does the math and layout,
Razor renders SVG, and a tiny JS module only measures the container and the font.

**Docs and live examples:** https://joadan.github.io/RioCharts/ (Blazor WebAssembly, runs in your browser).

> **Status: early preview.** APIs will change without notice before 1.0. It is built for our
> own teams' needs; issues are welcome, but there is no roadmap promise.

Named after Rio, and *río* means river: fitting for flowing lines and time series.

## What works today (milestones 1–6)

- Line, area and bar series (grouped bars), with linear, monotone and step lines
- Linear, time and category axes, picked automatically from the data
- Nice ticks; time ticks that switch between minutes, hours, days, weeks, months and years
- Compact, culture-aware labels (1.2k, 3.4M); dates adapt to the visible range
- Gaps for missing values; clean states for empty data, a single point and flat data
- Legend (shown when there is more than one series); click to hide and show series
- Responsive width with real font measurement; fixed height (300 px by default)
- Light and dark themes through CSS variables; colorblind-aware palette
- LTTB downsampling for large series, and a columnar fast path for big data
- Screen-reader description of every chart (series, extremes, trend)
- Tooltip and crosshair (shared across series by default), handled in the browser
- Stacked bars, areas and lines; mixed series; multiple Y axes
- Data labels with collision avoidance
- Reference lines and bands (setpoints, comfort zones, fault periods)
- Point and annotation click events
- Pie, donut (also half donut and nested rings) and radial bar, with inside and outside labels,
  leader lines and "Other" grouping
- Zoom and pan on the x axis (drag, Shift+drag, Ctrl+wheel), `@bind-VisibleRange`, linked charts
  sharing crosshair and zoom (`Group`)
- Live data from a `SeriesBuffer` (append from any thread, time window, capped redraw rate)
- Point markers, callouts and draggable reference lines (`@bind-Value`)
- Display time zone (`TimeZone`): plotted on instants, labelled in the zone, DST-safe
- Canvas series layer for large data (`SeriesLayer="SeriesLayer.Canvas"`)
- Server-side export to SVG and PNG (`RioCharts.Skia`), with real font metrics and light/dark themes
- JSON chart specs for AI agents: tolerant parsing, JSON schema, validation with actionable errors,
  dataset fields with filter, time buckets, aggregation, groupBy, sort and limit, `"type": "auto"`
- `create_chart` and `render_chart` (the image as PNG or SVG for the agent itself) as Microsoft.Extensions.AI
  `AIFunction`s and as MCP server tools (`RioCharts.AI`),
  spec → Razor export, `<Chart Spec="...">`
- `riocharts-mcp`: a local MCP server (.NET tool) so agents such as Claude Code, Claude Desktop or
  Copilot render charts to PNG or SVG files from CSV/JSON files or inline data (`RioCharts.Mcp`)

All six milestones of [Design.md](Design.md) are in. Later topics there (keyboard navigation,
client-side export, a WASM demo site) are not done yet.

## Usage

Reference `RioCharts.Blazor` and add `@using RioCharts.Blazor` and `@using RioCharts.Core`
to `_Imports.razor`. Styles come with `{YourApp}.styles.css` automatically.

```razor
<Chart Items="readings">
    <LineSeries XValue="r => r.Timestamp" YValue="r => r.SupplyTemp" />
</Chart>
```

Several series, options and two-way bound legend state:

```razor
<Chart TItem="Reading" Title="Air handling unit" YAxis="new AxisSpec { Title = "°C" }" @bind-HiddenSeries="hidden">
    <LineSeries Items="supply" Name="Supply" XValue="r => r.Time" YValue="r => r.Value" Curve="CurveType.Monotone" />
    <AreaSeries Items="extract" Name="Extract" XValue="r => r.Time" YValue="r => r.Value" />
    <BarSeries Items="energy" Name="Energy" XValue="r => r.Time" YValue="r => r.Value" />
</Chart>
```

- `XValue` may return a number, `DateTime`, `DateTimeOffset`, `DateOnly` or `string` (category).
- `YValue` returns `double?`; `null` leaves a gap. Cast `decimal` values to `double`.
- Data is processed when `Items` is a new reference or `DataVersion` changes, not on every render.

Stacking, a second axis, data labels and annotations:

```razor
<Chart TItem="Reading" YAxis="new AxisSpec { Title = "°C" }" YAxes="[new AxisSpec { Id = "energy", Title = "kWh" }]"
       OnPointClick="e => selected = e.Item">
    <ReferenceBand From="20" To="24" Label="Comfort zone" />
    <ReferenceLine Value="21" Label="Setpoint" />
    <ReferenceBand Axis="AnnotationAxis.X" From="faultStart" To="faultEnd" Label="Fan fault" />
    <BarSeries Items="heating" Name="Heating" XValue="r => r.Time" YValue="r => r.Value" YAxisId="energy" Stack="use" />
    <BarSeries Items="fans" Name="Fans" XValue="r => r.Time" YValue="r => r.Value" YAxisId="energy" Stack="use" DataLabels="true" />
    <LineSeries Items="supply" Name="Supply" XValue="r => r.Time" YValue="r => r.Value" Format="0.0 °C" />
</Chart>
```

Pie, donut and radial bar (the x value names each slice; the legend lists slices):

```razor
<Chart Items="use">
    <DonutSeries Name="Energy use" XValue="u => u.System" YValue="u => u.MWh" GroupSmallSlices="0.03" />
</Chart>

<Chart Items="health">
    <RadialBarSeries XValue="h => h.Component" YValue="h => h.Percent" Max="100" Format="0'%'" />
</Chart>
```

AI chart creation, with `RioCharts.AI`: agents write a small JSON spec against your datasets; the
tool validates it (errors name the path and the valid options), renders it and describes it:

```csharp
var tool = new ChartTool(new InMemoryDataSource([ChartDataset.FromItems("ahu", readings, "Air handling units, hourly")]));
var chat = new ChatOptions { Tools = tool.AsAIFunctions() };          // Microsoft.Extensions.AI
builder.Services.AddMcpServer().WithHttpTransport().WithRioChartsTools(tool); // or as an MCP server
tool.ChartCreated += result => Show(result.Png);
```

When the agent needs the image itself (for a report, an email, a document), it calls `render_chart`
with the same spec plus `format` (png or svg), `width`, `height` and `theme`; the image comes back
as content (and is saved to `ChartToolOptions.OutputDirectory` when set).

For local agents (Claude Code, Claude Desktop, GitHub Copilot in VS Code) there is a ready-made
MCP server. Install it and register it with the agent; CSV and JSON files in `--data` become
datasets, and charts are saved to `--out` (default `./charts`):

```
dotnet tool install -g RioCharts.Mcp --prerelease
```

```json
{ "mcpServers": { "riocharts": { "command": "riocharts-mcp", "args": ["--data", "data", "--out", "charts"] } } }
```

To try it, open [samples/RioCharts.Mcp.Sample](samples/RioCharts.Mcp.Sample) in Claude Code or VS Code: it has
sample data, the agent configuration and example prompts.

The same specs work in Blazor: `<Chart TItem="object" Spec="spec" Datasets="datasets" />`, e.g.
`{"dataset":"ahu","series":[{"type":"line","x":"timestamp","y":"supplyTemp","groupBy":"unit"}]}`.

Server-side export (no browser), with `RioCharts.Skia`: the same spec and data, as PNG or SVG:

```csharp
var spec = new ChartSpec { Title = "Supply air", Series = [new SeriesSpec { Id = "supply", Name = "Supply" }] };
var data = new Dictionary<string, SeriesData> { ["supply"] = SeriesData.From(times, values) };
byte[] png = ChartExport.ToPng(spec, data, new ExportOptions { Width = 640, Height = 320, Theme = ExportTheme.Dark });
string svg = ChartExport.ToSvg(spec, data);
```

Large data, as plain arrays (no per-item cost):

```razor
<Chart TItem="double">
    <LineSeries Name="Sensor" Data="SeriesData.From(timestamps, values)" />
</Chart>
```

Theming: set any of `--rio-text`, `--rio-text-muted`, `--rio-grid`, `--rio-axis`,
`--rio-surface` (tooltip), `--rio-annotation`, `--rio-data-label-inverse` and
`--rio-series-1` … `--rio-series-10` anywhere above the chart. Dark mode follows
`prefers-color-scheme`, or an ancestor with `data-theme="dark"` or class `dark`.

Times are shown exactly as given by default. Set `TimeZone` ("UTC", "local" or an IANA id) to
plot on instants and label in that zone.

## Projects

| Path | What |
|------|------|
| `src/RioCharts.Core` | Scales, ticks, layout, path builders, scene, hit-testing, chart spec. No UI dependencies. |
| `src/RioCharts.Blazor` | `<Chart>` and series components, SVG scene renderer, JS module. |
| `src/RioCharts.Skia` | Server-side export to SVG and PNG with SkiaSharp (`ChartExport`). |
| `src/RioCharts.AI` | `create_chart` and `render_chart` tools for agents (Microsoft.Extensions.AI and MCP), schema, spec → Razor. |
| `src/RioCharts.Mcp` | `riocharts-mcp`: local MCP server (stdio, .NET tool) that renders charts to PNG/SVG files. |
| `tests/RioCharts.Tests` | xUnit tests for Core and the SVG writer. |
| `samples/RioCharts.Demo` | Blazor Server demo of everything above. |
| `samples/RioCharts.Mcp.Sample` | Sample data and agent config (`.mcp.json`, `.vscode/mcp.json`) for trying `riocharts-mcp`. |
| `samples/RioCharts.Docs` | Docs site with live examples (Blazor WebAssembly, published to GitHub Pages). |

```powershell
dotnet test
dotnet run --project samples/RioCharts.Demo --launch-profile http
dotnet run --project samples/RioCharts.Docs --launch-profile http   # docs site, http://localhost:5031
```

See [CHANGELOG.md](CHANGELOG.md) for changes.

## Releasing

Push a tag `v<version>` (e.g. `v0.1.0-preview.1`); [nuget.yml](.github/workflows/nuget.yml) tests,
packs and publishes all packages to nuget.org with that version. The package README is
[assets/nuget-readme.md](assets/nuget-readme.md).

## License

[MIT](LICENSE) © 2026 Joakim Dangården
