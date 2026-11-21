# Implementation notes

Where the code differs from, or fills in details of, [Design.md](../Design.md).

## Milestone 1

- **RioChart and TItem.** `<RioChart TItem>` cascades `TItem` to its series, so `XValue`/`YValue`
  lambdas are typed. `TItem` is inferred from `RioChart.Items` (shared by all series) or must be
  given. A chart that only uses columnar `Data` still needs some `TItem`, e.g. `TItem="double"`.
- **Selectors.** `XValue` is `Func<TItem, object?>` (numbers, dates and strings are boxed once per
  data change); `YValue` is `Func<TItem, double?>`, so `decimal` needs a cast.
- **Time.** Inside Core, times are milliseconds since 1970-01-01 of the wall-clock time as given
  (`TimeValue`); `DateTime.Kind` and `DateTimeOffset.Offset` are ignored unless a display zone is set (see
  Time zones below).
- **Step curve** is step-after: a value holds until the next point (states and setpoints).
- **Y axis title** is drawn horizontally above the axis labels instead of rotated.
- **X labels** at the plot edges align to their tick (start/end) instead of centering.
- **Markers** are hidden, except for a point with no neighbour (a single value or a value between
  gaps), which gets a small dot so it stays visible.
- **Text measurement.** The JS module measures the inherited font once: the width of each ASCII
  character. `WidthTableTextMeasurer` sums them (no kerning). Before that (prerendering), or
  without JS, `ApproximateTextMeasurer` is used and the SVG scales with its viewBox until the real
  width is known.
- **Styles.** Scene styles are a `StyleTable` of paint and text styles. Colors are CSS colors or
  theme tokens (`--rio-series-1`); the SVG renderer writes tokens as
  `var(--rio-series-1, var(--_rio-series-1))`, with the defaults defined in `RioChart.razor.css`.
- **Change detection.** A series reprocesses its data only when `Items` or `Data` is a new
  reference or `DataVersion` changes. `RioChart.Refresh()` forces a new layout.
- **Downsampling.** Line and area series with more points than twice the plot width (at least
  500) are downsampled with LTTB; each run between gaps is downsampled separately.
- **Target framework** is net10.0 only for now.

## Milestone 2

- **Tooltip and crosshair.** Core builds a `TooltipModel` per layout: pixel x/y of the drawn
  (downsampled) points plus their formatted x and y values. The Blazor chart sends it to JS once
  per layout; JS handles pointer moves locally and draws into two elements Blazor renders empty
  (an overlay `<svg class="rio-hover">` and a `<div class="rio-tooltip">`). So the scene's
  Overlay layer only holds the empty-state message for now.
- **Shared tooltip matching.** A series is listed when its nearest point is within half its median
  point spacing of the hovered x (half a band for categories and bars), so series on different
  time grids still line up. `Tooltip.Shared = false` shows only the nearest point (or the bar under
  the pointer).
- **Tooltip values** use the series `Format`, then the axis `Format`, then two decimals with
  trailing zeros dropped (compact from 1M). Time x values show at the resolution of the data:
  date, date and minutes, or date and seconds.
- **Custom tooltips.** `TooltipValue` (series) and `TooltipHeader` (chart) are
  `Func<TooltipPoint<TItem>, string?>` formatters rather than separate tooltip/axis delegates;
  the point carries x, y, the item(s) and the default text. They run in the Blazor layer once per
  layout over the points that can be hovered (`TooltipBuilder.Customize`), replacing the
  model's strings, so hover stays in JS. A new lambda on each render does not re-run them; only
  a layout change or adding/removing one does.
- **Tooltip template** (`TooltipTemplate`, a `RenderFragment<TooltipContext<TItem>>`) departs
  from the interaction model's "JS calls .NET only on meaningful events": with a template, JS
  calls `OnTooltipHover` whenever the hovered points change (one call in flight, the latest
  wins). The crosshair stays local; the content is rendered by a small internal component inside
  `.rio-tooltip` (only it re-renders), and a MutationObserver places the card when it arrives.
  So on Blazor Server the content lags one round trip, and the previous point's content shows
  until the new one arrives. The card keeps `pointer-events: none`, so links in a template are
  not clickable.
- **Point clicks** only count on or next to a point (16 px) or a bar, and win over an annotation
  underneath. Hover events are not raised to .NET (except with a tooltip template).
- **Stacking.** Series stack when they share `Stack`, axis and kind (bars, or lines/areas),
  matched by exact x value (or category). Positive values stack up and negative values down.
  Hidden series leave the stack. Stacked bars are square with a 1 px gap; stacked areas fill
  between their base and top. No percent stacking yet.
- **Multiple Y axes.** `ChartSpec.YAxes` adds axes (right side by default, outward when there are
  several). The first axis with visible series owns the grid and the zero line; the others choose
  a domain with the same number of nice steps, so their labels sit on its grid lines. An axis whose
  series are all hidden is not shown. Zeros of different axes are not aligned.
- **Data labels** are off by default (`DataLabels` per series). Bars: above the bar (below when
  negative), or inside it in the inverse color when there is no room above; stacked segments only
  inside, when the label fits. Points: above, else below. A label overlapping an earlier label or
  an annotation label is dropped. More than 1000 points: no labels, with a warning.
- **Annotations** are `AnnotationSpec` records (`<ReferenceLine>` and `<ReferenceBand>`
  components, or `RioChart.Annotations`). Values are `object`: numbers, `DateTime`,
  `DateTimeOffset`, `DateOnly`, category names, or strings (ISO 8601 times, numbers) so JSON
  specs work. Labels stay inside the plot. Hover text is an SVG `<title>`; they become focusable
  buttons only when `OnAnnotationClick` is set. Point annotations and callouts are milestone 4.

## Milestone 3

- **Polar charts** are laid out in their own pass (`ChartLayout.Polar.cs`). A chart is polar
  when any series is `Pie` or `RadialBar`; cartesian series in it are skipped with a warning, as
  are series of the other polar type and extra radial bar series.
- **Donut is a pie with `InnerRadius`.** `<DonutSeries>` only changes the default to 0.6.
- **Slices are categories.** The x value names each slice (numbers and times are formatted). The
  legend lists categories, colored by category in order of first appearance, and `HiddenSeries`
  holds category names for polar charts. Hidden slices leave the total.
