# Changelog

All notable changes are listed here. The project is pre-1.0: APIs change freely, and breaking
changes are called out under **Breaking**.

## 0.1.0-preview (unreleased)

Restructured to the new design ([Design.md](Design.md)), milestones 1 to 6.

### Blazor

- **Breaking:** the `<Chart>` component is renamed to `<RioChart>` (`RioChart<TItem>`), so it does
  not clash with an app's own `Chart` types or other libraries. Series and annotation components
  keep their names. `ChartRazor` now generates `<RioChart>`.
- Trimming and native AOT: Core, Blazor, Skia and AI are marked `IsAotCompatible` and build
  without trimming or AOT warnings. Spec JSON uses source-generated metadata
  (`ChartSpecJson.Options`); `ChartSpecJson.Parse` fills in the defaults of properties missing from
  the JSON, which generated metadata cannot keep for `init` properties. Deserializing a
  `ChartSpec` yourself with `JsonSerializer` and `ChartSpecJson.Options` does not do that: use
  `Parse`. The AI tool's result JSON is source-generated too. `tests/RioCharts.AotSmoke` checks
  specs, binding, layout, texts, SVG/PNG export and the AI tool in a native AOT build (the "Native
  AOT" workflow runs it on Linux).
- Static server rendering and prerendering draw plain SVG (`RendererInfo.IsInteractive`): no
  tab stop, download button or hover layer, and canvas series are drawn as SVG until the page is
  interactive, so a prerendered canvas chart is no longer blank.
- Languages: the chart's own texts ("No data available", "Reset zoom", "Total", "Other", the
  download button, the chart description, axis and legend names and keyboard help for screen
  readers) are in `ChartTexts`, built in for English, Swedish, Norwegian, Danish, Finnish, German,
  French and Spanish and picked from the chart's `Culture` (else the current UI culture, then
  parent cultures, then English). Change texts with `Texts="ChartTexts.Swedish with { ... }"` or a
  cascaded `ChartTexts`; add a language with `ChartTexts.Register("pl", ...)`; load from resx or
  `IStringLocalizer` with `ChartTexts.From(key => ...)`. Templates take `{0}` placeholders and
  "one|other" forms for counts, and a broken template falls back to English. Also
  `LayoutOptions.Texts` and `ExportOptions.Texts` (Skia). The full description now says "trend
  rising/falling" instead of "trend increasing/decreasing".
- Compact charts (sparklines): `Compact="true"` on `RioChart` (`ChartSpec.Compact`, `"compact"` in
  JSON specs) draws no axes, grid, legend or title; the series fill the box with a small inset, the
  y range fits the data (bars keep zero), heights below 50 px are allowed, annotations keep their
  shape without text, and empty data draws nothing. Screen readers get a short summary ("Room:
  18.2 °C to 20.8 °C, last 19.4 °C, rising"), and keyboard navigation is off unless set.
- `Highlight` on line and area series (`SeriesSpec.Highlight`, `"highlight"` in JSON as a string
  or list): dots on the last, first, lowest and/or highest value, on any chart.
- `Interactive="false"` on `RioChart`: plain SVG without the JS module (no tooltip, hover, zoom,
  keyboard, download button or measuring; canvas series fall back to SVG). For many charts on a
  page; set `Width` for exact geometry (default layout width 640, compact 160).
- **Breaking:** `KeyboardNavigation` is now `bool?` (null: on, off for compact charts).
- Keyboard navigation (`KeyboardNavigation`, on by default): a chart with data is a tab stop
  (`role="application"`, a focus ring, instructions for screen readers). With it focused, arrow
  keys move between points and Up/Down between series (pies: between slices), Home/End and
  PageUp/PageDown jump; the crosshair and tooltip follow (and linked charts), each point is
  announced through a live region, Enter raises `OnPointClick` and Escape leaves. Handled in the
  browser from the points it already has. On `Zoomable` charts, + and - zoom 2x around the
  focused point (or the middle), Shift+Left/Right pans a fifth of the view and 0 shows all data,
  through the same zoom commands as the pointer (`OnZoomChanging`, `@bind-VisibleRange`, linked
  charts); the focus stays on the same x. The download menu also moves with Up/Down/Home/End.
- **Breaking (markup):** the chart's `<svg>` now has `role="application"` and `tabindex="0"`
  instead of `role="img"` when keyboard navigation is on; `KeyboardNavigation="false"` restores
  the old markup.
- Client-side export. `Exportable` adds a download button (top-right, shown on hover or focus) with
  a menu for PNG, SVG and CSV; `ExportFileName` names the files (default: the
  title). From code: `DownloadPngAsync`, `DownloadSvgAsync`, `DownloadCsvAsync`, and `ExportPngAsync`
  (`byte[]`), `ExportSvgAsync` (string) and `ExportCsv()` to get the content in .NET.
  `ChartImageOptions` sets the PNG `Scale` (default 2) and the `Background` (default the surface
  color). Images are made in the browser from the chart as drawn, so they keep the size, zoom,
  hidden series, canvas series layer and the page's CSS theme; images reach .NET as a stream, so
  Blazor Server's message size limit does not apply. Core: `ChartCsv` and `CsvOptions` (an x column
  and one column per series, invariant numbers and ISO 8601 times by default).
