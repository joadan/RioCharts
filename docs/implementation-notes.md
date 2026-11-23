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
- **Step curve** is step-after: a value holds until the next point (states, prices and limits).
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
  between their base and top. Percent stacking: see "100 % stacking".
- **Multiple Y axes.** `ChartDefinition.YAxes` adds axes (right side by default, outward when there are
  several). The first axis with visible series owns the grid and the zero line; the others choose
  a domain with the same number of nice steps, so their labels sit on its grid lines. An axis whose
  series are all hidden is not shown. Zeros of different axes are not aligned.
- **Log axes.** `ScaleType.Log` on a y axis. `ContinuousScale.Log` maps log10 of the value (the same
  class as linear and time axes, flagged `IsLog`, so `ChartLayoutResult.YScale` keeps its type);
  zero and below map to the bottom of the range, so bars and areas start there. The domain is the
  data rounded out to powers of ten (a positive `Min`/`Max` is kept; zero or below is ignored).
  Ticks are 1, 2 and 5 per decade for short ranges, else every (k-th) power of ten, one per 28 px
  (linear axes: 42 px); labels are formatted one by one (`NumberFormatter.ForLogTicks`: 0.01, 1k,
  5M). Series values of zero or below become gaps on copies of the data, and annotation lines and
  points there are not drawn, each with a warning. A linear second axis does not align to a log
  grid. Draggable lines drag in log10 units (`DragHandle.Log`; .NET takes 10^value). Not for x
  axes (a warning; the axis stays linear), horizontal bars, radar or polar charts.
- **Data labels** are off by default (`DataLabels` per series). Bars: above the bar (below when
  negative), or inside it in the inverse color when there is no room above; stacked segments only
  inside, when the label fits. Points: above, else below. A label overlapping an earlier label or
  an annotation label is dropped. More than 1000 points: no labels, with a warning. The y axis
  makes room for the labels of unstacked bars: when the highest or lowest bar's label would not fit
  between the bar's end and the plot edge, the domain gets one more tick step on that side (same
  step, so ticks stay round; up to three rounds, as a wider domain has fewer pixels per unit). An
  axis `Min` or `Max` set in the definition is kept. Horizontal bars put a label that does not fit
  outside inside the bar instead.
- **Annotations** are `Annotation` records (`<ReferenceLine>` and `<ReferenceBand>`
  components, or `RioChart.Annotations`). Values are `object`: numbers, `DateTime`,
  `DateTimeOffset`, `DateOnly`, category names, or strings (ISO 8601 times, numbers) so JSON
  definitions work. Labels stay inside the plot. Hover text is an SVG `<title>`; they become focusable
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

