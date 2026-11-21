# RioCharts

A pure .NET chart library, Blazor first. `Design.md` is the design (the owner's document): read it
before adding or changing features. Record where the code differs from it in
`docs/implementation-notes.md`, not in `Design.md`.

## Layout

- `src/RioCharts.Core/` — no UI dependencies (namespace `RioCharts.Core.*`).
  - `Data/` — `SeriesData` (columnar x/y, NaN = gap), `TimeValue`, `Lttb`.
  - `Scales/` — linear, band, nice ticks, calendar time ticks.
  - `Formatting/`, `Text/` — label formatting, text measurement.
  - `Paths/` — line/area path builders (linear, monotone, step).
  - `Scene/` — render-agnostic nodes, `PathData`, styles, theme tokens.
  - `Series/` — series type contract and line/area/bar types (internal).
  - `Layout/` — `ChartLayout.Compute` (spec + data → scene), hit-test index, describer.
  - `ChartSpec.cs` — the serializable chart model.
- `src/RioCharts.Blazor/` — `RioChart.razor` (+ `.cs`, `.css`, `.js`), `Series.cs`, `Annotations.cs`, `Rendering/` (SVG writer and scene renderer).
- `src/RioCharts.Skia/` — server-side export: `ChartExport`, `SvgExporter`, `SkiaRenderer`, `SkiaFonts`/`SkiaTextMeasurer`, `ExportTheme`.
- `src/RioCharts.AI/` — AI chart creation: `ChartTool` (create_chart as AIFunction), `RioChartsMcpTools` (MCP), `ChartRazor` (spec → Razor).
- `src/RioCharts.Mcp/` — `riocharts-mcp`, a local stdio MCP server packed as a .NET tool: `ServerOptions` (arguments), `LocalServer` (tools: render_chart, list_datasets, get_chart_schema).
- `src/RioCharts.Core/Specs/` — JSON specs: `ChartSpecJson` (tolerant parse, schema), `ChartSpecBinder` (fields, transforms, auto type), `ChartDataset` (also `FromCsv`), `IChartDataSource`, `FolderDataSource` (CSV/JSON files).
- `tests/RioCharts.Tests/` — xUnit tests (Core, AI, MCP, Skia).
- `tests/RioCharts.Blazor.Tests/` — bUnit component tests for `<RioChart>`; `ChartTestBase` fakes the JS module in strict mode, so add a setup when the component calls a new JS function.
- `samples/RioCharts.Demo/` — Blazor Server demo (http://localhost:5030).
- `samples/RioCharts.Mcp.Sample/` — sample data (CSV/JSON) and agent config for trying `riocharts-mcp`; not a .NET project. Its `.mcp.json` applies only when an agent is opened in that folder.
- `samples/RioCharts.Docs/` — docs site, Blazor WebAssembly (http://localhost:5031); `.github/workflows/docs.yml` publishes it to GitHub Pages. Add new features there with a live example.
- `docs/` — implementation notes.

## Build and test

```
dotnet build RioCharts.sln
dotnet test
dotnet run --project samples/RioCharts.Demo --launch-profile http
dotnet run --project samples/RioCharts.Docs --launch-profile http
```

Stop the running demo before rebuilding; it locks the library DLLs.

## Git

- Commit and push directly to `master`; no feature branches or PRs for now.
- Keep `CHANGELOG.md` up to date and call out breaking changes.

## Conventions

- Core never emits SVG or references Blazor. It produces scene nodes; renderers convert them.
- Series render nothing themselves; they register a `SeriesSpec` and `SeriesData` with the chart.
- Selectors run once per data change, never per render. Never diff list contents on render.
- Format every number written into SVG with `SvgWriter.N` (invariant culture). User-visible labels use the chart's culture.
- Colors in the scene are theme tokens (`ThemeTokens`) or CSS colors; never hard-code theme colors in Core.
- Visual defaults follow Design.md "Visual design": no y-axis line, horizontal grid only, no tick marks, markers hidden.
- Edge cases (empty, single point, equal values, gaps) must render a clean state, never throw.
- Keep the JS module tiny: measurement and high-frequency interaction only.
- Temperatures in examples are in °C.
