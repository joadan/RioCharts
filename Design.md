# RioCharts – design

**RioCharts** is a pure .NET chart library with Blazor as the first target.

## Packages and naming
| Package | Contents |
|---|---|
| `RioCharts.Core` | Scales, ticks, layout, path builders, scene, hit-testing, chart spec. No UI dependencies. |
| `RioCharts.Blazor` | Razor components, SVG renderer, JS interop module. |
| `RioCharts.Canvas` | Canvas renderer (later). |
| `RioCharts.Skia` | Server-side SVG/PNG export (later). |
| `RioCharts.AI` | `create_chart` tool (AIFunction/MCP), schema export, validation (later). |

- Root namespace: `RioCharts` (e.g. `RioCharts.Core.Scales`, `RioCharts.Blazor`).
- Reserve the NuGet package ID prefix `RioCharts` and the domains early.

### Why "RioCharts"
- **Named after Rio**, my son. It gives the project a personal story worth telling in the README.
- **"Río" means river** in Spanish and Portuguese, which fits flowing lines and time series.
- **Short and international**: easy to say and spell in any language, with no awkward meanings.
- **"Charts" in the name** for discoverability, so people searching for chart libraries find it.
- **Not tied to Blazor**: the name still fits as Canvas, Skia and AI packages are added.

## Stance
- Open source (MIT), but built for our own teams. It is not promoted externally.
- Scope comes from internal needs: build the 20% of features that cover 90% of real use, not ApexCharts parity.
- The repo is **public from day one**. Before creating it, confirm ownership and copyright under the company open-source policy.
- Stay pre-1.0 and change APIs freely. Publish packages as prereleases (e.g. `0.x.y-preview`), and treat our internal dashboards as the main test bed.
- The README states the status clearly from the first commit: early preview, APIs will change, built for our own needs, issues welcome, no roadmap promises.
- Keep a CHANGELOG from the start and call out breaking changes there.
- No public plugin API or third-party series registration until there is a real need. Keep the internal seams clean, though.

## Architecture (hybrid approach)
C# does the math and layout. Razor renders SVG. A small JS module handles only interaction and measurement.

```
Core (no Blazor dependency)  ->  Scene (display list)  ->  Renderers (SVG, Canvas, later Skia)
```

### Packages
- **Core**: scales (linear, log, sqrt, time, band), nice-tick and time-tick generation, layout pass, path builders (linear, monotone, step), arcs, stacking, LTTB downsampling, hit-test index. Pure and unit-testable.
- **Blazor**: Razor components that render the scene as SVG, with series registering through a cascading parameter.
- **JS module** (keep it tiny): ResizeObserver, text measurement, pointer tracking, overlay drawing.

### Scene (render-agnostic)
Core never emits SVG. It emits primitives:

```csharp
abstract record Node;
record PathNode(PathData Path, StyleRef Style) : Node;   // commands + float coords, not SVG strings
record RectNode(RectF Bounds, StyleRef Style) : Node;
record TextNode(string Text, PointF At, TextStyle Style, Anchor Anchor) : Node;
record GroupNode(IReadOnlyList<Node> Children, RectF? Clip, Matrix? Transform) : Node;

record Scene(Node Static, Node Series, Node Overlay);
```

