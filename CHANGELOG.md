# Changelog

All notable changes are listed here. The project is pre-1.0: APIs change freely, and breaking
changes are called out under **Breaking**.

## 0.1.0-preview (unreleased)

Restructured to the new design ([Design.md](Design.md)), milestone 1.

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
