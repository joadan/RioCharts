# Changelog

All notable changes are listed here. The project is pre-1.0: APIs change freely, and breaking
changes are called out under **Breaking**.

## 0.1.0-preview (unreleased)

Restructured to the new design ([Design.md](Design.md)), milestones 1 to 6.

### Docs site

- Blazor WebAssembly docs site (`samples/RioCharts.Docs`) with a live example and its code for every
  feature, a JSON spec playground that runs in the browser, and pages on server export and AI
  agents. Published to GitHub Pages by `.github/workflows/docs.yml`.

### Milestone 6

- JSON chart specs (`RioCharts.Core.Specs`): series name dataset fields (`dataset`, `x`, `y`) with
  `filter`, `timeBucket`, `aggregate`, `groupBy`, `sort` and `limit`; inline `data` for small sets;
  `"type": "auto"` picks line, bar or donut. `ChartSpecJson.Parse` is tolerant (letter case,
  unknown fields, synonyms such as column or doughnut, a single series object) and returns errors
  that name the path and the valid options; `ChartSpecJson.Schema()` gives the JSON schema with
  descriptions. `ChartSpecBinder` binds a spec to `ChartDataset`s or an `IChartDataSource`.
- Blazor: `<Chart Spec="spec" Datasets="datasets" />`; binding errors show on the chart.
- New package `RioCharts.AI`: `ChartTool` (create_chart, list_datasets, get_chart_schema) as
  Microsoft.Extensions.AI functions and MCP server tools (`WithRioChartsTools`), rendering with
  RioCharts.Skia; `ChartRazor.FromSpec` turns a spec into component markup.
- Demo: spec playground at `/spec`, the schema at `/ai/schema.json`, an MCP server at `/mcp`.
- **Breaking:** the chart's `Spec` property is now the `Spec` parameter; use `GetSpec()` to read
  the model. `SeriesSpec.Id` is optional (generated for JSON specs).

### Milestone 5

- New package `RioCharts.Skia`: `ChartExport.ToPng` and `ChartExport.ToSvg` render a `ChartSpec`
  and data on the server with SkiaSharp, using the same layout as the Blazor components and real
  font metrics (`SkiaTextMeasurer`). `ExportOptions` sets size, pixel density, theme
  (`ExportTheme.Light`/`Dark`, with per-token overrides), background, fonts and culture.
  `ChartExport.Layout` returns the layout for further use; `ToSvg`/`ToPng` also take a scene.

### Milestone 4

- Zoom and pan on the x axis: `Zoomable="true"`. Drag to zoom in, Shift+drag to pan,
  Ctrl/⌘+wheel to zoom at the pointer, double-click or "Reset zoom" to zoom out. The y axes fit
  the visible data, and downsampling runs on the visible points only.
- `@bind-VisibleRange` (`ChartRange`), `OnZoomChanging` (clamp or cancel), and
  `ZoomToAsync`, `PanAsync`, `ResetZoomAsync`, `GetVisibleRange` on the chart reference.
- Linked charts: `Group="name"` shares the crosshair (in the browser) and the zoom.
- Live data: `SeriesBuffer` (append from any thread, capacity and time window) on a series'
  `Buffer` parameter; the chart redraws at most every `RefreshInterval` ms (default 100) and keeps
  the tooltip open while it updates. `ConsumeAsync` feeds a buffer from an `IAsyncEnumerable`.
- Annotations: `<PointAnnotation>` (a marker with a label) and `<Callout>` (a text box with a
  connector line); `AnnotationType.Point` and `Callout` with `X`, `Y`, `OffsetX`, `OffsetY`.
- Draggable reference lines: `Draggable="true"`, with `@bind-Value` on `<ReferenceLine>` and
  `OnAnnotationChanged` on the chart.
- Display time zone: `TimeZone="Europe/Stockholm"` (or `"UTC"`, `"local"` for the browser's
  zone). Times are plotted on instants, so lines never double back at DST changes; ticks
  (midnights, Mondays, months) and labels use the zone, and tooltips show the offset when it
  differs from the viewer's. `UnspecifiedTimes` says how DateTime values without a zone are read.
  The default is unchanged: times are shown as given.
- Canvas series layer: `SeriesLayer="SeriesLayer.Canvas"` draws the series on a canvas (axes,
  labels and annotations stay SVG), with a higher default downsampling threshold;
  `DownsampleThreshold` on the chart sets it explicitly.
- **Breaking:** `<ReferenceLine>` is generic (`ReferenceLine<TValue>`, inferred from `Value`) so
  it can bind its value. `Axis` and `LabelPosition` moved from `AnnotationBase` to the line and
  band components.

### Milestone 3

- `<PieSeries>`, `<DonutSeries>` and `<RadialBarSeries>`. The x value names each slice and the
  legend lists slices; clicking one hides it.
- Pie labels: percentages (or values with `Format`) inside slices that fit, otherwise outside
  with leader lines, stacked per side so they never overlap. Donuts show the total in the middle.
- `StartAngle`/`EndAngle` (half donuts fit their space), `InnerRadius`, `GroupSmallSlices`
  ("Other"), and nested rings from several pie series.
- Radial bars: one track per category, `Max` for the full track, name and value at the start.
- Slice tooltips with a hover halo, and `OnPointClick` for slices and radial bars.
- **Breaking:** `SeriesSpec.DataLabels` and the series `DataLabels` parameter are now `bool?`
  (null: on for pie, donut and radial bar, off otherwise).

### Project

- Project icon (`assets/icon.svg`, `assets/icon.png`), used as the NuGet package icon and the demo favicon.

### Milestone 2

- Tooltip and crosshair, on by default: one tooltip per x across series, or per point with
  `Tooltip="new TooltipSpec { Shared = false }"`. Handled in the browser, no server round trips.
- Stacked series: `Stack="name"` on bars, areas and lines.
- Multiple Y axes: `YAxes` on the chart and `YAxisId` on series; extra axes align to the grid.
- Data labels: `DataLabels="true"` on a series, with collision avoidance.
- Annotations: `<ReferenceLine>` and `<ReferenceBand>` on either axis, or `Annotations` as data.
- Events: `OnPointClick` (with the item) and `OnAnnotationClick`.
- `Format` on series for tooltip and data label values.
- **Breaking:** `ChartLayoutResult` gains `Tooltip` and `YScales`; `YScale` is now the scale of
  the axis that owns the grid. The series layer is split into `rio-plot` and `rio-labels` groups.

### Breaking

- The single `RioCharts` package and its `<RioChart Series="...">` component are replaced by
  `RioCharts.Core` and `RioCharts.Blazor` with `<Chart>` and `<LineSeries>`, `<AreaSeries>`,
  `<BarSeries>` child components.
- Removed for now (they return in later milestones): pie, donut and funnel charts, bubble
  charts and heatmaps, multiple Y axes, tooltips, crosshair, pan and zoom.

### Added

- `RioCharts.Core`: columnar `SeriesData` (items with selectors or arrays), linear, time and band
  scales, nice and calendar-aware time ticks, compact culture-aware formatting, linear/monotone/step
  path builders with gaps, LTTB downsampling with gaps, a render-agnostic scene (static, series and
  overlay layers), a hit-test index and a serializable `ChartSpec`.
- `RioCharts.Blazor`: responsive `<Chart>` (ResizeObserver, measured font widths), SVG scene
  renderer, legend with `@bind-HiddenSeries` and `OnLegendClick`, CSS-variable theming with
  light and dark defaults.
- MIT license.