Rules:
- `PathData` stores move/line/curve/**arc** commands as float arrays. An arc is defined by center, radii and angles, and each renderer converts it (SVG uses endpoint `A` parameters; canvas uses `arc()`).
- There are three layers from day one. **Static** holds axes and grid; **Series** holds the data; **Overlay** holds the crosshair, tooltip, selection and hover effects. On canvas, only the overlay is redrawn when the pointer moves.
- Styles resolve to concrete values (colors, fonts) through a style table. Theme CSS variables are read once via JS, because canvas cannot use CSS variables.
- Text measurement sits behind `ITextMeasurer`: browser measurement (cached per font), Skia on the server, or an approximation for prerendering and export.

### Renderers
- **SVG (first)**: Razor emits one `<path>` per series, never a component per point. Use `@key` and control `ShouldRender`.
- **Canvas (later)**: send data as binary (`float[]`/`byte[]` over interop), not JSON, and scale by `devicePixelRatio`. Needs an accessibility fallback (hidden table or ARIA summary).
- **Hybrid option**: canvas for the series layer and SVG for axes and text.
- **Skia (later)**: server-side SVG/PNG export from the same Core.

## Chart model
Separate the **chart** (coordinate system and axes) from the **series types** (things that draw inside it). Adding a chart type means adding a series type.

### Coordinate systems
- **Cartesian**: line, area, bar, scatter, heatmap, candlestick, box plot.
- **Polar**: pie, donut, radial bar, radar, polar area, gauge.
- Special cases (treemap, sankey) get their own system later, if ever.
- Cartesian and polar series never mix in one chart.

### Series type contract
Each series type provides:
- the coordinate system it requires;
- its **extent**, used for shared scale domains (stacked series report stacked extents);
- its **layout needs**, such as bar band slots shared across bar series or a ring's radius band;
- **scene nodes** for its layers;
- **hit-test entries**, **tooltip data** and **legend items**, so interaction and legends work uniformly.

### Mixing (cartesian)
- Multiple y-axes, with each series bound to an axis id.
- Named stacking groups, so several stacks can sit side by side.
- Default z-order is areas, then bars, then lines, with an override.

### Polar specifics
| Type | Angle | Radius |
|---|---|---|
| Pie | value (cumulative share) | fixed band |
| Donut | as pie | inner radius > 0 (a parameter, not a separate type) |
| Gauge | custom start/end angle | as pie or radial bar |
| Radial bar | value | category band |
| Radar | category band | value |
| Polar area | category band | value on a **sqrt** scale (area proportional to value) |
| Nested donut / sunburst | value | one radius band per ring |

- Labels are the hard part: inside labels when the slice is big enough, otherwise outside labels with leader lines and per-side collision avoidance.
- An optional threshold groups small slices into "Other".
- Hit-testing uses the angle and distance from the center.
- Hover explode and the donut center label (total or hovered value) live in the overlay layer.
- Animate start and end angles, never path data.
- Without axes, the legend does more of the work; it is a shared component for both coordinate systems.

## Data input model
**Two ways in, one representation inside.** Developers get a convenient API, large data gets a fast path, and Core only ever sees one data format.

### Inside Core: columnar buffers
Every series is normalized to contiguous arrays (structure of arrays):
- **x**: `double[]`. Times become instants (e.g. ms since epoch); categories become band indices plus a category list.
- **y**: `double[]`, with `NaN` for missing values (gaps in lines).
- **Extra columns** for multi-value types: OHLC, range area (low/high), box plot.

Scales, downsampling (LTTB), stacking, hit-testing and binary transfer to canvas all work on these buffers. One code path.

### Way in 1: items with selectors (default)
```razor
<LineSeries TItem="Reading" Items="readings"
            XValue="r => r.Timestamp" YValue="r => r.SupplyTemp" />
```
- Selectors are plain `Func` delegates, never expression trees, so they stay trimming- and AOT-friendly.
- Selectors run **once per data change** to fill the buffers, never per render.
- An index → `TItem` mapping is kept, so events and tooltips return the actual item.

### Way in 2: columnar data (fast path)
```razor
<LineSeries Data="SeriesData.From(timestamps, values)" />
```
- Takes `ReadOnlyMemory<double>` / `ReadOnlyMemory<DateTimeOffset>` directly: no per-item cost, close to zero-copy to canvas.
- Events return an index and values instead of a `TItem`.
- Intended for large time series, e.g. straight from an analytics pipeline.

Selector-based and columnar series behave identically. Developers can start with selectors and switch to the fast path when data grows.

### Change detection
- Reprocess only when the data changes: a new reference, or an explicit `DataVersion` parameter.
- Never diff list contents on render. Mutating a list in place is not the update mechanism.

### Streaming
- A dedicated buffer type (a ring buffer with `Append`, `Trim` and a window) that the chart observes.
- An optional helper consumes an `IAsyncEnumerable<T>` into that buffer with throttling. It is a convenience, not a core input type.

### Out of scope
- `IQueryable`: querying belongs in the app or data layer. The chart takes materialized data; AI-spec transforms run in memory or through the data-source abstraction.

### Performance rules
- Time series are assumed sorted. Check once on load and sort if needed, because binary-search hit-testing depends on it.
- Pool buffers (`ArrayPool`) for large and streaming data to reduce GC pressure.

## Annotations
Annotations are **another kind of scene contributor**, using the same contract as series: extent, scene nodes, hit-test entries and optional legend items. They get layout, rendering, interaction and AI-spec support without special cases.

### Types
- **Reference lines**: horizontal or vertical, e.g. a setpoint, threshold or "today".
- **Bands**: an x-range (a time period such as a fault or maintenance window) or a y-range (an acceptable zone).
- **Point annotations**: a marker plus a label at a data value, e.g. "alarm triggered".
- **Text/callouts**: free labels with an optional connector line to a point.
- **Polar**: kept minimal; a target marker on gauges and radial bars.

### Rules
- **Coordinates**: data coordinates bound to an axis id by default. Relative positions are allowed per dimension, e.g. x at a timestamp and y at "top".
- **Extent**: each annotation sets whether it expands the axis domain. Default yes for reference lines (a threshold is always visible), no for bands.
- **Layering**: bands go behind series; lines and labels go above. Overridable per annotation.
- **Labels**: positions at start, middle or end; clamped to the plot edges and included in the shared label collision avoidance.
- **Interaction**: tooltips and click events from the start. Draggable annotations (e.g. editing a threshold) come later.
- **Styling**: muted and theme-driven by default, so annotations support the data instead of competing with it.

## Interaction model
- After each render, send the computed x-pixel positions (and bounds for bars and slices) to JS once.
- JS handles pointermove locally: a binary search finds the nearest point, and JS draws the crosshair and tooltip in the DOM.
- JS calls back to C# only on meaningful events: click, selection, end of a zoom.
- This keeps Blazor Server as responsive as WASM.

## Blazor API
- Take inspiration from Blazor-ApexCharts' component model (generic `TItem`, XValue/YValue selectors) so it feels familiar. It is not a compatibility target: change names, structure and defaults wherever a cleaner API results.
- Example shape: `<RioChart TItem="..."><LineSeries .../><BarSeries .../></RioChart>`.
- Prerendering runs no JS, so the first render must look acceptable without measured text sizes.

## Visual design: modern and clean
Charts should look calm, current and data-first. When in doubt, remove rather than add.

- **Minimal chrome**: no chart border, no background box, no 3D, no gradients, no drop shadows.
- **Gridlines**: horizontal only, thin and low-contrast. No vertical gridlines by default.
- **Axes**: no y-axis line. The x-axis baseline is subtle. Tick marks are off by default.
- **Typography**: inherit the app's font. Axis labels are smaller and muted. Use tabular numbers (`font-variant-numeric: tabular-nums`) so values align.
- **Color**: a restrained, colorblind-safe default palette, with good contrast in both light and dark mode. Series colors come from theme tokens.
- **Shapes**: line width around 2px, markers hidden until hover, subtle rounded corners on bars, a small gap between donut slices.
- **Whitespace**: generous padding. Labels never touch the plot edges.
- **Tooltip**: a clean card in the theme surface color, with series color dots, aligned values and no heavy borders.
- **Motion**: short, subtle entry and update animations. Respect `prefers-reduced-motion`.
- **Theming**: everything is driven by CSS variables / theme tokens, with light and dark mode out of the box.

## Sensible defaults
Zero configuration should produce a good chart. Every default can be overridden, but defaults are opinionated, not "everything on".

- `<RioChart><LineSeries Items="data" XValue="..." YValue="..." /></RioChart>` with nothing else must look finished.
- **Size**: width is 100% of the container and the default height is fixed (e.g. 300px). The chart is responsive automatically.
- **Scales**: chosen from the data type (DateTime gives a time axis, numbers a linear axis, strings a band axis).
- **Zero baseline**: bars and areas always start at zero. Lines and scatter may use a tight domain.
- **Ticks**: nice values, with the count derived from the available pixels rather than a fixed number.
- **Formatting**: culture-aware. Large numbers are compact (1.2k, 3.4M). Date labels adapt to the visible range (hours, days, months, years).
- **Legend**: shown only when there is more than one series. Clicking an item toggles that series.
- **Tooltip**: on by default. Shared across series (one tooltip per x) for cartesian charts, per slice for polar charts.
- **Data labels**: off for cartesian charts. On for pie and donut when the slice is large enough.
- **Colors**: assigned automatically from the palette in series order, and stable when series are toggled.
- **Missing data**: null values create gaps in lines rather than dropping to zero.
- **Edge cases**: empty data, a single point and all-equal values each render a clean state, never an error or a collapsed axis.
- **Large data**: downsampling switches on automatically above a threshold.

## AI chart creation
AI agents create charts through a **declarative chart spec**: JSON that maps 1:1 to the component model, in the spirit of Vega-Lite. The AI produces a spec; the library validates and renders it.

### One model, two front doors
- `<RioChart Spec="spec" />` and the Razor components (`<LineSeries … />`) compile to the same internal chart model. There is no second rendering path.
- Because of the sensible defaults, a minimal spec must render a finished chart:

```json
{ "series": [{ "type": "line", "x": "timestamp", "y": "supplyTemp" }] }
```

### Schema
- Spec types are plain C# records. The JSON schema is generated from them with `JsonSchemaExporter`.
- Every property has a `[Description]`, which LLMs rely on heavily. Include a few short examples in the tool description.
- Keep required fields to a minimum, and use enums for chart types and options.

### Data
- Specs reference data instead of inlining it: a dataset id or query plus a field mapping. Inline data is allowed only for small sets, with a size limit.
- Data binding goes through a data-source abstraction, so the chart library doesn't depend on any particular data layer.
- Optional declarative transforms: aggregate, group by, time bucket, filter, sort.

### Robustness
- **Strict validation with actionable errors** that name the path and list valid options, e.g. `series[0].y: field 'temp' not found; available: supplyTemp, extractTemp`. This lets the model fix the spec and retry.
- **Tolerant parsing where it's safe**: ignore unknown fields, accept obvious synonyms ("column" → bar, "doughnut" → donut).
- **`"type": "auto"`**: a recommender picks the chart type from the data shape (time + numeric → line, category + numeric → bar, part-of-whole → donut).
- **The spec is pure data**: no code, no expressions, formatting only through format strings, and limits on series count and data size.

### Integration
- Expose a `create_chart` tool as an `AIFunction` (Microsoft.Extensions.AI) and as an MCP server. It works with Microsoft Agent Framework agents.
- **Server-side render** (SVG/PNG via Core + Skia) so an agent can return a chart in Teams, email or chat, not only in Blazor.
- **Spec → Razor export**, so a developer can turn an AI-built chart into component markup in the codebase.
- **Text summary**: generate an accessible description (trend, min/max, notable points) from the spec and data, for screen readers and for the agent to explain the chart.
- **Annotations in the spec**: an agent can add reference lines, bands and points, e.g. "mark the fault periods" or "add the setpoint at 21 °C", or turn detected anomalies into annotations.
- **Spec round-trip**: any chart built with components can be serialized back to a spec, which is useful for saving dashboards and for AI edits ("make it a stacked bar").

## Time handling
**Default: use the time as given.** A `DateTime` is shown exactly as it arrives, with no conversion. This is simple, predictable and correct for most dashboards.

**Optional display zone** for the cases where "as given" breaks:
- the data is UTC but viewers think in local time;
- local-time data crosses DST (a repeated hour in autumn, a missing hour in spring);
- one chart mixes data from different time zones.

When a display zone is set (the browser's zone, UTC, or a specific IANA zone such as the unit's site):
- **Plot on instants, label in the zone.** Time positions are absolute instants, so lines never double back across DST.
- **Calendar logic runs in the display zone**: ticks ("every day at 00:00", "every Monday", "the 1st of each month"), date formatting and time-bucket transforms compute boundaries there, then convert back to instants.
- **Input**: prefer `DateTimeOffset`. `DateTime` with Kind `Utc` or `Local` is converted. `Unspecified` is interpreted through an explicit chart option ("assume UTC" or "assume display zone"), never a silent guess.
- **Show the zone** in tooltips or on the axis when it differs from the viewer's.

Notes:
- On Blazor Server the browser's zone comes from JS. Never use the server's local time for display.
- Use `TimeZoneInfo` with IANA IDs; Core stays dependency-free (NodaTime could be an optional add-on).
- Blazor WASM invariant time zone mode breaks conversion; document it.
- Test case: hourly data across the autumn DST change.

## Live and streaming data
See also the streaming buffer under Data input model.
- **Append and window**: append points to a series, with a sliding window by point count or time span (e.g. the last 24 hours).
- **Incremental updates**: extents update incrementally. Only the series layer re-renders; axes re-render only when the domain or ticks change.
- **Throttling**: coalesce rapid updates (especially on Blazor Server) and render at a capped rate.
- **Downsampling** applies to the visible window.
- **Motion**: no entry animation on every update; transitions stay subtle or off for streaming.

## Linked charts
- A **chart group** (by group id or a wrapping component) links charts on a dashboard.
- **Shared crosshair**: hovering one chart shows the crosshair at the same x in all charts. This is synced in JS, with no server round trip.
- **Synced zoom/pan** on the x-axis, with state held in C#.
- Charts in a group share the same x-scale type (e.g. all time axes).

## Programmatic interaction
**Principle: everything a user can do in the UI can be done from code, and every UI interaction raises an event.** Chart state is observable and controllable.

### Command model
- UI interactions, code and the AI spec all go through the same internal **commands** (zoom, toggle series, select, add annotation, …).
- This keeps behaviour consistent whichever way a change starts, and makes undo/redo possible later.

### Declarative state (Blazor-idiomatic)
Prefer two-way binding where state is natural:
- `@bind-VisibleRange` (zoom/pan window)
- `@bind-HiddenSeries`
- `@bind-SelectedItems` and `@bind-SelectedRange`

### Imperative API (chart reference)
All methods are async, and they work the same on Server and WASM.
- **View**: `ZoomToAsync(range)`, `PanAsync`, `ResetZoomAsync`, `GetVisibleRange()`.
- **Series**: show, hide, toggle and highlight series.
- **Selection**: select points or a range, clear the selection, read the current selection.
- **Hover and tooltip**: `ShowTooltipAsync(x)`, `HideTooltipAsync()`, set the crosshair position. For example, hovering a table row highlights the matching point.
- **Data**: append, replace and remove points (streaming), refresh.
- **Annotations**: add, update and remove at runtime.
- **Export**: `ExportSvgAsync`, `ExportPngAsync`, and `GetSpec()` to serialize the current chart to a spec.
- **Batching**: `ApplyAsync(…)` or an update scope groups several changes into one render. This matters on Blazor Server.

### Events
- `OnPointClick`, `OnSeriesClick`, `OnAnnotationClick`, `OnLegendClick`.
- `OnSelectionChanged`, `OnVisibleRangeChanged`, `OnAnnotationChanged` (e.g. a dragged threshold), `OnRendered`.
- **Cancelable "-ing" events** where useful, e.g. `OnLegendClicking` to override the default toggle, or `OnZoomChanging` to clamp the range.
- **Hover events** are opt-in and throttled, since each one is a server round trip on Blazor Server.
- **Typed payloads**: `TItem`, series, x/y value, and the screen position, not just indices.

### Coordinates and hit-testing
- `ValueToPixel` / `PixelToValue` for custom overlays and positioning external UI.
- `FindNearest(point)` and `GetItemsInRange(range)` from the Core hit-test index. These also work server-side, without a browser.

### Customization hooks
- Templates as `RenderFragment<TContext>` for the tooltip, legend items and data labels.
- A custom overlay layer: a `RenderFragment` that draws in chart coordinates.
- Formatter delegates for axis labels, tooltips and data labels.

### Linked charts
- A chart group exposes the same view commands (`group.ZoomToAsync(range)`), applied to every chart in the group.

## Other topics (to cover later)
- Render modes: Server, WASM, Auto, and static SSR (plain SVG, no interactivity).
- Trimming/AOT friendliness: no reflection in hot paths; System.Text.Json source generation for the spec.
- Target frameworks: current LTS plus the latest .NET.
- Testing: Core unit tests, SVG snapshot tests (e.g. Verify), BenchmarkDotNet.
- Accessibility: keyboard navigation through points and series, ARIA roles, focus handling.
- Docs and a Blazor WASM demo site on GitHub Pages.
- Low priority: a short README note for Blazor-ApexCharts users on how the main concepts map.
- Client-side export: PNG/SVG download, CSV data.
- Compact modes: sparklines, and auto-hiding axes and legend at small sizes.

## Hard parts to budget for
- Nice ticks and time axes that switch cleanly between hours, days and months, plus formatting.
- Axis and pie label collision avoidance.
- Responsive sizing (ResizeObserver; `viewBox` scaling alone distorts text).
- Large data: LTTB downsampling at about 5–10k points and above, canvas for much more.

## Milestones
1. Line, area and bar with linear and time axes (time as given by default), legend, responsive sizing, theming via CSS variables (SVG renderer).
2. Tooltip and crosshair, stacked and mixed series, multiple y-axes, data labels, annotations (reference lines and bands).
3. Pie, donut and radial bar (arc command, label placement).
4. Zoom/pan, display time zone, streaming data, linked charts, remaining annotations (points, callouts, draggable), large-data mode (downsampling, canvas series layer).
5. Server-side export via Skia using the same Core.
6. AI chart spec: JSON schema, validation with actionable errors, `create_chart` tool (AIFunction/MCP), auto chart type.

Design the internal chart model so it is serializable from milestone 1. That keeps the AI spec cheap to add later.

## References
- Radzen and MudBlazor charts: pure-SVG Blazor implementations.
- ScottPlot: C# rendering and axis logic.
- Blazor-ApexCharts: inspiration for the component API (not a compatibility target).