- **Nested rings:** several pie series become rings, the first outermost, with a 3 px gap; the
  innermost ring starts at 0.4 of the radius unless `InnerRadius` is set.
- **Gaps between slices** are a 1.5 px stroke in the surface color, not a pad angle.
- **Pie labels:** percentages by default (`Format` switches to values). A label goes inside when
  the arc at mid radius is wide enough, else outside with a leader line (outer ring only). Outside
  labels are stacked per side; the pie shrinks to make room only when some slice needs one, and
  outside labels are dropped when the pie would get too small. Labels show values only; names
  are in the legend and tooltip.
- **Fit:** pies fit the bounding box of their sweep, so a half donut uses the whole plot.
- **"Other"** (`GroupSmallSlices`) groups two or more slices under the threshold; it uses the
  last palette color, is not in the legend, and is not clickable. Grouped categories stay in the
  legend.
- **Radial bars** are stroked arcs with round caps on a track in the grid color. The full track
  is `Max`, else a nice value above the largest. The first category is the outer track. Labels
  ("Name value") sit left of the track start and are dropped when they do not fit.
- **Hover** draws a thin halo outside the slice in the JS overlay (no explode, no DOM changes to
  the rendered slices); the donut center keeps showing the total. Entry motion is the shared
  fade, not an angle animation.
- **Negative pie values** are left out with a warning; negative radial bar values show as empty.

## Milestone 4

- **Zoom state** is view state, not part of `ChartSpec`: `LayoutOptions.XRange` (data units,
  milliseconds for time). The chart keeps it in C# and clamps it to the data extent
  (`ChartLayoutResult.XExtent`); a range covering all data means "not zoomed". Category axes
  do not zoom.
- **Gestures:** drag selects a range (mouse only), Shift+drag pans, Ctrl/⌘+wheel zooms around the
  pointer (the plain wheel keeps scrolling the page), double-click resets. Pan previews by sliding
  the series inside the clip; wheel zoom has no preview and commits 140 ms after the last wheel
  event. On touch, two fingers zoom and pan together: the x under the fingers' midpoint stays under
  it, and the range scales with their horizontal spread (counted as at least 24 units, so fingers
  above each other only pan). The preview stretches the series along x (strokes stretch too) and
  commits when a finger lifts; the finger left on the chart shows no tooltip until it lifts. One
  finger still shows the tooltip, and vertical swipes scroll the page (`touch-action: pan-y`).
- **Y fits the visible data** (only points inside the range count), and line series draw the
  visible points plus one on each side, downsampled within that window.
- **Linked charts:** charts with the same `Group` find each other in the JS module. Hover shows the
  crosshair and markers at the same x in the others (no tooltip there). A zoom or pan in one is sent
  to every zoomable chart in the group, each applying it through its own .NET side.
- **Linked plots line up:** each layout reports the insets its own labels and legend need
  (`ChartLayoutResult.NeededInsets`). After an update, the JS module takes the largest left and
  right insets in the group and calls `OnGroupInsets` on every chart that was laid out with less;
  that chart lays out again with `LayoutOptions.MinInsets`. Needs are reported before the minimum,
  so the group shrinks again when the widest chart leaves or its labels get shorter. This costs one
  extra layout per chart when the needs change, not per render. Only vertical cartesian charts take
  part; polar, horizontal bars, heatmaps and compact charts report no needs. Without JS (static
  rendering) linked charts don't line up.
- **Streaming:** `SeriesBuffer` is a locked ring buffer (it grows up to `Capacity`, then drops the
  oldest point; a `TimeWindow` drops points older than the newest minus the window). Each change
  raises `Changed` on the appending thread; the chart coalesces them and lays out again at most
  every `RefreshInterval` ms, taking a copy of the buffer (O(n) per redraw). Extents are not
  updated incrementally and the whole layout runs again, which is fine for tens of thousands of
  points. Out-of-order points are sorted in the copy (the window then is not trimmed).
- **Points and callouts** are annotations with `X` and `Y`; both expand their axes by default and
  are hidden when outside the plot. A point's label goes above the marker (below near the top).
  A callout's box sits at `OffsetX`/`OffsetY` from the point, kept inside the plot, with a
  connector to the nearest point on the box edge. They take part in label collision avoidance
  as obstacles for data labels, but are not moved themselves.
- **Dragging** works for reference lines (not bands, points or callouts, and not vertical lines
  on category axes). The layout reports `DragHandles` (pixel position and scale); the renderer
  marks annotation groups with `data-key`, and JS moves the group while dragging (mouse or touch)
  and sends only the dropped value. The line snaps back unless the app stores the new value
  (`@bind-Value` or `OnAnnotationChanged`).
- **Time zones.** `SeriesData` keeps each time's UTC offset when the input had one
  (`DateTimeOffset`, `DateTime` with Kind Utc or Local) in `TimeOffsets`; without a display zone it
  is ignored. With `ChartSpec.TimeZone`, the layout converts time data to instants
  (`TimeConversion`): known offsets are used; other times follow `UnspecifiedTimes` (UTC by default,
  with a warning when it is not set). Ticks are generated on wall-clock time in the zone and
  converted back; a tick that repeats (autumn) is kept once, one in the spring gap moves to the
  hour after it. Kind Local means the server's zone on Blazor Server. `"local"` is the browser's
  zone, sent by the JS module (UTC until it is known). Columnar `SeriesBuffer` times have no offset.
  Zone names use `TimeZoneInfo` (IANA ids work on Windows too); Blazor WASM in invariant time zone
  mode only knows UTC. With a zone, `VisibleRange`, point clicks and dragged values use
  `DateTimeOffset` in the display zone.
- **Canvas series layer.** With `SeriesLayer.Canvas`, the SVG renderer leaves out the series groups
  and `CanvasSeriesWriter` sends them to JS as draw operations: SVG path data (read with `Path2D`),
  rectangles and CSS paint. This is JSON, not binary; the paths are already downsampled, so it
  stays small. The canvas sits over the SVG, scaled by `devicePixelRatio`, and clipped to the plot.
  So annotations and data labels sit under the series there, unlike in SVG mode. Colors are CSS
  values resolved in the browser; the canvas repaints when a `class` or `data-theme` attribute
  changes anywhere in the page or the OS color scheme changes. Downsampling defaults to four times
  the plot width (at least 2000 points) in canvas mode. Pan previews by redrawing with an offset.
  There is no entry fade on the canvas.