- Download menu: `ExportFormats` picks the formats it offers (`Png | Svg | Csv`, default all; with
  one format the button saves it directly, with none there is no button). `OnExporting` runs when
  the user picks a format, before the file is saved: change `FileName`, `ImageOptions` or
  `CsvOptions`, or set `Cancel` and handle the content yourself with `GetPngAsync`, `GetSvgAsync`
  or `GetCsv`, which make it only when called. Not raised for the export methods called from code.
- Fix: the Reset zoom button now resets every zoomable chart in the same `Group`, as
  double-click already did; before it reset only the chart it was on.
- Series components combine items with the same x into one point: `Aggregate` (`Sum`,
  `Average`, `Min`, `Max`, `Count` or `Last` of `YValue`) or `YAggregate`, a function of the
  group's items (e.g. `g => g.Sum(o => o.Amount)`), in the spirit of Blazor-ApexCharts. Grouping
  runs once per data change. `PointClickEventArgs.Items` holds every item behind a clicked point
  (the group, or just `Item`). Core: `SeriesData.FromGroups` and `Aggregation`.
- `ChartRazor` writes a spec's per-x `aggregate` as the series' `Aggregate` instead of a comment
  (with a `timeBucket` it stays a comment).
- Layout warns when a category, bar or pie series has the same x more than once and nothing
  combines them (the points are drawn on top of each other).
- Custom tooltips. `TooltipValue` on a series and `TooltipHeader` on the chart set the text of
  the value and the header from the hovered point (`TooltipPoint<TItem>`: x, y, item(s), default
  text); they run once per layout, so hover stays in the browser. `TooltipTemplate` renders any
  Razor in the tooltip card from a `TooltipContext<TItem>` (header, x and the hovered points);
  its content comes from .NET when the hovered x changes, one round trip behind on Blazor Server.
- Fixed: in a shared tooltip, a step line (`Curve="CurveType.Step"`) showed the value of its
  nearest point, not the one in effect, and was missing when its nearest point was far away
  (common with irregular data, e.g. a setpoint next to a sensor logged on change). It now shows
  the last point at or before the hovered x, marked at that point; the header comes
  from a series with a point at that x. Core: `TooltipSeries.Step`.
- Fixed: on time axes without a display time zone, `PointClickEventArgs.X` was a
  `DateTimeOffset` with the server's offset instead of a `DateTime`.
- Fixed: a point click on a live-data series could report the wrong point when the buffer had
  changed since the last layout.

### Tests

- Component tests with bUnit (`tests/RioCharts.Blazor.Tests`): the download menu and
  `OnExporting`, legend clicks and `@bind-HiddenSeries`, zoom binding and `OnZoomChanging`, point
  clicks on aggregated series, rendering without JS, empty and single-point data, spec errors and
  disposal. The JS module is faked in strict mode, so an unexpected JS call fails a test. The
  release workflow runs both test projects.

### Packaging

- NuGet packages for RioCharts.Core, .Blazor, .Skia, .AI and the `RioCharts.Mcp` tool, with a
  package README, symbols (`.snupkg`), Source Link and XML docs. The manual **Create Release**
  workflow (`.github/workflows/create-release.yml`) tests, packs and publishes them to nuget.org
  (Trusted Publishing), then tags the commit and creates a GitHub release. Versions come from
  `version.json` and the git height (Nerdbank.GitVersioning).

### AI

- `render_chart` tool (`ChartTool.RenderChartAsync`, `AsRenderFunction`, and in
  `RioChartsMcpTools`): the same spec as `create_chart`, returned to the agent as a PNG or SVG
  image at the size and theme it asks for, e.g. for reports, emails or documents. Set
  `ChartToolOptions.OutputDirectory` to also save the images as files and return their paths.
- **Breaking:** `AsAIFunctions()` and `WithRioChartsTools` now include `render_chart`.
- New package `RioCharts.Mcp`: `riocharts-mcp`, a local MCP server over stdio, installed as a .NET
  tool, for agents such as Claude Code, Claude Desktop or GitHub Copilot. It offers render_chart,
  list_datasets and get_chart_schema; CSV and JSON files in `--data` are the datasets, images are
  saved to `--out`. Options for default size, theme and culture.
- `ChartDataset.FromCsv` (delimiter detection, quoted cells, decimal comma with `;` or tab) and
  `FolderDataSource` (one dataset per CSV or JSON file, read again when a file changes).
- `samples/RioCharts.Mcp.Sample`: sample data, `.mcp.json` (Claude Code), `.vscode/mcp.json`
  (VS Code) and example prompts for trying `riocharts-mcp`.

### Docs site

- Blazor WebAssembly docs site (`samples/RioCharts.Docs`) with a live example and its code for every
  feature, a JSON spec playground that runs in the browser, and pages on server export and AI
  agents. Published to GitHub Pages by `.github/workflows/docs.yml`.
- Redesigned docs site: a landing page (hero with a live chart, a gallery of live chart tiles,
  features, packages), a frosted header, a sidebar with an active marker, previous/next links,
  highlighted code blocks with a copy button, a remembered light/dark theme that follows the OS by
  default, and a branded loading screen. The JSON spec playground editor highlights its JSON as you
  type.

### Fixed

- Pie and donut labels inside slices no longer spill over the ring. The fit test now checks the
  horizontal label box against the slice, so on the sides of a thin donut a label that would run
  across the ring is placed outside with a leader line instead.
- Leader lines of outside pie labels no longer cross or run over each other when tiny slices
  crowd the top or bottom. Each runs from its slice to a bend at its label's height and then
  straight across to the text; labels keep slice order, and a bend moves outward only where the
  leader would otherwise pass over a neighbour's.

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
