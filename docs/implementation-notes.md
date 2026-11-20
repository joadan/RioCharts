# Implementation notes

Where the code differs from, or fills in details of, [Design.md](../Design.md).

## Milestone 1

- **Chart and TItem.** `<Chart TItem>` cascades `TItem` to its series, so `XValue`/`YValue`
  lambdas are typed. `TItem` is inferred from `Chart.Items` (shared by all series) or must be
  given. A chart that only uses columnar `Data` still needs some `TItem`, e.g. `TItem="double"`.
- **Selectors.** `XValue` is `Func<TItem, object?>` (numbers, dates and strings are boxed once per
  data change); `YValue` is `Func<TItem, double?>`, so `decimal` needs a cast.
- **Time.** Inside Core, times are milliseconds since 1970-01-01 of the wall-clock time as given
  (`TimeValue`); `DateTime.Kind` and `DateTimeOffset.Offset` are ignored. Display zones come later.
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
  `var(--rio-series-1, var(--_rio-series-1))`, with the defaults defined in `Chart.razor.css`.
- **Change detection.** A series reprocesses its data only when `Items` or `Data` is a new
  reference or `DataVersion` changes. `Chart.Refresh()` forces a new layout.
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
  date, date and minutes, or date and seconds. Tooltip templates (`RenderFragment`) come later.
- **Point clicks** only count on or next to a point (16 px) or a bar, and win over an annotation
  underneath. Hover events are not raised to .NET.
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
  components, or `Chart.Annotations`). Values are `object`: numbers, `DateTime`,
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
  event. Pinch zoom on touch is not supported yet.
- **Y fits the visible data** (only points inside the range count), and line series draw the
  visible points plus one on each side, downsampled within that window.
- **Linked charts:** charts with the same `Group` find each other in the JS module. Hover shows the
  crosshair and markers at the same x in the others (no tooltip there). A zoom or pan in one is sent
  to every zoomable chart in the group, each applying it through its own .NET side.
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
- **Not yet:** Blazor's `ExportSvgAsync`/`ExportPngAsync` and client-side download, and PDF.

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
  MIME type image/svg+xml (MCP image blocks are for raster images). It does not raise
  `ChartCreated`. With `ChartToolOptions.OutputDirectory` set it also writes the file and returns
  its full path in `file`; the name comes from `fileName` or the title, with folders stripped,
  unsafe characters replaced and the extension forced to the format, so the agent cannot write
  outside the directory. Whether a model can see an SVG it gets back depends on the provider; most
  only look at raster images, so PNG is the default.
- **Spec → Razor** writes component markup with field names as PascalCase properties; transforms
  that components do not do become comments to implement in the query.
- **Not yet:** a time zone-aware `timeBucket` (buckets are on wall-clock time), declarative joins,
  and streaming specs.
