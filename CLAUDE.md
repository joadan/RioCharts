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
- `src/RioCharts.Blazor/` — `Chart.razor` (+ `.cs`, `.css`, `.js`), `Series.cs`, `Annotations.cs`, `Rendering/` (SVG writer and scene renderer).
- `src/RioCharts.Skia/` — server-side export: `ChartExport`, `SvgExporter`, `SkiaRenderer`, `SkiaFonts`/`SkiaTextMeasurer`, `ExportTheme`.
- `tests/RioCharts.Tests/` — xUnit tests.
- `samples/RioCharts.Demo/` — Blazor Server demo (http://localhost:5030).
- `docs/` — implementation notes.

## Build and test

```
dotnet build RioCharts.sln
dotnet test
dotnet run --project samples/RioCharts.Demo --launch-profile http
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
