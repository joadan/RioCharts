# Chart types

A list of chart types RioCharts supports or could support. It covers each type's status, what it
would cost and why it has or hasn't been added. The design of the types we do support is in
`Design.md` ("Chart model"); this file is for deciding what comes next.

## Rating: popularity, effort and complexity

Each candidate is rated on what it's worth (popularity) and what it costs (effort and
complexity). Cost is split in two because a type can be quick to build and still make the
library harder to use and maintain.

**Popularity** is how often people expect a chart library to have it. It is a judgement, based
on what Excel and the common chart libraries offer and what dashboards and reports tend to use:
- **High**: expected; its absence is noticed.
- **Medium**: asked for in some domains (finance, science, project tools).
- **Low**: niche; most users never look for it.

**Effort** is the work to build it once:
- **Small**: an option or a variant of an existing series type.
- **Medium**: a new series type that reuses existing scales, layout and hit-testing.
- **Large**: a new layout, coordinate system or data model.

**Complexity** is what it adds for good: to the public API (components, parameters, definition
fields), to the concepts a user has to learn, and to code that other features must keep working
with (stacking, zoom, tooltips, keyboard navigation, the describer, export, the AI definition):
- **Low**: a few parameters; contained in its own series type.
- **Medium**: a new concept or data shape, or options that several series types must support.
- **High**: a new data model or layout that other features must special-case.

Rules of thumb:
- Prefer types whose complexity stays inside their own series type over options that spread across many types.
- Prefer a data transform (e.g. binning) or a variant of an existing type over a new concept.
- A type that is rarely the best chart for the data needs low complexity to be worth adding.

## Supported

| Type | Series / definition type | Notes |
|---|---|---|
| Line | `LineSeries` / `line` | Linear, monotone and step curves. Also `spline`. |
| Area | `AreaSeries` / `area` | Stacking groups, 100 % stacked. |
| Bar | `BarSeries` / `bar` | Grouped, stacked, 100 % stacked, horizontal. Also `column`, `stackedBar`, `stackedBar100`. |
| Scatter | `ScatterSeries` / `scatter` | Also `points`. |
| Bubble | `ScatterSeries` with `SizeValue` / `bubble` (scatter with `size`) | Area ∝ size, one scale per chart, largest drawn first, a size legend. |
| Heatmap | `HeatmapSeries` / `heatmap` | One row per series. |
| Range area | `RangeAreaSeries` / `rangeArea` | Low/high band. |
| Box plot | `BoxPlotSeries` / `boxPlot` | Raw values or a precomputed summary. |
| Candlestick | `CandlestickSeries` / `candlestick` | OHLC. |
| Pie / donut | `PieSeries`, `DonutSeries` / `pie` | A donut is a pie with an inner radius. |
| Radial bar | `RadialBarSeries` / `radialBar` | The definition parser also accepts `gauge` as an alias for this. |
| Radar | `RadarSeries` / `radar` | Also `spider`. |
| Treemap | `TreemapSeries` / `treemap` | Squarified layout, hierarchy. |
| Histogram | `HistogramSeries` / `histogram` | Equal-width bins with nice edges (auto, `BinWidth`, `BinCount`); several overlay. |
| Waterfall | `WaterfallSeries` / `waterfall` | Changes from a running total, totals and subtotals, connectors. Also `bridge`. |
| Sparkline | compact mode on any chart | Not a series type. |

## Candidates

| Type | Status | Popularity | Effort | Complexity | What it's for | Notes / why not yet |
|---|---|---|---|---|---|---|
| Polar area | In Design.md | Low | Small | Low | Categories compared around a circle | Angle = category band, radius = sqrt value. Fits `PolarSeriesType`. Rarely better than a bar chart, so not a priority. |
| Gauge | In Design.md | High | Medium | Medium | A single value against a range or target | `gauge` is only an alias for radial bar today. A real gauge needs custom start/end angles, colored ranges, a target marker and a big center value: several new parameters. |
| Range bar / timeline | Candidate | Medium–High | Medium | Medium | Periods per category: schedules, states over time (on/off/fault) | Horizontal bars from start to end on a time axis, many per category row. A new data shape (start/end per item). A full Gantt (dependencies, drag) is out of scope. |
| Bullet | Candidate | Low–Medium | Small–Medium | Medium | Value against target and quality bands in little space | A compact horizontal bar with band backgrounds and a target tick. Goes well with sparklines, but bands and targets are new concepts. |
| Error bars | Candidate | Medium | Small | Medium | Uncertainty on points or bars | Cheap to draw, but as an option it spreads to bar, line and scatter (extent, tooltip, describer, spec). |
| Calendar heatmap | Candidate | Low–Medium | Medium | Medium | Daily values over months or a year | A heatmap with its own week × weekday layout and month labels. Reuses the heatmap color scale; the layout is isolated. |
| Dumbbell / slope | Candidate | Low | Small–Medium | Low | Two values per category (before/after, min/max) | Two markers joined by a line on a band axis; a slope chart is the same with two x positions. |
| Streamgraph | Candidate | Low | Small | Low | Shares over time, organic look | A centered ("wiggle") offset as another `StackMode` next to `Percent` (100 % stacked, added). Rarely asked for. |
| Nested donut / sunburst | In Design.md | Medium | Medium–Large | Medium | Hierarchical shares | One ring per level; can reuse the treemap hierarchy. Labels on thin rings are the hard part. |
| Funnel | Candidate | Medium | Small–Medium | Low | Stages narrowing down | Rarely the best choice (a bar chart is easier to read), but often asked for. |
| Sankey | "Later, if ever" in Design.md | Medium | Large | High | Flows between nodes | Needs a node/link data model, a node ordering layout and curved links; touches tooltips, keyboard navigation and the describer in new ways. |

## Out of scope

| Type | Why |
|---|---|
| 3D charts | Hard to read; against the visual design. |
| Maps / choropleths | Need geo data and projections; a separate library's job. |
| Network graphs | A graph layout engine, not a chart. |
| Word clouds | Not a quantitative chart. |
