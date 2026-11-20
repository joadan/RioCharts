# Changelog

All notable changes are listed here. The project is pre-1.0: APIs change freely, and breaking
changes are called out under **Breaking**.

## 0.1.0-preview (unreleased)

Restructured to the new design ([Design.md](Design.md)), milestones 1 to 3, milestone 4 in progress.

### Milestone 4

- Zoom and pan on the x axis: `Zoomable="true"`. Drag to zoom in, Shift+drag to pan,
  Ctrl/⌘+wheel to zoom at the pointer, double-click or "Reset zoom" to zoom out. The y axes fit
  the visible data, and downsampling runs on the visible points only.
- `@bind-VisibleRange` (`ChartRange`), `OnZoomChanging` (clamp or cancel), and
  `ZoomToAsync`, `PanAsync`, `ResetZoomAsync`, `GetVisibleRange` on the chart reference.
- Linked charts: `Group="name"` shares the crosshair (in the browser) and the zoom.

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