## Milestone 5

- **`RioCharts.Skia`** renders the same scene as the Blazor SVG renderer. Layout uses
  `SkiaTextMeasurer` (real widths of the first installed font in `ExportOptions.FontFamily`, else
  Skia's default font), so labels fit as in the browser.
- **Colors:** an image has no CSS, so theme tokens resolve through `ExportTheme` (light and dark
  match the component's built-in defaults; `With` overrides a token). SVG export writes concrete
  colors, or `var(--rio-…, color)` with `CssVariables`. The faded look of hidden legend items
  (CSS in the component) is reproduced as 40% opacity.
- **SVG export** has its own small writer (`SvgExporter`) instead of sharing the Blazor
  `SvgWriter`, so the package does not depend on ASP.NET; both write the same path data.
- **PNG:** text baselines follow SVG's `hanging`, `central` and `alphabetic` from the font
  metrics; arcs convert from clockwise-from-12-o'clock radians to Skia's degrees. Full circles are
  drawn as two halves. `Scale` sets the pixel density (default 2).
- **Fonts on Linux servers:** the package brings `SkiaSharp.NativeAssets.Linux.NoDependencies`;
  install fonts in the container (e.g. `fonts-dejavu`) for good text.
- **Not yet:** PDF. (Blazor's `ExportSvgAsync`/`ExportPngAsync` and downloads: see Client-side export.)

## Milestone 6

- **One model.** JSON specs are `ChartSpec` with data bindings added (`ChartSpec.Dataset`, `Data`;
  `SeriesSpec.Dataset`, `X`, `Y` and the transforms). `ChartSpecBinder` turns them into the
  ordinary model (concrete series, generated ids, `groupBy` expanded) plus `SeriesData`, which
  then goes through the same layout as the components. The binder lives in Core so the Blazor
  `<Chart Spec>` and the AI tool share it.
- **Tolerant reading** happens on the JSON before deserializing: letter case, comments and
  trailing commas, a single series object or top-level x/y, and synonyms (column, stackedBar,
  doughnut/donut/ring → pie with innerRadius 0.6, spline/smooth → monotone, gauge → radialBar,
  avg/mean, daily/monthly, asc/desc, >=, ...). Unknown fields are ignored. Unknown enum values
  are errors listing the valid ones. Limits: 20 series (after groupBy), 1000 inline rows, 50
  annotations.
- **Errors** are `SpecError(path, message)`. Field errors list the dataset's fields with their
  types and suggest a close name ("did you mean 'supplyTemp'?"). Parsing errors come first; binding
  errors appear once the spec parses, so a model fixes problems in two rounds at most.
- **Defaults the binder fills in** (reported as warnings): x = the first time, else text field;
  y = the only numeric field; aggregate average with a timeBucket; sum when a text x repeats.
- **"auto"**: time or numeric x → line (bar with a time bucket); text x → bar, or a donut when
  there is one series of 2–6 positive values adding up to 100 (or 1). Deliberately conservative:
  a donut only when the data is clearly shares of a whole.
- **Datasets** are columns of double, DateTime/DateTimeOffset or string, with the type inferred
  from the values (mixed columns become text; ISO 8601 strings become times; quoted numbers stay
  text). `ChartDataset.FromItems` uses reflection (camelCase property names): fine for setup,
  not for hot paths.
- **create_chart** takes the spec as a JSON object (or a string holding one) and returns success,
  errors, warnings, a summary (the chart's text description) and the normalized spec. The PNG,
  SVG and bound spec are for the app (`ChartCreated`) and are not sent to the model; over MCP
  the PNG is returned as an image block. The tool description lists the datasets with their
  fields and descriptions, the spec shape and three examples; the full schema is a separate tool
  (`get_chart_schema`) to keep the description short.
- **render_chart** takes the same spec plus `format` (png or svg), `width`, `height` (100 to
  `MaxImageSize`, default 4000), `theme` (light or dark) and `fileName`, and returns the image to
  the agent: as `DataContent` (image/png or image/svg+xml) after the result JSON for
  Microsoft.Extensions.AI, and over MCP as an image block (PNG) or an embedded text resource with
  MIME type image/svg+xml (MCP image blocks are for raster images). It raises `ChartCreated`
  too (after writing the file), so an app can show what the agent made; `ChartToolResult.Tool`
  says which tool made a chart, and a render_chart result can be SVG with `Png` null. With `ChartToolOptions.OutputDirectory` set it also writes the file and returns
  its full path in `file`; the name comes from `fileName` or the title, with folders stripped,
  unsafe characters replaced and the extension forced to the format, so the agent cannot write
  outside the directory. Whether a model can see an SVG it gets back depends on the provider; most
  only look at raster images, so PNG is the default.
- **riocharts-mcp** (`src/RioCharts.Mcp`) is a stdio MCP server packed as a .NET tool. It offers
  only render_chart, list_datasets and get_chart_schema: with no app to show a chart, create_chart
  makes no sense. render_chart there uses `ChartTool.StandaloneRenderDescription`, which carries
  the spec format and points to list_datasets instead of listing datasets, since files may change
  while the server runs. Logging goes to stderr; stdout is the protocol. Images always go to
  `--out` (default `./charts` under the agent's working directory). There is no `--data` default:
  scanning the working directory would list unrelated JSON files.
- **CSV and folder data:** `ChartDataset.FromCsv` detects the delimiter (comma, semicolon, tab)
  from the header row, reads quoted cells per RFC 4180, numbers with a decimal point, and a
  decimal comma only with a semicolon or tab delimiter (Excel's export in many locales); empty
  headers become `column<n>` and duplicates get `_2`. `FolderDataSource` reads `.csv`, `.tsv` and
  `.json` (an array of objects, or an object with one array) in one folder, caches by write time
  and size, skips files over 100 MB and lists unreadable files with the reason and no fields.
- **Spec → Razor** writes component markup with field names as PascalCase properties; transforms
  that components do not do become comments to implement in the query. An `aggregate` without a
  `timeBucket` becomes the series' `Aggregate` parameter.
- **Not yet:** a time zone-aware `timeBucket` (buckets are on wall-clock time), declarative joins,
  and streaming specs.

## Aggregating series components

- Design.md lists aggregate only as a declarative spec transform. The series components also
  have it, modelled on Blazor-ApexCharts' `YAggregate`: `Aggregate` (the enum the specs use) and
  `YAggregate` (`Func<IReadOnlyList<TItem>, double?>`, which wins when both are set).
- Items group by the x value as `XValue` returns it: strings ordinally, times exactly (no
  bucketing; two timestamps a millisecond apart are two points), numbers by value (1 and 1.0 are
  the same x). Items with a null x are left out. Categories keep first-appearance order.
- Grouping runs in `SeriesData.FromGroups`, once per data change (new Items reference,
  `DataVersion`, or a change of `Aggregate` or of whether `YAggregate` is set). Changing the
  `YAggregate` function alone does not reprocess, the same as `YValue`. `Data` (columnar) and
  `Buffer` (live) are not aggregated.
- A point's source index is its group's index; the series maps it back to the group's item
  indexes. `PointClickEventArgs.Item` and `Index` are the group's first item, `Items` the whole group.
- Without an aggregate, duplicates are drawn as given. The layout warns about them for category
  x, bars and polar series; lines on numbers or times may repeat an x on purpose (a vertical step)
  and get no warning.

## Client-side export

- **In the browser, not via Skia.** The JS module clones the drawn `<svg>` and writes the
  computed presentation properties (fill, stroke, opacity, fonts, ...) as attributes, so theme
  variables, app CSS overrides and the faded hidden legend items come out as plain colors;
  inherited properties are written only where they differ from the parent. Classes, Blazor's
  scoped-CSS attributes and render-marker comments are dropped. The canvas series layer is added
  on top as a PNG `<image>`. PNG draws that SVG onto a canvas at `Scale`. This keeps the Blazor
  package free of SkiaSharp (large for WebAssembly) and matches the page exactly; Design.md's
  "keep the JS module tiny" gives way here because only the browser knows the resolved CSS.
- **Fonts:** an SVG drawn as an image cannot load the page's web fonts, so PNG text uses the
  viewer's installed fonts (the next font in the list). Server export with `RioCharts.Skia` is the
  route for exact fonts or images without a browser.
- **Size limits:** `ExportPngAsync`/`ExportSvgAsync` receive the bytes as an `IJSStreamReference`
  (up to 256 MB), not a JS interop result, which Blazor Server caps at
  `MaximumReceiveMessageSize` (32 KB). `Download…Async` never sends the image through .NET.
- **CSV** (`ChartCsv`, Core) uses the data as bound (after per-x aggregation, before downsampling
  and zoom), every series including hidden ones. Rows are the union of x values; a repeated x gets
  as many rows as the longest run, so no point is lost. Times are written as given (ISO 8601, with
  the input's offset when it had one; dates alone when every time is midnight). The download adds
  a UTF-8 byte order mark so Excel reads non-ASCII names.
- **Menu:** plain Blazor state with a transparent backdrop that closes it on an outside click,
  and Escape; focus moves to the first item on open. Its selectors are prefixed with `.rio-chart`
  so broad app rules (e.g. a docs site's `.doc button`) do not restyle it.
- **`OnExporting`** is the menu's event, cancelable like `OnZoomChanging`. Its content getters
  are lazy, so on Blazor Server an image crosses to .NET only when the handler asks for it. It is
  raised for menu picks only: code that calls `Download…Async` already decides what happens.
- **`ExportFormats`** is a flags enum (menu order stays PNG, SVG, CSV); a single format turns the
  button into a direct download (labelled with the format, no `aria-haspopup`).

## Benchmarks

- `benchmarks/RioCharts.Benchmarks` (BenchmarkDotNet) measures public APIs only: Core layout,
  data and spec work, and Skia export. The Blazor side (SVG render tree, interop payloads) and the
  JS module are not benchmarked. Not run in CI (timings on shared runners are too noisy); the
  project is in the solution, so CI builds it. `benchmarks/README.md` holds a baseline from one
  machine, to compare changes on the same machine rather than as absolute numbers.
- Layout work shared within one `ChartLayout.Compute`: `SeriesFacts` (count, extremes, last, trend)
  per series feed the legend tooltips, the series' ARIA labels and the chart description; tooltip
  x labels are cached by point index when all series have identical x values (up to 10,000
  points; larger series are downsampled per series and the array would go on the large object
  heap). Nothing is cached across layouts: `SeriesData` exposes its arrays and could be changed.

## Component tests (bUnit)

- Design.md's testing list names Core unit tests, Verify snapshots and BenchmarkDotNet; component
  tests with bUnit are added on top, in their own project (`tests/RioCharts.Blazor.Tests`) so the
  main test project has no ASP.NET test dependencies.
- `ChartTestBase` sets bUnit's JS interop to strict mode and sets up the module's functions
  (`init` reports a 600 px container). A test that needs JS to fail (static rendering) sets an
  exception on `Init`. `ChartContainerInfo` moved out of the generic component and became
  `internal` so the tests can name it (`InternalsVisibleTo`).
- Not covered: what runs in the JS module (image export, hover, zoom drags, canvas painting).
  That needs browser tests (e.g. Playwright against the docs site), not set up yet.

## Keyboard navigation

- **Focus target:** the chart's `<svg>` itself (`tabindex="0"`) with `role="application"` and
  `aria-roledescription="chart"`, so screen readers pass the arrow keys to it (with `role="img"`
  they would stay in browse mode). Its `aria-describedby` names the text summary and a hidden
  instructions span. Charts without points (empty, or `KeyboardNavigation="false"`) keep
  `role="img"` and no tab stop. Legend items and clickable annotations stay their own tab stops
  inside it.
- **In the browser:** the JS module moves through the tooltip model it already has (pixel
  positions, formatted values), so keys cost no round trip; the model is now sent whenever
  keyboard navigation is on, also with tooltips off (then only the crosshair shows). Points are
  the drawn ones: downsampled and limited to the zoomed range. Only Enter calls .NET
  (`OnPointClicked`, as a pointer click).
- **Keys:** Left/Right step along the focused series, Up/Down move to the next series' point
  nearest the same x, Home/End, PageUp/PageDown (a tenth of the points); pies and radial bars
  step through the slices with any arrow. The first key lands on the first point (the last for
  Left/End). Escape or leaving the chart clears the focus; the next key starts again.
- **Announcements:** a visually hidden `aria-live="polite"` region gets "x: series value, ...
  n of N" (the focused series first) or "series, slice: value. n of N". Texts are English, like
  the Reset zoom button; localizing the built-in texts is a separate topic.
- **Live data and resizes:** a new layout keeps the focused point (by index, clamped) while the
  chart has focus, without announcing it again.
- **Zoom keys** (zoomable charts): + / = and - / _ zoom 2x around the focused point's x (the
  middle without one), Shift+Left/Right pans 20%, 0 resets. They reuse the pointer's commit path
  (`OnZoom`/`OnZoomReset` on this chart and its zoomable group), so clamping, `OnZoomChanging`
  and binding behave the same. The focused data x is remembered and, when the new layout
  arrives, the focus moves to the nearest point at that x and is announced again (the index
  changes with the visible range). The zoomed range itself is not announced (JS has no
  formatted range labels).
- **Not yet:** moving through hidden series' data or annotations with the arrows.

## Irregular time series in the shared tooltip

- Series need no common interval or timestamps; the shared tooltip anchors on the drawn point
  nearest the pointer and lists each other series' nearest point within its snap distance (half
  its median spacing). A line series with no point near that x is left out rather than showing a
  value from another time.
- Step lines (step-after) have a value everywhere between their first and last point, so they
  are always listed with the last point at or before the anchor (`TooltipSeries.Step`), marked at
  that point (where the value was set), not on the crosshair. The header is the label of a row with a point at the anchor.
- Not yet: interpolated values for line series between their points.

## Compact charts

- **No separate component.** Design.md's "compact modes: sparklines" is `Compact` on `RioChart`
  and in the spec, so the JSON/AI spec and Skia export get it too. The height is not changed
  automatically; examples set it.
- **Layout:** the same `ChartLayout.Compute` pass with the chrome left out: no title, legend,
  axis labels or titles, grid (unless an axis sets `GridLines`), baseline (except a zero line for
  bars that go negative), data labels, annotation text or "No data" text. The plot is the box
  minus an inset: half the widest stroke, or a highlight dot plus its outline. The minimum chart
  size drops from 50 to 8 px.
- **Y range:** the data extent as is (`TightDomain`): no nice rounding and no ticks. Lines and
  areas leave zero out; bars keep it. Equal values are padded by 10% so the line sits in the
  middle. Bands still do not widen the range (`ExpandsDomain` defaults to false for bands).
- **Highlight dots** (`PointHighlight` flags) are picked from the data within the visible range,
  not from the downsampled points, so a min or max dropped by LTTB is still marked. They are
  outlined in the surface color so they stand out on the line. Coinciding picks draw one dot.
- **Summary:** `ChartDescriber.DescribeCompact` (range, last value, trend in words), formatted
  with the series' or axis' format like the tooltips.
- **Keyboard** defaults to off for compact charts (`KeyboardNavigation` became `bool?`), so a
  table of sparklines is not a long row of tab stops; the summary is read instead.
- **`Interactive="false"`** skips the JS module entirely (no import, init, measuring or point
  data) and the hover/tooltip elements; it is read on the first render. Without JS there is no
  measured width, so the chart lays out at `Width` or a default (640, compact 160) and the SVG
  scales to its container.
- **JSON:** `highlight` accepts "last", "last, max" or ["last", "max"] and is normalized to the
  enum's comma-separated form; unknown values get the list of valid ones.
- **Auto-compact** (below) covers normal charts that get small.



## Languages and texts

- **`ChartTexts`** (Core) holds every text the chart writes itself: empty states, "Total", "Other",
  "Reset zoom", the download button, and the screen reader texts (description, axis and legend
  names, series labels, keyboard help, the "3 of 12" position the JS module appends). Spec
  warnings and errors stay English: they are for developers.
- **Built in:** English, Swedish, Norwegian (Bokmål, under "no" and "nb"; Nynorsk falls back to it
  through its parent "no"), Danish, Finnish, German, French, Spanish. The non-English texts were
  written without a native-speaker review.
- **Lookup:** the Blazor `Texts` parameter, then a cascaded `ChartTexts`, then
  `ChartTexts.For(Culture ?? CurrentUICulture)`, which walks the culture's parents through
  registered languages first, then built-in ones, then English. Core and Skia use
  `options.Texts ?? ChartTexts.For(options.Culture)`. The cascaded value is the per-user hook in
  Blazor Server (a static resolver would be shared by every circuit).
- **Storage:** a dictionary behind init properties, so `with` works (a private copy constructor
  clones the dictionary), `From` reads by key without reflection (trimming-safe), and equality is
  by value, so `Texts="@(ChartTexts.Swedish with { ... })"` in markup does not lay the chart out
  on every render. Unset or empty texts fall back to English.
- **Templates:** composite format strings; "one|other" picks the first form for a count of 1
  (enough for the built-in languages, not for languages with more plural forms). A template that
  fails to format falls back to the English one, never throwing.
- **Filling templates from code:** `texts.Fill(key, provider, args)` (the key is the property
  name, e.g. `nameof(ChartTexts.DownloadFormat)`) fills a template the same way, with the English
  fallback; the static helper behind it is internal.
- **Description wording:** the full description says "trend rising/falling/stable" (was
  "increasing/decreasing"), the same words as the compact summary.


## Trimming, native AOT and static rendering

- **Analyzers:** Core, Blazor, Skia and AI set `IsAotCompatible` (trimmable, trim/AOT/single-file
  analyzers on). The only warnings were reflection-based System.Text.Json in `ChartSpecJson` and
  the AI tool's result JSON.
- **Spec JSON** uses `ChartSpecJsonContext` (source-generated, Web defaults, plus primitive types
  for the `object?` values of specs built in code). camelCase enum values come from generic
  `JsonStringEnumConverter<T>` instances, one per spec enum, added to the options.
- **Init-only properties:** the generator treats them as constructor arguments. It has no
  `CreateObject` for the spec records, cannot set them afterwards ("Setting init-only properties
  is not supported in source generation mode") and writes `default(T)` for a property missing
  from the JSON, so an annotation's axis became x and `"tooltip": {}` turned the tooltip off.
  `Parse` therefore fills every missing property from a serialized default instance before
  deserializing, walking nested objects and lists through the generated metadata. The default
  instances come from `Factories` (one entry per spec record). A test checks that every object type
  reachable from `ChartSpec` has one. Changing the records to `set` would have avoided this but
  made the spec mutable.
- **Skipping defaults when writing** uses the same factories (`JsonTypeInfo.CreateObject` is null
  for these types).
- **Proof:** `tests/RioCharts.AotSmoke` (`PublishAot`, warnings as errors except NuGet audit
  advisories). Locally it is published trimmed (no C++ linker on the dev machine) and passes. The
  "Native AOT" workflow publishes and runs it on Linux. The docs site (Blazor WebAssembly, trimmed
  on publish) publishes without trimming warnings.
- **Static rendering:** `HtmlRenderer` renders the chart without calling JS. `Live` (`Interactive`
  and `RendererInfo.IsInteractive`) gates the tab stop, hover layer, download button and canvas.
  Static pages and prerendering get plain SVG at `Width` or the default width, and the interactive
  render adds the rest. bUnit tests set an interactive `RendererInfo`.
- **NuGet advisory:** restoring the smoke app pulls `Microsoft.DotNet.ILCompiler` 10.0.12, which
  has a high-severity advisory (NU1903). It is a build-time compiler for this test app only, not
  a dependency of the packages; a newer SDK brings a fixed version.


## Auto-compact

- **On by default** (`AutoCompact`, Design.md: "auto-hiding axes and legend at small sizes").
  `FitChrome` decides from the chart's size what it has room for, in this order: x axis title
  (180 px high), legend (160 px high, 200 px wide), y axis titles (150 px high, 200 px wide), title
  (120 px high), x labels (90 px high), y labels (200 px wide). Below 120 × 60 px the chart is laid
  out as a compact chart (`AutoCompacts`), which also allows sizes under 50 px and uses the short
  summary. The sizes are for the 12 px font and scale with `FontSize`.
- **Legend:** a right legend moves to the top under 400 px wide; an automatic legend taller than
  30% of the chart is left out. `LegendMode.Show` always shows it (it was asked for).
- **Still there when hidden:** the description keeps the title, the svg keeps its aria-label, and
  the tooltip names the series and values. Hidden y labels keep the grid lines.
- **Polar charts** shed the legend and title by the same rules; they have no compact layout.
- **Deterministic thresholds, no hysteresis:** a chart resized across a threshold switches at
  the same size both ways.


## Visual snapshots

- **What:** `SnapshotTests` lays out 18 reference charts with the approximate text measurer and
  writes them with `SvgExporter` (light theme, sans-serif), compared as text with
  `tests/RioCharts.Tests/Snapshots/*.svg`.
- **Deterministic by design:** no Skia measuring or rasterizing (fonts differ per OS), invariant
  or fixed cultures, a fixed viewer time zone, and numbers rounded by `SvgWriter.N`. The Test
  workflow runs them on Linux; development is on Windows.
- **No library:** a 30-line comparer instead of Verify, so there is no diff tool launching and no
  extra dependency. A mismatch writes `name.received.svg` (git-ignored) and fails;
  `RIOCHARTS_ACCEPT_SNAPSHOTS=1` accepts. A new case writes its snapshot and fails once, so every
  baseline gets looked at.
- **Not covered:** the Blazor SVG renderer's own markup (covered by bUnit tests) and PNG output.


## Legend tooltips and the pie's "Other"

- **Grouping before the legend:** `PieSlices` decides per ring which slices fall under
  `GroupSmallSlices`; `ComputePolar` runs it before building the legend. A category grouped in
  every ring it appears in is left out of the legend, and one "Other" item (key
  `ChartLayout.OtherSliceKey`, color of the last series token like the slice) takes its place.
  Hiding "Other" leaves its slices out and the shares are of the visible slices. Colors of the other
  categories keep their index, so they do not shift.
- **Tooltip text** is built in Core (`LegendEntry.Tooltip`, lines separated by `\n`): a series uses
  `ChartDescriber.DescribeSeries`, the same text as the compact summary; a slice gets its value and
  share per ring. "Other" is the total, then each grouped slice: `Other\t15 k€ (2%)`, then
  `• Gift cards\t9 k€ (1%)` per part. The JS module lays a text with tabs out as rows (name left,
  value right) and lines starting with "• " as the parts under the line above: smaller, muted and
  indented under a thin rule, so the total and its parts are easy to tell apart. As plain text (a
  native `<title>`, `aria-description`) it still reads as a total followed by a list.
- **The "Other" slice's own tooltip** lists the same parts (`TooltipSlice.Parts`, `Pl`/`Pv` in the
  interop slice model), laid out the same way; keyboard navigation reads them after the total.
- **Rendering:** the scene puts it in the legend group's `Title`. The Skia exporter and static or
  prerendered Blazor charts write an SVG `<title>` (a native tooltip). Interactive charts render
  `data-tip` and `aria-description` instead (`SvgSceneRenderer.LegendTips`), and the JS module shows
  it in a `.rio-legend-tip` card under the item on pointerover and focusin, so there is no second,
  native tooltip.


## JS interop payloads

- **The bug:** marking `RioCharts.Blazor` AOT-compatible also made it trimmable. Blazor serializes
  JS interop arguments with reflection-based System.Text.Json, and the trimmer removed the
  constructor parameter names of the anonymous payload types, so every interactive chart threw
  `ConstructorContainsNullParameterNames` in published WebAssembly apps. The trim analyzer cannot
  see it: interop takes `object[]`. The dev server does not trim, so only the published site broke.
- **Now:** the payloads (`update`, `paint`, image options) are records in `Rendering/Interop.cs`,
  serialized with the source-generated `InteropJson` to UTF-8 bytes. Blazor passes `byte[]` as
  binary (a `Uint8Array` in JS) and the module's `json()` parses it. Init's reply is read as a
  `JsonElement` (`ChartContainerInfo.From`), which needs no type metadata.
- **Guard:** a bUnit test renders a chart that uses every JS call and fails if any argument is not
  a primitive, `byte[]`, `ElementReference` or `DotNetObjectReference`. A published (trimmed) docs
  build was checked in the browser before and after.


## Scatter

- **Type:** `ScatterSeriesType` draws one path of circles (5 px by default, translucent fill, no outline).
  Default layer 3, above lines.
- **Thinning, not downsampling:** each point is data, so LTTB does not apply. Above the downsampling
  threshold (visible points), at most 3 markers are drawn per square cell one marker radius wide
  (`MarkersPerCell`). Three markers at 0.75 opacity cover about 98%, so the picture does not change:
  rendered with and without thinning, at most 0.07% of pixels differ visibly (uniform noise; 0.01%
  for clouds). One per cell was visibly lighter in the middle of a 5k cloud; two still showed.
  Sparse areas and outliers keep every point. Hover, tooltips and data labels use the drawn points;
  the description and CSV use all data. Drawn markers are bounded by the plot area (about 3 per
  r² of plot), not by the data: 100k points in a Gaussian cloud draw about 20k, 1M about 33k;
  uniform noise over the whole plot is the worst case (86k of 100k).
- **Trend line:** least squares over all visible points (also those thinned out) in data units, drawn from the first to the
  last point's x (dashed, series color). No equation or r²: the legend tooltip says rising/falling.
- **Axes:** numeric x gets `NiceDomain(min, max, 10)` (steps of 5 for a typical outdoor range),
  unless zoomed or `X.Min/Max` are set; y has no forced zero. The plot clip grows by the marker
  radius so edge points are not cut.
- **Tooltip:** scatter points are hit-tested in 2D (all points within 40 px in x, nearest by
  distance; x is sorted so the scan is short). A chart with any scatter series is not shared, since
  "everything at this x" means little for unordered points. No crosshair; the hovered point gets a
  ring. The tooltip header is "x axis title: x" for scatter.
- **Not yet:** bubble (a third, size field: `SeriesData` has no column for it), per-point colors,
  and log axes.


## Heatmap

- **Rows are series.** No third data column: each `HeatmapSeries` (or each `groupBy` group in a
  spec) is a row, named by the series, top to bottom in series order. This reuses `SeriesData`, the
  binder's grouping and the keyboard's row navigation as they are.
- **Own layout path** (`ComputeHeatmap`, like polar): row labels on the left, x labels below, no
  grid or y scale (`YScale` is a dummy 0..rows scale). Other series types in the same chart are
  skipped with a warning. No zoom (`XScale` is null).
- **Color:** the first row's color (default the first series token) with fill-opacity from 0.08
  (lowest) to 1 (highest or `Max`). Opacity over the surface rather than computed colors keeps
  theme tokens, dark mode and Skia export working without color math. Cells have a 1 px gap and
  2 px corners when they are larger than 6 px.
- **Legend:** a color scale (lowest value, five steps, highest value) instead of series items;
  `ChartLayoutResult.Legend` is empty.
- **Tooltip:** `TooltipSeries.CellHeight` marks heatmap rows; JS hit-tests the cell under the
  pointer and outlines it. Not shared.
- **Not yet:** diverging scales (below/above a midpoint), custom color ranges, and calendar layouts
  (weeks × weekdays from dates) beyond what grouping gives.


## Range area

- **Data:** `SeriesData.Low`, an optional second value column (Design.md: "extra columns for
  multi-value types"). `WithLow` takes the lows in input order and places them by `SourceIndex`,
  so they follow their points through sorting; `WithX` (time zone conversion) carries them.
  Live buffers (`SeriesBuffer`) have no low column yet.
- **Drawing:** the layout sets `Resolved.Lower` to the lows, so the existing two-edge area path
  (`AreaBetween`, used for stacks) draws the band. Fill 0.2, no outline, default layer 0 (below
  lines). Not stacked even with `Stack` set. Missing low or high: a gap.
- **Aggregation:** in Blazor, `Aggregate`/`YAggregate` give the high end and the low end is the
  lowest low per point. In specs, average and sum become min (low) and max (high), since averaging a
  band's ends would hide its extremes; other aggregates apply to both ends.
- **Not yet:** an outline option, live data with lows, and error bars.


## Horizontal bars

- **A chart option, not a series type** (`ChartSpec.Horizontal`): orientation is a property of the
  whole chart. JSON `"horizontalBar"` (and synonyms) maps to `bar` and sets the chart flag.
- **Own layout path** (`ComputeHorizontal`): rows from the categories (or the distinct x values as
  labels), first on top; a nice value scale along the bottom (`spec.Y` gives title, format,
  min/max); vertical grid lines and a zero line. Grouping and stacking are computed here (stack
  per row, positive and negative separately). Only bar series; others are skipped with a warning.
- **Labels:** row labels get at most 45% of the width and are shortened with "…" (the tooltip
  keeps the full name). Data labels go after the bar end, inside it when there is no room,
  and inside stacked segments only when they fit. Value lines and bands are supported (other
  annotations are skipped with a warning); their labels sit in a strip above the rows.
- **Hover:** `TooltipSeries.CellWidths` gives each bar's length; JS checks the rectangle under the
  pointer (a linear scan, also used for heatmap cells now) and outlines it. No zoom.

## Performance

- **Check (2026-11):** a Stopwatch program over Core, Skia and the Blazor static render (median of
  5 runs, Release, .NET 10, one desktop core), plus the docs site in the browser. Guards in
  `LargeDataTests` catch a return to quadratic work.
- **After the fixes**, plot about 900 px wide:

  | Case | Time | Allocated |
  | --- | --- | --- |
  | Line, 100k / 1M time points | 5 / 40 ms | 0.3 MB |
  | Line, 1M points, zoomed to 10% | 23 ms | 0.3 MB |
  | 3 lines, 1M points each | 95 ms | 1 MB |
  | 3 stacked areas, 1M points each | 117 ms | 63 MB |
  | Scatter, 100k points (cloud / uniform noise) | 26 / 85 ms | 11 / 37 MB |
  | Bars, 10k categories | 7 ms | 4 MB |
  | Pie, 1000 / 10 000 slices | 40 / 35 ms | 2 / 20 MB |
  | Heatmap, 50 rows x 365 days | 25 ms | 7 MB |
  | Live buffer, 100k points: append + snapshot + layout | 4 ms | 2 MB |
  | `SeriesData.From` 1M times / `FromItems` 1M items | 11 / 60 ms | 23 / 53 MB |
  | Spec binding, 200k rows into day buckets per store | 30–70 ms | 20 MB |
  | `ChartDataset.FromCsv`, 200k rows | 200–320 ms | 64 MB |
  | Skia PNG, 900 x 360 at 2x | 45–85 ms | 0.5 MB |
  | Blazor static render, 1M items | 100 ms | 54 MB |

- **What was slow:** pie outside labels untangled leader lines in up to 40·n² steps over all pairs
  (now: only labels that fit, 40·n steps, no allocation per check); pie color and legend lookups
  used `IndexOf` per category; the description copied every point into a list of tuples (twice per
  series with legend tooltips); stacking kept a dictionary entry per point; every x was mapped to
  a pixel before downsampling (`PixelPositions` maps on demand); PNG encoding tried every filter.
- **Known costs, left as is:**
  - Scatter: thinned to what can be seen (see Scatter); uniform noise over the whole plot still
    draws most points, so use the canvas layer for big scatters.
  - `FromItems` boxes each x (the selectors return `object`); `SeriesData.From` with columns is
    the fast path for big data, as the Large data page says.
  - In the browser, hover handling is about 0.05 ms per pointer move at 200k points. A full layout
    (zoom, data change) runs in WebAssembly's interpreter, several times slower than on the
    server: for the 200k points on the published docs site, about 0.6 s before these fixes and
    0.1 s after.

## Selection and tooltip from code

Design.md "Programmatic interaction": selection (`@bind-SelectedItems`, `@bind-SelectedRange`,
`OnSelectionChanged`) and `ShowTooltipAsync`/`HideTooltipAsync`.

- **Modes:** `Selection="SelectionMode.Points"` or `Range`; `None` (default) still shows selections
  set from code. Points: a click selects the point's items (all of them when the series
  aggregates), Ctrl/Cmd/Shift+click adds or removes, a click beside the points or clicking the only
  selected point again clears; Enter on a focused point selects it (Shift+Enter adds). Range: a
  mouse drag across the plot, a click without a drag clears. With `Zoomable` too, the drag selects
  and Shift+drag still pans; zoom with Ctrl/Cmd+wheel, pinch or the keys.
- **State is items, not indexes:** the chart keeps the selected `TItem`s, so a selection survives
  new data, sorting and zoom. A range also keeps its x values, and selects the items of the visible
  series' points inside it (`GetItemsInRange`, binary search on the sorted x). When the parent changes
  `SelectedRange` without new `SelectedItems` (e.g. a Clear button setting the range to null), the
  items follow the range after the next layout and `SelectedItemsChanged` reports them; until then
  no point markers are drawn, so the old range's items never show as selected points.
- **Drawn by Core** (`LayoutOptions.SelectedRange`, `SelectedPoints`): a band in the first series
  color behind the series with edge lines (like the zoom drag), markers on lines, areas and
  scatter points, an outline around bars (its own segment when stacked). In the scene, not the JS
  hover layer, so it shows without JS, in exports and moves with the pan preview. A range shows as
  the band only, not as markers.
- **Item to point:** each layout maps the selected items to point indexes per series (through
  `InputIndexes`), O(points) per layout and only while points are selected.
- **Tooltip from code:** .NET maps the x value to a pixel (scale or category band) and calls
  `showTooltip`; JS draws it like keyboard focus (shared rows, or the nearest point when the
  tooltip is not shared), and linked charts show the crosshair. .NET sends it again after each new
  layout, and JS puts it back when the pointer leaves, until `HideTooltipAsync`.
- **Not yet:** drawing the selection on horizontal bars, heatmaps and pies (clicks there still
  select and raise the events), point selection for columnar data (no items), selecting a range
  with the keyboard or on touch screens, a shared selection across linked charts, and
  highlighting a series from code.

## Series, coordinates and batching from code

The rest of Design.md "Programmatic interaction".

- **Series:** `ShowSeriesAsync`, `HideSeriesAsync`, `ToggleSeriesAsync` and `IsSeriesVisible` change
  the same hidden set as the legend and raise `HiddenSeriesChanged`, but not the legend events.
  `OnLegendClicking` (cancelable) comes before a legend click's toggle; `OnLegendClick` follows only
  when the series was toggled.
- **Highlight:** `HighlightSeriesAsync(id)` fades the other series and their data labels (opacity
  0.2, 150 ms, none with reduced motion). It is a class the JS module sets on the series groups, not
  a layout: no round trip per hover, and it is applied again after each layout. Hovering or focusing
  a legend item highlights its series meanwhile (JS only). The SVG renderer now writes `data-key`
  on series, data label and legend groups (before: annotations only) so the module can find them.
  Nothing fades when the series is not drawn as such (hidden, pie slices, the canvas layer).
- **Coordinates:** `ValueToPixel(x, y, yAxis)` and `PixelToValue(x, y, yAxis)` use the last
  layout's scales; pixels are CSS pixels from the chart's top left (its SVG coordinates). Category
  axes give the nearest category. Null for pies and horizontal bars.
- **Batching:** `ApplyAsync(changes)` holds rendering (`ShouldRender` is false while it runs) and
  renders once at the end. Events and bound parameters are still raised as each change happens;
  JS calls (tooltip, highlight) go out at once.
- **Not yet:** highlight on pies and the canvas layer, coordinates for horizontal bars, an
  `OnSeriesClick` event, opt-in hover events, legend and data label templates, a custom overlay
  `RenderFragment`, and undo/redo.

## Public API surface

- Reviewed before the first release. Public: the spec model, data types, scales, formatters, the
  scene types (renderers in other packages and custom renderers draw them), `ChartLayout`, and
  each package's entry points. Internal: SVG/canvas writers and the scene renderer component in
  Blazor, Skia's color and path helpers, `ChartSpecJson.Options` (deserializing with it skips the
  defaults fix-up that `Parse` does) and the components' `Spec`.
- **JS callbacks:** the module calls a private `JsCallbacks` object (`DotNetObjectReference`), so
  the `[JSInvokable]` entry points are not methods on `RioChart`. The method names are unchanged.
- **`SvgSceneRenderer`** is internal, and Razor only resolves public components as tags, so
  `RioChart` renders it from code (`SceneFragment`).
- **`ChartTool` names stay constants** (`ToolName`, `RenderToolName`, `RenderDescription`): the MCP
  attributes need compile-time values.
- **Missing XML docs** (CS1591) warn in the libraries; test and sample projects turn it off.

