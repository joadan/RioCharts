# RioCharts

A pure .NET chart library, Blazor first. `Design.md` is the design (the owner's document): read it
before adding or changing features. Record where the code differs from it in
`docs/implementation-notes.md`, not in `Design.md`.

## Layout

- `src/RioCharts.Core/` — no UI dependencies (namespace `RioCharts.Core.*`).
  - `Data/` — `SeriesData` (columnar x/y, NaN = gap), `TimeValue`, `Lttb`.
  - `Scales/` — linear, band, nice ticks, calendar time ticks.
  - `Formatting/`, `Text/` — label formatting, text measurement, `ChartTexts` (the chart's own texts per language).
  - `Paths/` — line/area path builders (linear, monotone, step).
  - `Scene/` — render-agnostic nodes, `PathData`, styles, theme tokens.
  - `Series/` — series type contract and line/area/bar types (internal).
  - `Layout/` — `ChartLayout.Compute` (definition + data → scene), hit-test index, describer.
  - `ChartDefinition.cs` — the serializable chart model.
- `src/RioCharts.Blazor/` — `RioChart.razor` (+ `.cs`, `.css`, `.js`), `Series.cs`, `Annotations.cs`, `Rendering/` (SVG writer and scene renderer).
- `src/RioCharts.Skia/` — server-side export: `ChartExport`, `SvgExporter`, `SkiaRenderer`, `SkiaFonts`/`SkiaTextMeasurer`, `ExportTheme`.
- `src/RioCharts.AI/` — AI chart creation: `ChartTool` (create_chart as AIFunction), `RioChartsMcpTools` (MCP), `ChartRazor` (definition → Razor).
- `src/RioCharts.Mcp/` — `riocharts-mcp`, a local stdio MCP server packed as a .NET tool: `ServerOptions` (arguments), `LocalServer` (tools: render_chart, list_datasets, get_chart_schema, get_razor, get_usage).
- `src/RioCharts.Core/Definitions/` — JSON definitions: `ChartDefinitionJson` (tolerant parse, schema), `ChartDefinitionBinder` (fields, transforms, auto type), `ChartDataset` (also `FromCsv`), `IChartDataSource`, `FolderDataSource` (CSV/JSON files).
- `tests/RioCharts.Tests/` — xUnit tests (Core, AI, MCP, Skia).
- `tests/RioCharts.Tests/Snapshots/` — visual snapshots (SVG) of reference charts from `SnapshotTests.cs`. A layout change that alters them fails with `name.received.svg` next to the snapshot: look at both, then accept with `RIOCHARTS_ACCEPT_SNAPSHOTS=1 dotnet test` and review the diff. New chart types get a reference chart there.
- `tests/RioCharts.AotSmoke/` — console app published with native AOT (`.github/workflows/aot.yml`); exits 0 when its checks pass. Locally without a C++ linker: `dotnet publish tests/RioCharts.AotSmoke -c Release -f net10.0 -r win-x64 --self-contained -p:PublishAot=false -p:PublishTrimmed=true` (and `-f net11.0`).
- `docs/llms/guide.md` — the guide for coding agents. `AgentGuideTests` (Blazor tests) builds `samples/RioCharts.Docs/wwwroot/llms-full.txt` from it, the example components in `tests/RioCharts.Blazor.Tests/Guide/` (compiled and rendered there) and the XML docs of every component parameter; RioCharts.AI embeds that file (`RioChartsUsage`, get_usage). A changed parameter or doc comment fails the test: accept with `RIOCHARTS_ACCEPT_SNAPSHOTS=1 dotnet test`. `llms.txt` next to it is written by hand.
- `tests/RioCharts.Blazor.Tests/` — bUnit component tests for `<RioChart>`; `ChartTestBase` fakes the JS module in strict mode, so add a setup when the component calls a new JS function.
- `benchmarks/RioCharts.Benchmarks/` — BenchmarkDotNet: layout (line, zoomed, bars, pie), items to columns, LTTB, SVG/PNG export, JSON definition parse/CSV/bind. Run in Release: `dotnet run -c Release --project benchmarks/RioCharts.Benchmarks -- --filter *Layout*` (`--job short` for a quick look). Compare before and after a change to a hot path; the baseline is in `benchmarks/README.md`.
- `samples/RioCharts.Demo/` — Blazor Server demo (http://localhost:5030).
- `samples/RioCharts.Mcp.Sample/` — sample data (CSV/JSON) and agent config for trying `riocharts-mcp`; not a .NET project. Its `.mcp.json` applies only when an agent is opened in that folder.
- `samples/RioCharts.Docs/` — docs site, Blazor WebAssembly (http://localhost:5031); `.github/workflows/docs.yml` publishes it to GitHub Pages. Add new features there with a live example.
- `docs/` — implementation notes; `chart-types.md` lists supported and candidate chart types (status, effort, why or why not); `v1.md` is the 1.0 plan (decisions and what is left).
- `tools/SocialCard.cs` — makes the docs site's link preview image (`social-card.png`): `dotnet run tools/SocialCard.cs` from the root.
- `tools/prerender/` — the docs workflow's prerender step (Node + Playwright): opens every docs page in headless Chromium and saves it as static HTML (title, description from the page's `lead` paragraph, canonical URL), plus `sitemap.xml` and `robots.txt`. Locally: `npm install` and `npx playwright install chromium` in that folder, then `node tools/prerender/prerender.mjs <published wwwroot> samples/RioCharts.Docs/Pages`.
- `tools/ReadmeImage.cs` — makes the README picture (`assets/readme-charts-light.png` and `-dark.png`): `dotnet run tools/ReadmeImage.cs` from the root.
- `README.md` is the short front page (pitch, picture, one example per output, packages); details belong on the docs site. Contributor material (build, tests, project layout, releasing) is in `CONTRIBUTING.md`.

## Build and test

```
dotnet build RioCharts.sln
dotnet test
dotnet run --project samples/RioCharts.Demo --launch-profile http
dotnet run --project samples/RioCharts.Docs --launch-profile http
```

Stop the running demo before rebuilding; it locks the library DLLs.

The libraries and test projects target .NET 10 (LTS) and .NET 11 (`LibraryTargetFrameworks` in
`Directory.Build.props`), so building needs both SDKs and `dotnet test` runs every test on both.
The docs site runs on .NET 11, so the .NET 11 build of the library runs in a real browser; the
demo (Blazor Server), riocharts-mcp and the benchmarks stay on .NET 10 (the tool rolls forward
to .NET 11). Behavior must be the same on both: a test that differs is a bug to fix,
not a reason to branch on the framework.

## Git

- Every change goes through a pull request: work on a branch, push it and open a PR against
  `master`. Never commit or push directly to `master`.
- Keep `CHANGELOG.md` up to date and call out breaking changes.

## Conventions

- Core never emits SVG or references Blazor. It produces scene nodes; renderers convert them.
- Series render nothing themselves; they register a `SeriesOptions` and `SeriesData` with the chart.
- Selectors run once per data change, never per render. Never diff list contents on render.
- Format every number written into SVG with `SvgWriter.N` (invariant culture). User-visible labels use the chart's culture.
- Colors in the scene are theme tokens (`ThemeTokens`) or CSS colors; never hard-code theme colors in Core.
- Visual defaults follow Design.md "Visual design": no y-axis line, horizontal grid only, no tick marks, markers hidden.
- Edge cases (empty, single point, equal values, gaps) must render a clean state, never throw.
- Keep the JS module tiny: measurement and high-frequency interaction only.
- Temperatures in examples are in °C.
- Libraries are `IsAotCompatible`: keep the build free of IL2xxx/IL3xxx warnings. JSON goes through source-generated contexts (`ChartDefinitionJsonContext`, `ChartToolJsonContext`), never reflection. A new definition record type needs an entry in `ChartDefinitionJson.Factories`.
- Public API is locked: a new, changed or removed public symbol fails the build (RS0016/RS0017) until `PublicAPI.Unshipped.txt` of that package lists it (CONTRIBUTING.md, Public API). Keep the public surface small; prefer `internal` plus `InternalsVisibleTo`.
- JS interop arguments are primitives, `byte[]` or element/.NET references only: payloads are records in `Rendering/Interop.cs`, sent as source-generated UTF-8 JSON (`InteropJson.Bytes`) and read with `json()` in the JS module. Never pass anonymous or other objects (they break trimmed WebAssembly apps; a bUnit test checks).
- Blazor features that need JS check `Live` (interactive and rendered interactively), not `Interactive` alone.
- Text a chart writes itself (empty states, buttons, screen reader text) comes from `ChartTexts`, never a string literal; add a key with English and all built-in languages. Warnings and errors for developers stay English.