- **Zoom state** is view state, not part of `ChartDefinition`: `LayoutOptions.XRange` (data units,
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
  is ignored. With `ChartDefinition.TimeZone`, the layout converts time data to instants
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

- **One model.** JSON definitions are `ChartDefinition` with data bindings added (`ChartDefinition.Dataset`, `Data`;
  `SeriesOptions.Dataset`, `X`, `Y` and the transforms). `ChartDefinitionBinder` turns them into the
  ordinary model (concrete series, generated ids, `groupBy` expanded) plus `SeriesData`, which
  then goes through the same layout as the components. The binder lives in Core so the Blazor
  `<RioChart Definition>` and the AI tool share it.
- **Tolerant reading** happens on the JSON before deserializing: letter case, comments and
  trailing commas, a single series object or top-level x/y, and synonyms (column, stackedBar,
  doughnut/donut/ring → pie with innerRadius 0.6, spline/smooth → monotone, gauge → radialBar,
  avg/mean, daily/monthly, asc/desc, >=, ...). Unknown fields are ignored. Unknown enum values
  are errors listing the valid ones. Limits: 20 series (after groupBy), 1000 inline rows, 50
  annotations.
- **Errors** are `DefinitionError(path, message)`. Field errors list the dataset's fields with their
  types and suggest a close name ("did you mean 'airTemp'?"). Parsing errors come first; binding
  errors appear once the definition parses, so a model fixes problems in two rounds at most.
- **Defaults the binder fills in** (reported as warnings): x = the first time, else text field;
  y = the only numeric field; aggregate average with a timeBucket; sum when a text x repeats.
- **"auto"**: time or numeric x → line (bar with a time bucket); text x → bar, or a donut when
  there is one series of 2–6 positive values adding up to 100 (or 1). Deliberately conservative:
  a donut only when the data is clearly shares of a whole.
- **Datasets** are columns of double, DateTime/DateTimeOffset or string, with the type inferred
  from the values (mixed columns become text; ISO 8601 strings become times; quoted numbers stay
  text). `ChartDataset.FromItems` uses reflection (camelCase property names): fine for setup,
  not for hot paths.
- **create_chart** takes the definition as a JSON object (or a string holding one) and returns success,
  errors, warnings, a summary (the chart's text description) and the normalized spec. The PNG,
  SVG and bound definition are for the app (`ChartCreated`) and are not sent to the model; over MCP
  the PNG is returned as an image block. The tool description lists the datasets with their
  fields and descriptions, the definition shape and three examples; the full schema is a separate tool
  (`get_chart_schema`) to keep the description short.
- **render_chart** takes the same definition plus `format` (png or svg), `width`, `height` (100 to
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
  render_chart, list_datasets and get_chart_schema, plus get_razor and get_usage for coding agents
  (see "Guide for coding agents"); with no app to show a chart, create_chart makes no sense. render_chart there uses `ChartTool.StandaloneRenderDescription`, which carries
  the definition format and points to list_datasets instead of listing datasets, since files may change
  while the server runs. Logging goes to stderr; stdout is the protocol. Images always go to
  `--out` (default `./charts` under the agent's working directory). There is no `--data` default:
  scanning the working directory would list unrelated JSON files.
- **CSV and folder data:** `ChartDataset.FromCsv` detects the delimiter (comma, semicolon, tab)
  from the header row, reads quoted cells per RFC 4180, numbers with a decimal point, and a
  decimal comma only with a semicolon or tab delimiter (Excel's export in many locales); empty
  headers become `column<n>` and duplicates get `_2`. `FolderDataSource` reads `.csv`, `.tsv` and
  `.json` (an array of objects, or an object with one array) in one folder, caches by write time
  and size, skips files over 100 MB and lists unreadable files with the reason and no fields.
- **Definition → Razor** writes component markup with field names as PascalCase properties; transforms
  that components do not do become comments to implement in the query. An `aggregate` without a
  `timeBucket` becomes the series' `Aggregate` parameter.
- **Not yet:** a time zone-aware `timeBucket` (buckets are on wall-clock time), declarative joins,
  and streaming definitions.

## Aggregating series components

- Design.md lists aggregate only as a declarative definition transform. The series components also
  have it, modelled on Blazor-ApexCharts' `YAggregate`: `Aggregate` (the enum the definitions use) and
  `YAggregate` (`Func<IReadOnlyList<TItem>, double?>`, which wins when both are set).
- Items group by the x value as `XValue` returns it: strings ordinally, times exactly (no
  bucketing; two timestamps a millisecond apart are two points), numbers by value (1 and 1.0 are
  the same x). Items with a null x are left out. Categories keep first-appearance order.
- Grouping runs in `SeriesData.FromGroups`, once per data change (new Items reference,
  `DataVersion`, or a change of `Aggregate`, `Bucket` or of whether `YAggregate` is set). Changing the
  `YAggregate` function alone does not reprocess, the same as `YValue`. `Data` (columnar) and
  `Buffer` (live) are not aggregated.
- **Time buckets on components:** Design.md has time buckets only as a definition transform (and for
  candles). Line, area and bar series also take `Bucket`; the grouping lives in `SeriesBase`
  (`XBucket`, `SeriesData.FromTimeBuckets`), and `CandlestickSeries` uses the same path instead of
  wrapping `XValue`. A bucket without `Aggregate` or `YAggregate` averages, as JSON definitions do. Other
  series types (scatter, heatmap, range area, box plot, pies) don't take it yet.
- **`TimeBucket.Auto`:** picked once per data change from the span of the x values (min to max),
  never from the chart's width or zoom, so it stays in the data step and selectors still run once.
  The pick is the calendar bucket whose approximate count (span ÷ its length; a month is 30.44
  days) is closest to 50 on a log scale; an empty or zero span gives Day. In JSON definitions it is
  resolved over all of a series' filtered rows before `groupBy`, so every group gets the same
  bucket, and a warning says which one. `TimeBuckets.Floor` throws for `Auto`, since one value
  can't pick it.
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
  data and definition work, and Skia export. The Blazor side (SVG render tree, interop payloads) and the
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
  and in the definition, so the JSON/AI definition and Skia export get it too. The height is not changed
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
- **Pies, radial bars, radar charts and treemaps** have a compact layout too (`FitChrome(spec,
  options, ...)`, `CompactPlot`): no title, legend, slice or bar labels, donut center total, radar
  tick and spoke labels, treemap group headers or path bar (a compact treemap always shows the
  whole tree), and no "No data" text. The plot is the box inset by 1 px; a pie or radial bar fills
  its shorter side. Their description is the normal one (no `DescribeCompact`); the tooltip stays.



## Languages and texts

- **`ChartTexts`** (Core) holds every text the chart writes itself: empty states, "Total", "Other",
  "Reset zoom", the download button, and the screen reader texts (description, axis and legend
  names, series labels, keyboard help, the "3 of 12" position the JS module appends). Definition
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
  analyzers on). The only warnings were reflection-based System.Text.Json in `ChartDefinitionJson` and
  the AI tool's result JSON.
- **Definition JSON** uses `ChartDefinitionJsonContext` (source-generated, Web defaults, plus primitive types
  for the `object?` values of definitions built in code). camelCase enum values come from generic
  `JsonStringEnumConverter<T>` instances, one per definition enum, added to the options.
- **Init-only properties:** the generator treats them as constructor arguments. It has no
  `CreateObject` for the definition records, cannot set them afterwards ("Setting init-only properties
  is not supported in source generation mode") and writes `default(T)` for a property missing
  from the JSON, so an annotation's axis became x and `"tooltip": {}` turned the tooltip off.
  `Parse` therefore fills every missing property from a serialized default instance before
  deserializing, walking nested objects and lists through the generated metadata. The default
  instances come from `Factories` (one entry per definition record). A test checks that every object type
  reachable from `ChartDefinition` has one. Changing the records to `set` would have avoided this but
  made the definition mutable.
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
- **Pies, radial bars, radar charts and treemaps** shed the legend and title by the same rules,
  and below the size above are laid out compact like the other charts.
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
- **Not yet:** a log x axis (log y axes work, see Log axes).

## Bubbles

- **A scatter series with sizes**, not a series type of its own: `SeriesData.Sizes` (a column like
  `Low` and `Totals`, set with `WithSizes`) switches `ScatterSeriesType` to bubbles. JSON
  `"bubble"` is an alias for scatter; the `size` field makes the bubbles. Everything scatter has
  (2D tooltip, trend line, colors, zoom, keyboard, export) keeps working.
- **Scale:** area ∝ size (radius ∝ √size), over the largest positive size of all bubble series in
  the chart (`BubbleScale`), so series compare. The largest radius is `MarkerSize / 2`, else 6 %
  of the chart's smaller side, clamped to 4–28 px; the smallest drawn radius is 1.5 px. Sizes of
  zero or less, or NaN, are not drawn (no negative bubbles).
- **Drawing:** largest first within a series (small bubbles stay on top and hoverable), fill
  opacity 0.5 with a 1 px outline in the series color. Never thinned: unlike markers, every bubble
  carries its own value. Across series, later series draw on top as usual.
- **Room at the edges:** before nice ticks, the x range and each y axis grow so that each bubble's
  radius fits, converting pixels to data units with an estimated plot of 85 % of the chart width
  and 70 % of its height. Axis `Min`/`Max` and zoom keep their values. The plot clip grows by the
  largest radius.
- **Tooltip:** the size as a part row labelled `SizeName` (else `ChartTexts.Size`), formatted with
  `SizeFormat`. `TooltipSeries.Radii` goes to the JS module (`rs`): a bubble is hit when the
  pointer is within its radius + 6 px, the best by distance relative to radius (a small bubble on
  a large one wins), and highlighted with a ring.
- **Aggregation:** points combined per x get the sum of their sizes (Blazor and JSON alike).
- **Size legend** (`ChartLayout.SizeLegend.cs`): nested circles sharing their bottom, at their
  real size on the first bubble series' scale, for the largest 1/2/5 × 10ⁿ at or below the
  largest size and a quarter and a sixteenth of it (half and a quarter of its width). A circle
  under 3 px, or whose value would overlap the one above, is left out. Values sit at each circle's
  top with a dashed leader; the size name is the title. It takes a column right of the plot (the
  right legend's, below its items), so the plot narrows; the bubble x padding uses the narrower
  width. Auto (`LegendOptions.Sizes` null): when the chrome has room for a legend and the chart is
  at least 360 px wide; never in compact charts. Not interactive. If it does not fit the plot's
  height it is left out, with its column still reserved.


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
  lowest low per point. In definitions, average and sum become min (low) and max (high), since averaging a
  band's ends would hide its extremes; other aggregates apply to both ends.
- **Not yet:** an outline option, live data with lows, and error bars.


## Box plot

Design.md "Box plot" (milestone 7).

- **Data:** `SeriesData.Box` (`BoxColumns`): per point the whisker ends, quartiles, extremes and
  count, plus the outliers as one flat list with point indexes. `Y` is the median, so everything
  that reads y (hit test, describer, selection, keyboard) works unchanged. `WithBox` takes boxes in
  input order and places them by `SourceIndex`; `WithX` carries them.
- **Statistics** (`BoxSummary.FromValues`): quartiles by linear interpolation (type 7, as Excel's
  QUARTILE.INC and NumPy), whiskers at the furthest values within 1.5 × IQR (Tukey), outliers
  beyond. `FromFiveNumbers` takes precomputed boxes and sorts the five numbers.
- **Two ways in:** raw values (`BoxPlotSeries` with `XValue`/`YValue`, one item per value; items
  with the same x form a box, so clicking a box selects all its items) or precomputed boxes
  (`Q1Value`, `Q3Value`, `MedianValue`, `LowValue`, `HighValue`, optional `OutliersValue` and
  `CountValue`, one item per box). JSON definitions: `"type": "boxPlot"` (aliases box, boxAndWhisker,
  whisker); with `q1` and `q3` each row is a box, otherwise boxes come from y per x. Raw values
  never trigger the "x repeats, using sum" aggregate.
- **Drawing:** a band slot per series like grouped bars (never stacked, also with `Stack`), boxes
  at most 56 px wide, fill 0.22 with a 1 px outline, a 1.75 px median line, 1 px whiskers
  without caps, outliers as hollow dots (r 2.5) in the surface color. The y domain covers the
  extremes, outliers included, and does not force zero.
- **Tooltip:** the median as the row's value, then Max, Upper quartile, Median, Lower quartile,
  Min, Count and Outliers as smaller rows (from `ChartTexts`, all languages); empty rows (no
  count, no outliers) are left out, more than five outliers are shortened. The keyboard
  announcement reads the same. Outliers are listed in the tooltip rather than hovered one by one
  (Design.md: "hovering an outlier shows its value"): simpler, and it works from the keyboard.
- **Selection:** a selected box is outlined from quartile to quartile.
- **Not yet:** `Horizontal` box plots, hovering a single outlier (with its item), notches, and a
  box plot description for screen readers beyond the medians.

## Candlestick

Design.md "Candlestick" (milestone 7).

- **Data:** `SeriesData.Candles` (`CandleColumns`): open, high, low and count per point; `Y` is the
  close. `WithCandles` (input order, placed by `SourceIndex`; an incomplete candle is a gap) and
  `WithX` carry them. `CandleSummary.FromValues` takes values in time order (first, highest,
  lowest, last); `Merge` joins candles.
- **Raw readings:** `FromCandleBuckets` (and `CandlestickSeries` with `Bucket`, JSON with
  `timeBucket`, a day when not set) groups per `TimeBucket` on wall-clock time, sorted by time
  inside each bucket. The bucket floor moved from the definition binder to the public
  `TimeBuckets.Floor`, which now keeps the DateTime's `Kind` and returns a DateTime for a DateTime
  (the binder's version widened it to a DateTimeOffset with the machine's local offset). The component wraps `XValue` to
  floor it and keeps the caller's selector; a `DataSettings` hook in `SeriesBase` rebuilds the
  data when `Bucket` changes (before, only new `Items`, aggregates or `DataVersion` did).
- **Drawing:** a band slot per series like bars (never stacked), bodies at most 32 px. Rising
  (close ≥ open) is a light fill (0.22) with a 1 px outline (wicks 1 px too), falling is solid, so the
  direction does not depend on color. Both are in the series color, like the legend and the tooltip
  dot, so several candle series can be told apart. `DirectionColors` (`"directionColors"`) colors
  rising candles `--rio-positive` and falling ones `--rio-negative` instead, as for prices; the
  tooltip dot then takes each candle's color. (Green and red were the default at first: they did
  not match the series' legend and tooltip colors, two candle series looked the same, and red and
  green read as bad and good, which is wrong for e.g. temperatures.) The wick is drawn above and
  below the body, not through it. (Hollow bodies were tried first: when narrow they read as
  double lines.) New theme tokens with light and dark defaults in
  the CSS and `ExportTheme`. The legend swatch is a small falling and rising body, in the colors drawn.
- **Zoomed out:** when a slot is under 5 px, candles merge into longer periods. On time axes the
  shortest period from a ladder (2, 5, 10, 15, 30 minutes; 1, 2, 3, 6, 12 hours; a day; a week from
  Monday; a month, quarter, year) whose width gives each series at least 12 px, aligned to the
  clock and calendar; on numeric axes groups of k spacings from zero. Merged candles are centered
  on their period (a day's at noon) and share 80% of its width. (Groups of k consecutive candles
  were tried first: not aligned to anything, a daily rhythm turned into alternating noise.) The
  merged candle keeps the first open, highest high, lowest low and last close; its tooltip says
  from when to when. Not for category axes. A selected candle is outlined from high to low, but
  not while merged.
- **Tooltip:** the close as the row's value, then Open, High, Low, Close and Change as smaller rows
  (`ChartTexts`, all languages). The change is in the value's format ("+0.3"); with `DirectionColors`
  also in percent ("+0.3 (+1.4%)"). A percentage only means something for values measured from a
  true zero (prices), not for e.g. °C, so it comes with the price-style option.
- **Not yet:** live data (`SeriesBuffer`) with candles, a hollow/filled option, a volume pane
  helper (use a bar series on a second axis), and `Horizontal` candles.

## Histogram

Not in Design.md; added from `docs/chart-types.md` (candidate, high popularity).

- **Bins:** `Bins` (Core, Data) is a start, a width and a count; a bin holds values from its lower
  edge up to its upper edge, the last bin also its upper edge (so 0–100 in bins of 10 is ten bins,
  not eleven). `Bins.For` takes a width as given, or rounds a wanted count's width to 1, 2 or 5
  times a power of ten (`NiceTicks.Step`); neither: Freedman–Diaconis (Sturges when the quartiles
  are equal), at least 5 (or Sturges) and at most 100 bins. Edges are multiples of the width, so
  they meet the axis ticks. All-equal values give one bin; at most 1000 bins (a narrower width is
  widened). A value on an edge lands in the bin it starts (a 1e-9 tolerance for 0.3 / 0.1).
- **Data:** `SeriesData.FromBins` makes one point per bin, empty bins included: x the bin's
  middle, y the count, and each bin's items as groups (clicks give them). `HistogramSeries` bins
  its `XValue` through a new `SeriesBase.BuildData` hook (data built the type's own way), and puts
  the width it used into `SeriesOptions.BinWidth`, which the layout reads. Without `BinWidth`
  (data given directly) the layout takes the smallest distance between bins.
- **Drawing:** bars from edge to edge with a 1 px hairline between bins (no bar gap: x is
  continuous). The x extent uses the bin width as the bar spacing, so it runs from the first lower
  to the last upper edge, a single bin included. Several histograms overlay at 0.55 opacity
  instead of sharing the slot; they should share a `BinWidth` (JSON `groupBy` does this itself).
  Never stacked (a percent axis warns). Empty bins draw nothing but take hovers.
- **Tooltip:** the bin's range ("125 – 150", in the x format) and its count. The describer says how
  many values and the fullest bin ("Response time: 20 values, most from 125 to 150",
  `ChartTexts.HistogramPeak`, all languages), in the legend and the chart description.
- **JSON definitions:** `"type": "histogram"` (also `hist`, `distribution`), x a numeric field, no y
  (a y without an x is read as x, with a warning). `binWidth`, `binCount`.
- **Not yet:** density or percent instead of counts, cumulative histograms, uneven bins, a
  `Horizontal` histogram and time x values (bins of hours or days: use `BarSeries` with `Bucket`).

## Range bar / timeline

Not in Design.md; added from `docs/chart-types.md` (candidate, medium–high popularity).

- **Data:** a point is a bar: `X` its start, `Y` its end (same units: numbers, or times in ms),
  and the new `SeriesData.Rows` its row. `SeriesData.FromRanges(items, row, start, end)` builds it
  (sorted by start, `SourceIndex` back to the items; text or a start/end of the other kind counts as
  missing); `WithRows` adds rows to columnar data. Without rows (e.g. `SeriesData.From(starts,
  ends)`) every bar is in a row named after its series. `RangeBarSeries` builds its data through
  the `BuildData` hook from `RowValue`, `StartValue` and `EndValue`; `ColorValue` and
  `TooltipValue` work as for other series.
- **Time zones:** with a display zone the ends are converted too (`TimeConversion.Convert`), each
  with its own offset (`EndOffsets`, internal), so a bar across a DST change keeps its length.
- **Layout:** its own pass (`ChartLayout.ComputeRangeBars`), like the heatmap and horizontal bars;
  other series types are skipped with a warning. Rows top to bottom in order of first appearance in
  the input (also hidden series, so rows do not move when a series is toggled). Series share the
  rows and overlap where their bars do (a state timeline has one series per state, so they do not);
  no slots per series. Bars are at most 40 px tall, with a 2 px corner radius. The time axis is the
  value axis, so its grid is vertical (`X.GridLines`, default on), unlike Design.md's horizontal-only
  default; there is no y grid. Numeric axes get a nice domain; time axes run from the first start to
  the last end. `X.Min`/`X.Max` and the zoom window (`XRange`, `Zoomable`) clip bars at the plot
  edges. Bars that end before they start are skipped with one warning.
- **Annotations:** x lines and bands only (e.g. "now", a holiday); others are skipped with a warning.
  A line outside the data does not widen the axis unless `ExpandsDomain` is true.
- **Tooltip:** the row as the header, then "start – end (duration)": times only when every bar is
  on one day, otherwise the date, with the end's date left out when it is on the start's day.
  Durations read "2 h 30 min" or "3 d 4 h", with units from `ChartTexts.DurationUnits` (all
  languages); numeric axes give the difference in the series format. Hover outlines the bar, as
  for horizontal bars (no JS changes). `DataLabels` writes the duration inside bars where it fits.
- **Description:** per series "Running: 5 bars in 3 rows, from 06:00 to 13:00"
  (`ChartTexts.RangeBarSummary`, all languages), in the legend and the chart description.
- **JSON definitions:** `"type": "rangeBar"` (also `timeline`, `gantt`, `schedule`), with new
  `row`, `start` and `end` fields (`SeriesOptions.Row`, `Start`, `End`). Without start and end, a
  dataset with exactly two time fields uses them (warning); `x` is read as the row when `row` is not
  set. `groupBy`, `filter` and `colorField` apply; aggregates and time buckets are ignored (warning).
- **Not yet:** dependencies, milestones, progress, drag to move or resize, nested rows (groups of
  tasks), per-item labels inside bars, side-by-side bars per series (planned against actual) and
  sorting rows.

## Waterfall

Not in Design.md; added from `docs/chart-types.md` (candidate, medium–high popularity, low complexity).

- **Data:** each point is a change (`Y`). `SeriesData.Totals` (a flag per point, set with
  `WithTotals`, input order like the other columns, carried by `WithColors`, `WithLow` and `WithX`)
  marks totals; `WithTotals` sets a total's `Y` to the running total before it, so the describer,
  CSV export and clicks see the total, not a placeholder. `WaterfallSeries` takes `IsTotal`
  (aggregated points: their first item's flag); JSON definitions take `"total"`, a field whose
  rows are totals when it reads true, yes or 1 (booleans arrive as "true"/"false" text).
- **Drawing:** the layout sets `Upper` to the running total after each point and `Lower` to the one
  before it (zero for a total), so the y extent, selection outline and log-axis gaps work as for
  stacked bars. A band slot per series like bars, never stacked (a percent axis warns, as for
  candlesticks). Rises are `--rio-positive`, falls `--rio-negative`, totals the series color;
  `ColorValue` wins. No color option: a waterfall's bars mean up and down, so green and red read
  right here (unlike candlesticks of e.g. temperatures). Corners round 2 px at most. Connectors are
  1 px lines in the axis color from each bar's end to the next bar's left edge, over gaps too.
- **Tooltip:** the change with its sign ("+15", "−180") and the running total after it as a Total
  row (`ChartTexts.Total`); a total bar shows its value alone. The dot takes the bar's color.
  Hit-tested over the slot's whole height, like box plots (a floating bar has no top-down area).
- **Data labels:** the change above a rise and below a fall, a total's value at its end. Labels
  show the plain value (no "+"), as for bars.
- **Legend:** a small rising and falling bar in the positive and negative colors. The describer
  gives the running total's range and its end ("Result: 0 to 420, last 105").
- **Not yet:** `Horizontal` waterfalls, an automatic end total (add an item with `IsTotal`),
  room on the y axis for labels at its ends (bars have it), and a "+" on change labels.

## Radar

Design.md "Radar" (milestone 7).

- **Own layout path** (`ComputeRadar`, `ChartLayout.Radar.cs`), not the pie/radial bar pass: the
  legend lists series (pies list categories), hiding works per series, and the tooltip is shared
  per spoke. A chart with any radar series is a radar chart; other series are skipped with a
  warning. `SeriesType.Radar` reports the polar coordinate system.
- **Spokes:** the categories of all series in order of first appearance (numbers and times are
  formatted as labels), clockwise from 12 o'clock. A category that repeats in a series keeps its
  first value, with a warning.
- **Scale:** linear from zero (from the smallest value when it is negative), nice ticks (3 to 6
  rings depending on the radius); `ChartDefinition.Y.Min`/`Max` fix it. Rings are polygons, or circles
  with fewer than three spokes. Tick values sit just right of the first spoke, in the labels layer
  above the shapes on a backing in the surface color (opacity 0.8).
- **Drawing:** a closed path per series, line 2 px (`StrokeWidth`) with fill opacity 0.14. With a
  missing value the series is open lines that wrap around the circle; a value between two gaps
  (or a single spoke) is a dot. Markers stay hidden until hover.
- **Labels:** the radius leaves room for the spoke labels (capped at 22% of the width, shortened
  with "…"), at least 30% of the plot's smaller side. Side labels are anchored by side; top and
  bottom labels are centered and limited to the chord between neighbouring spokes. A label that
  would overlap one already placed is left out (the tooltip names the spoke).
- **Interaction:** the tooltip series carry each point's spoke (`TooltipSeries.Spokes`) and the
  model the geometry (`TooltipModel.Radar`); the JS module picks the spoke nearest the pointer by
  angle (within the radius + 24 px), draws it as the crosshair, and reuses the shared tooltip rows,
  keyboard navigation (Left/Right along the series, Up/Down between series, the whole spoke
  announced) and point clicks. `ShowTooltipAsync` takes a category: the component sends its spoke's index and the
  JS module shows that spoke.
- **Not yet:** data labels, a circular grid option, a sqrt scale (polar area), and per-spoke scales.

## Treemap

Design.md "Treemap" (milestone 7).

- **Own layout path** (`ComputeTreemap`, `ChartLayout.Treemap.cs`), coordinate system
  `SpaceFilling`. One treemap series is drawn; other series are skipped with a warning.
- **Hierarchy from paths:** the series stays plain category data whose names are paths
  (`"Asia/India"`, split on `SeriesOptions.PathSeparator`, default "/"), so `SeriesData` needed no new
  column. Design.md's "node list with parent indices" is built per layout as a small tree.
  `TreemapSeries.ParentValue` turns a parent-child list into paths in the component (cycles and
  unknown parents end the path). JSON: `SeriesOptions.Path` (the groups' fields) is joined into an x
  path by the binder. Repeated paths are added; a group's own value is ignored (warnings).
- **Layout:** squarified (Bruls, Huizing, van Wijk), children largest first. A group gets a header
  (name left, total right when both fit) when its rectangle is at least 2.4 headers tall and 48 px
  wide, otherwise a 2 px frame; items are inset 1 px, so neighbours are 2 px apart. Groups are
  tinted (fill opacity 0.16, nested groups add up); items are solid in their top-level group's
  palette color with inverse text (theme token, not computed contrast). Name and value inside when
  they fit; a shortened name keeps at least three characters, else it is hidden.
- **Other:** `GroupSmallSlices` (component: `GroupSmallItems`) per group: two or more items under
  the share become one lighter "Other" item.
- **Legend:** the top-level groups (`HiddenSeries` takes group names). Shown by default only with
  nested groups and 2 to 12 of them; a flat list colors each item and needs no legend. The legend
  tooltip lists the group's first children.
- **Interaction:** one tooltip series with each rectangle's center, `CellWidths`, `CellHeights` and
  `PointColors`: items first, then groups (found in their headers and frames). Rows: the path,
  the value, then share of group and of total as part rows. Groups have index -1 (not clickable).
  The JS module finds the rectangle under the pointer (as for heatmap cells), outlines it and puts
  the tooltip beside the pointer. Selected items get an outline.
- **Highlight:** each top-level group's rectangles and labels are their own scene groups keyed
  `series:{group}` and `labels:{group}` (the legend item's id), so legend hover and
  `HighlightSeriesAsync("Asia")` fade the other groups. The treemap series id itself matches no
  group.
- **Drill-down:** `LayoutOptions.TreemapPath` picks the group to lay out (found by name, path split
  on the separator; hidden, missing or item paths show the whole tree). The group fills the plot
  without a header; its children are recolored from the palette and the legend is hidden (its
  colors no longer apply); a path bar above the plot names the steps. With
  `InteractiveTreemap` (the component passes `Live`), each path step but the last is a scene
  button, and so are the groups: with `TreemapDrillOnItems` (no `OnPointClick`, no point
  selection) each top group as a whole, items included, so a group too small for a header still
  opens; otherwise each group's tint rectangle (header and frame), so items keep their clicks.
  Buttons (interaction kind "treemap", id = the path, "" for all),
  so clicks and Enter/Space work without the JS module; the labels layer ignores the pointer so
  headers do not block them. Shares in the tooltip stay shares of the whole tree. The component
  keeps the path: `TreemapPath` is a two-way bindable parameter (like `VisibleRange`),
  `DrillDownAsync` and `GetTreemapPath()` work from code, and `OnDrillDown` follows every change.
- **Visible levels:** `SeriesOptions.VisibleLevels` limits how many levels are laid out below the
  group shown; a group at the last level is collapsed into a cell (its total, index -1, class
  `rio-treemap-collapsed`) that drills into itself, or with its top group when items drill. Only
  what is drawn changes: the tree, totals and shares are those of the whole data. Named after
  `VisibleRange` (Design.md does not name it); "MaxDepth" read as if it cut the data.
- **Not yet:** color by a separate value on a sequential scale, and listing an "Other" item's parts
  in its tooltip.

## Target frameworks

Design.md: "current LTS plus the latest .NET". The libraries (Core, Blazor, Skia, AI) target
`net10.0;net11.0` through `LibraryTargetFrameworks` in Directory.Build.props; RioCharts.Blazor
references Microsoft.AspNetCore.Components.Web 10.0.x or 11.0.x per framework. The test projects
and the AOT smoke test target both too, and CI installs both SDKs. riocharts-mcp is a tool, not
a library: one build for .NET 10 with `RollForward=Major`, so it runs where only .NET 11 is
installed. The apps pick one each, so both library builds run in a real browser: the docs site
(Blazor WebAssembly, on GitHub Pages) is on .NET 11, the demo (Blazor Server) and the
benchmarks on .NET 10.

What .NET 11 changed:
- `DateTime.TryParse` reads "2026-03-01T24:00" as the next midnight (ISO 8601 end of day); .NET 10
  rejects it. CSV/JSON time detection fell back to TryParse for what its own ISO reader
  rejected, so the same file read differently. The fallback is gone: the ISO pattern without an
  offset is read in full by the fast path, and what it rejects stays text on both.
- Analyzer BL0016 (JS interop not in a try/catch). The download methods now catch
  `JSDisconnectedException` (no browser left to save to); `SyncInteractionAsync` runs inside
  OnAfterRenderAsync's try/catch and `ExportPngAsync`/`ExportSvgAsync` pass errors to the caller
  on purpose, so those suppress it with the reason. The bUnit tests turn it off (fakes).

## Guide for coding agents

Design.md has no section on this; it extends "Low priority: a short README note for
Blazor-ApexCharts users" and the AI definition work toward agents that write app code.

- **llms-full.txt** (docs site) is generated, not written: `AgentGuideTests` in the Blazor tests
  builds it from `docs/llms/guide.md` (hand-written: setup, rules, which series, common mistakes)
  with two placeholders. `{{examples}}` takes the components in `tests/RioCharts.Blazor.Tests/Guide/`
  verbatim (first line `@* Title | Description *@`); the same tests compile and render them, and
  that folder's `_Imports.razor` has only the two usings the guide tells apps to add, so the
  setup is checked too. `{{api}}` is a reference from reflection and the XML docs: every public
  component's `[Parameter]`s (type with nullability, default from a new instance, summary),
  `RioChart`'s public methods, the parameters common to series and annotations once, and every
  RioCharts type the parameters use, followed transitively (records with properties, enums with
  values; a type with more than 60 properties, i.e. `ChartTexts`, lists names only). The test fails
  when the file is out of date; it is accepted like the snapshots.
- **llms.txt** is the short index from the llms.txt convention, written by hand (the page list
  changes rarely).
- **RioChartsUsage** (RioCharts.AI) embeds llms-full.txt, so get_usage matches the package
  version. `Get(topic)` cuts one section by heading (a component, type or `##` heading, matched
  case-insensitively without markup, then by substring); no topic gives everything up to the
  reference plus the list of reference topics, about a third of the size.
- **get_razor** (`ChartTool.GetRazorAsync`) checks the definition like render_chart, then returns
  `ChartRazor.FromDefinition` markup, a positional record for the rows from the dataset's fields
  (`ChartRazor.ItemRecord`: double, DateTime or DateTimeOffset, string; nullable when a field has
  missing values; property names as FromDefinition writes them) and, for inline data, the rows as a C#
  list (`ItemsList`, at most 100 rows). Type and variable names default to the dataset name
  ("web_orders" → `WebOrder`, `webOrders`). The record has every field of the dataset, not only
  the ones the definition uses, so the app's own type is easy to match. Only riocharts-mcp offers it and
  get_usage; `WithRioChartsTools` (servers in apps, for end users) keeps its four tools.
- Found while writing the examples: an event handler lambda on `<RioChart>` (`OnPointClick="e =>
  ..."`) stops Blazor from inferring `TItem`, even with `Items` set; the guide says to set `TItem`.

## Colors per item

Not in Design.md; added on request ("a way to set a colour on a value").

- **Data:** `SeriesData.Colors` (one string per point, null keeps the series color; the column is
  dropped when no point has a color). `WithColors` takes input order like `WithLow`; every `With*`
  and `WithX` carry it. Components: `ColorValue` on `SeriesBase`, applied after the type's own
  data steps (treemap paths, candle buckets), a group's first item for aggregated points; a new
  selector lambda each render does not rebuild, adding or removing one does. JSON: `colorField`
  (a text field), first row per aggregated point.
- **Drawing:** bars and horizontal bars get a style per color; scatter one marker path per color;
  lines and areas keep their color and get a dot (ringed in the surface color) per colored point;
  pies and radial bars use it as the category's color (the first one given in any ring), so the
  legend swatch, slice and tooltip agree; treemap items use it, groups keep their group color.
  Tooltip rows show the point's color (`TooltipSeries.PointColors`).
- **Bands:** `SeriesOptions.ColorBands` (`ColorBand` with `Above`, `Below`, `Color`; a JSON definition
  record, so it has a `Factories` entry). Lines and areas are drawn once per value range of one
  color (`ColorBands.Ranges`), each copy in a group clipped to that range's pixels, so the color
  changes exactly at the edge, between points too; without bands one path as before (snapshots
  unchanged). The canvas layer gets each shape's enclosing clip (`CanvasOp.Cl`). Bars and scatter
  use `PointColor` (own color, else band, else series). The component keeps an equal list of
  bands as the same instance, so an inline list does not change the definition each render.
- **Not yet:** heatmap, box plot, candlestick and radar (they have their own coloring), and
  rule-based colors from a field in JSON beyond bands.

## Horizontal bars

- **A chart option, not a series type** (`ChartDefinition.Horizontal`): orientation is a property of the
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
  | Definition binding, 200k rows into day buckets per store | 30–70 ms | 20 MB |
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

- Reviewed before the first release. Public: the definition model, data types, scales, formatters, the
  scene types (renderers in other packages and custom renderers draw them), `ChartLayout`, and
  each package's entry points. Internal: SVG/canvas writers and the scene renderer component in
  Blazor, Skia's color and path helpers, `ChartDefinitionJson.Options` (deserializing with it skips the
  defaults fix-up that `Parse` does) and the components' options.
- **JS callbacks:** the module calls a private `JsCallbacks` object (`DotNetObjectReference`), so
  the `[JSInvokable]` entry points are not methods on `RioChart`. The method names are unchanged.
- **`SvgSceneRenderer`** is internal, and Razor only resolves public components as tags, so
  `RioChart` renders it from code (`SceneFragment`).
- **`ChartTool` names stay constants** (`ToolName`, `RenderToolName`, `RenderDescription`): the MCP
  attributes need compile-time values.
- **Missing XML docs** (CS1591) warn in the libraries; test and sample projects turn it off.

## 100 % stacking

Design.md has named stack groups but no percent stacking; `docs/chart-types.md` listed it as a
candidate.

- **On the axis, not the series.** `AxisOptions.Stacking = StackMode.Percent` (`"y": {"stacking":
  "percent"}`). All stacks on an axis add up the same way, and the axis labels change with it, so
  the setting lives there; a series-level setting could disagree within a stack. JSON definitions also
  take `"stacking"` on a series (moved to its axis, with a warning), at the top level (moved to
  `y`), the values `100%`, `normalize`, `expand`, `relative`, and the types `stackedBar100`,
  `percentStackedBar`, `100% stacked column`, `stackedArea100`.
- **No stack name needed.** On a percent axis, line, area and bar series without `Stack` share one
  stack (stacking to 100 % is the point); named stacks still stack separately, side by side for
  bars. Scatter, range area, box plot and candlestick series on the axis keep their own values,
  with a warning (scatter with a `Stack` name stacks as before).
- **Shares.** Each stack's values are divided by the total of their absolute values per x value
  (category, or exact x), so positive shares stack up and negative ones down and together they
  span 100 %. A total of zero gives zero-height segments; gaps stay gaps. Shares are clamped to
  -1…1 and the axis extent too, so rounding never adds a 120 % tick. Hidden series leave the
  total: the others fill their share.
- **Labels.** Axis ticks (and value annotations' labels) default to `P0` in the chart's culture
  (`50 %` in Swedish, `50%` in English); the axis `Format` overrides it. The tooltip shows the
  value in the series' format and its share: `10 (25 %)`. Data labels show the values, not shares.
- Horizontal bars stack the same way on their value axis (`ChartDefinition.Y`).
- Definition → Razor writes `Stacking = StackMode.Percent` in the y axis' `AxisOptions`.

## Naming: chart definitions and options

Design.md calls the serializable chart model a "spec" (`ChartSpec`, `<RioChart Spec>`, JSON
specs), after Vega-Lite. The code calls it a **chart definition**: "spec" read like a
requirements document, and "JSON spec" named the format rather than the thing (a definition built
in C# involves no JSON). Its parts are **options** (`AxisOptions`, `SeriesOptions`,
`LegendOptions`, `TooltipOptions`), which read naturally as the settings of a part and are
familiar from ApexCharts' `ChartOptions`; annotations and filters describe things rather than
settings, so they are `Annotation` and `Filter`. The whole is not called options: unlike
ApexCharts' `ChartOptions` it also holds the series, their fields and the data, and draws a
chart on its own. The JSON format did not change; the AI tools' argument is `definition`.


## Reset zoom button

- **An icon** (arrows out to the corners), a 28 px square like the download button but always
  visible while zoomed; `ChartTexts.ResetZoom` is its `title` and `aria-label`. An icon fits the
  top row more often than the text did, whatever the language.
- **In the top row when it fits:** `ChartLayoutResult.TopRowFree` is the area at the chart's top
  right, above the plot's top y label and right of the title, a top legend and the y-axis titles
  (a right axis title takes the row's right end). The component puts the button there, at the
  right edge or left of the download button, when it fits there without reaching the plot;
  else inside the plot's top right corner as before (a chart with nothing above the plot has no
  top row, and adding one while zoomed would move the plot on every zoom). Only the cartesian
  layout reports the area; it is the only zoomable one.

## Motion

Design.md "Visual design": an entry fade only, no update animations. How it is done and why:

- **Entry:** `.rio-series` fades in once (`--rio-enter-duration`, default 0.35 s). The tooltip,
  the hover buttons and series dimmed from the legend have short opacity fades. Nothing grows,
  draws on or sweeps in (radial bars included, see Milestone 3).
- **No update animations.** Changed data, zoom, pan and streaming render the new state at once.
  A tween would show values that are not in the data, and interaction would lag. It would also
  have to run frame by frame in the JS module (a Blazor Server round trip per frame is too slow),
  for both the SVG and the canvas layers. Morphing a path whose point count changes (LTTB,
  zoom, a sliding window) has no clean answer, and CSS transitions of `d` and SVG geometry are
  not supported in every browser. Animation is not supported, by decision (Design.md), and there
  are no animation options.
- **Off switch:** `Animate="false"` puts `rio-still` on the container, and `prefers-reduced-motion`
  does the same: every animation and transition inside the chart is turned off
  (`!important`, so later rules cannot bring one back). Animation is a component parameter,
  not part of `ChartDefinition`: exports are static anyway.
