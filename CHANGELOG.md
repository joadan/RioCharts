# Changelog

All notable changes are listed here. The project is pre-1.0: APIs change freely, and breaking
changes are called out under **Breaking**.

## Unreleased

- The docs site moved to https://riocharts.com (old joadan.github.io/RioCharts links redirect).
  Links in the READMEs, llms.txt, llms-full.txt and the AGENTS.md block point there, and so does
  the packages' project URL.
- Docs site: the `#` links on example headings stay on their page (they went to the start page).
- .NET 11: the packages target .NET 10 (LTS) and .NET 11; RioCharts.Blazor builds against
  ASP.NET Core 11 on .NET 11. riocharts-mcp runs on .NET 10 or, when only that is installed, .NET 11
  (`RollForward=Major`). Tests, the native AOT check and CI run on both. The docs site runs on
  .NET 11 (WebAssembly), the demo on .NET 10. Building from source
  needs both SDKs.
- `DownloadPngAsync`, `DownloadSvgAsync` and `DownloadCsvAsync` do nothing once the browser has
  disconnected, instead of throwing `JSDisconnectedException`.
- CSV and JSON data: a time like "2026-03-01T24:00" stays text on every framework (.NET 11's
  `DateTime.TryParse` accepts it as the next midnight, so it was read as a time there).
- Guide for coding agents: `llms.txt` and `llms-full.txt` on the docs site. llms-full.txt has setup,
  rules, which series to use, common mistakes, compiled examples and every component parameter
  and the types they use, generated from the code and XML docs by the Blazor tests (they fail
  when it is out of date; accept with `RIOCHARTS_ACCEPT_SNAPSHOTS=1`).
- A block to paste into an app's `AGENTS.md` (or `CLAUDE.md`, Copilot instructions) so coding
  agents read the guide first: on Getting started, in the README and in the NuGet README. The NuGet
  README now lists every series type.
- This repository's agent instructions moved from `CLAUDE.md` to `AGENTS.md` (read by Copilot,
  Codex, Cursor and others); `CLAUDE.md` imports it (`@AGENTS.md`).
- riocharts-mcp: `get_usage` returns that guide (the overview, or one component or type with
  `topic`), and `get_razor` turns a spec into code for a Blazor app: the `<RioChart>` markup, a
  record for the rows from the dataset's fields and, for inline data, the rows as a C# list. In
  RioCharts.AI: `RioChartsUsage`, `ChartTool.GetRazorAsync` (`ChartCodeResult`) and
  `ChartRazor.ItemRecord`, `ItemsList`, `ItemTypeName`, `ItemsName`.
- Docs site: the header badge shows the RioCharts version the site runs (e.g. `v0.1.73-preview`)
  and links to this changelog; the footer adds the commit it was built from.
- Pies: the "Other" slice's tooltip lists the slices it holds, and in both that tooltip and the
  "Other" legend tooltip the total and its parts look different: the total is a normal row, the
  parts are smaller, muted and indented under a thin line. Before, the legend tooltip was lines of
  plain text that looked alike, and the slice tooltip showed only the total.
  `TooltipSlice.Parts` (Core) holds the parts. The legend tooltip text for "Other" is now
  `Other<tab>total` followed by `• part<tab>value` lines.
- Pinch zoom on touch screens: on a `Zoomable` chart, two fingers zoom the x axis around them and
  pan when moved together, with a live preview; the range is applied when a finger lifts and linked
  charts follow, as with the mouse.
- Benchmarks (`benchmarks/RioCharts.Benchmarks`, BenchmarkDotNet): layout, items to columns, LTTB,
  SVG/PNG export and JSON specs, with a baseline in `benchmarks/README.md`.
- Faster chart layout for lines: 27% at 1k points, 47% at 100k (54% when zoomed). The screen reader
  texts no longer scan each series four times, the x extent reads only the first and last values,
  and series with the same x values share their tooltip x labels.
- The Test workflow checks the JS module's syntax (the component tests fake the module).
- Linked charts (same `Group`) line up their plots: each uses the widest y-axis labels in the
  group, so the same time sits at the same place in all of them and the crosshair runs in one
  vertical line. Before, a chart with narrower labels (e.g. "2k" under "400") started its plot
  further left. Core: `LayoutOptions.MinInsets` and `ChartLayoutResult.NeededInsets`.
- Selection: `Selection="SelectionMode.Points"` selects points and bars by clicking
  (Ctrl/Cmd/Shift+click adds or removes), `SelectionMode.Range` selects an x range by dragging.
  Bind `@bind-SelectedItems` and `@bind-SelectedRange`, handle `OnSelectionChanged`, or call
  `SelectItemsAsync`, `SelectRangeAsync`, `ClearSelectionAsync`, `GetSelectedItems`,
  `GetSelectedRange` and `GetItemsInRange`. Selected points get a marker (bars an outline), a
  range a light band. Core: `LayoutOptions.SelectedRange` and `SelectedPoints`.
- `ShowTooltipAsync(x)` and `HideTooltipAsync()` show and hide the crosshair and tooltip at an x
  value from code, e.g. when hovering a table row; linked charts follow.
- Series from code: `ShowSeriesAsync`, `HideSeriesAsync`, `ToggleSeriesAsync`, `IsSeriesVisible`,
  and `HighlightSeriesAsync(id)`, which fades the other series (null: none). Hovering or focusing a
  legend item now highlights its series the same way.
- `OnLegendClicking`: raised before a legend click toggles its series; set `Cancel` to do
  something else instead (e.g. show only that series).
- `ValueToPixel` and `PixelToValue` convert between data values and chart pixels, for placing
  your own elements over a chart.
- `ApplyAsync(changes)` runs several changes (zoom, selection, hidden series, …) and draws the
  chart once at the end.
- Box plots: `BoxPlotSeries` shows the spread of values per x: a box from the lower to the upper
  quartile with the median, whiskers to 1.5 box lengths and outliers as dots. Give one item per
  value (the boxes are computed per x) or precomputed boxes (`Q1Value`, `MedianValue`, `Q3Value`,
  `LowValue`, `HighValue`, optional `OutliersValue` and `CountValue`). Several series sit side by
  side; the tooltip lists the box's numbers. JSON specs: `"type": "boxPlot"`, with optional `q1`,
  `q3` and `high` fields (and `low`). Core: `SeriesType.BoxPlot`, `BoxSummary`, `BoxColumns`,
  `SeriesData.WithBox` and `FromBoxGroups`; new `ChartTexts` keys for the tooltip rows.
- Candlesticks: `CandlestickSeries` shows how a value moved within each period: a wick from low to
  high and a body from open to close, lightly filled when it rose and solid when it fell. Give raw
  readings with a `Bucket` (e.g. `TimeBucket.Hour`: first, highest, lowest and last reading per
  hour) or precomputed candles (`OpenValue`, `HighValue`, `LowValue`, `CloseValue`). When zoomed
  out, candles too narrow to see merge into longer calendar periods (hours, a day, a week, …) that
  keep the highs and lows. The tooltip
  shows open, high, low, close and the change. New theme tokens `--rio-positive` and
  `--rio-negative`. JSON specs: `"type": "candlestick"` (also `ohlc`) with `timeBucket`, or `open`,
  `high` and `low` fields. Core: `SeriesType.Candlestick`, `CandleSummary`, `CandleColumns`,
  `SeriesData.WithCandles` and `FromCandleBuckets`, `TimeBuckets.Floor`.
- Radar charts: `RadarSeries` draws one spoke per category (x), clockwise from 12 o'clock, and a
  lightly filled shape per series on a value scale from zero shared by all spokes (fix it with the
  y axis `Min`/`Max`). Hovering a spoke shows every series' value on it; the legend lists series.
  A missing value leaves the shape open instead of pulling it to the center. Long category names
  are shortened with "…". JSON specs: `"type": "radar"` (also `spider`). Core:
  `SeriesType.Radar`, `TooltipModel.Radar` (`RadarGeometry`), `TooltipSeries.Spokes`; new
  `ChartTexts.RadarChart`.
- Treemaps: `TreemapSeries` shows how a total splits into many parts as rectangles sized by value
  (squarified), nested by group. `XValue` is each item's path (`"Asia/India"`, `PathSeparator` to
  change the "/"), or its name with `ParentValue` for a parent-child list. Groups get a header
  with their name and total when there is room; colors and legend items follow the top-level
  groups: hovering a legend item highlights its group, and clicking it leaves it out. `GroupSmallItems` gathers small items into "Other" per
  group. The tooltip shows the path, the value and its share of the group and of the total. Zero
  and negative values are left out (the screen reader text says how many). JSON specs:
  `"type": "treemap"` with `"path": ["continent", "country"]` or an x path. Core:
  `SeriesType.Treemap`, `SeriesSpec.Path` and `PathSeparator`, `TooltipSeries.CellHeights` and
  `PointColors`; new `ChartTexts` keys `Treemap`, `ShareOfGroup`, `ShareOfTotal` and
  `TreemapLeftOut`.
- Treemap drill-down: click a group to show only it (anywhere in it, unless items take clicks
  with `OnPointClick` or point selection: then its header or frame), with its children in their
  own colors and a path bar ("All › Asia") to go back up. From code: `@bind-TreemapPath`
  (e.g. to keep it in the URL), `DrillDownAsync(path)`, `GetTreemapPath()` and `OnDrillDown`.
  `VisibleLevels` (e.g. 2) draws that many levels at a time below the group shown: deeper groups
  show as one cell with their total and open with a click (JSON: `"visibleLevels"`). Core: `LayoutOptions.TreemapPath` and `InteractiveTreemap`,
  `ChartLayoutResult.TreemapPath`, scene interactions of kind "treemap"; new `ChartTexts` keys
  `TreemapAll`, `TreemapShow` and `TreemapPathLabel`.
- Colors by value: `ColorBands` (e.g. `[new ColorBand { Above = 120, Color = "--rio-negative" }]`)
  colors a line or area where it runs in a band, changing exactly where it crosses the edge (also
  between points); bars and scatter points take the color of the band their value is in, and the
  tooltip dot follows. `Above`, `Below` or both; later bands win; `ColorValue` wins over bands.
  JSON specs: `"colorBands"`. The canvas layer clips shapes to their group (`CanvasOp.Cl`).
- Colors per item: `ColorValue` on every series gives an item its own color, e.g.
  `o => o.Late ? "--rio-negative" : null` (a CSS color or theme token; null keeps the series
  color). Bars (also horizontal and stacked), scatter markers, pie slices (and their legend
  swatch), radial bars and treemap items take it; lines and areas mark the point with a dot; the
  tooltip dot follows. Aggregated points take their first item's color. JSON specs:
  `"colorField"`. Core: `SeriesData.Colors`, `WithColors` and `ColorAt`, `SeriesSpec.ColorField`.
- Radar: the scale values sit above the shapes on a light backing, so a shape crossing the first
  spoke no longer covers them, and `ShowTooltipAsync("Battery")` shows that spoke's tooltip
  (before, it did nothing on radar charts).
- Pies, donuts and radial bars: the labels of the other slices fade too when a legend item is
  hovered.
- Linked charts (same `Group`) follow a zoom set from code: a bound `VisibleRange` changed by the
  parent, `ZoomToAsync`, `ResetZoomAsync` and `PanAsync`. Before, only the chart it was set on
  zoomed; drag, pan and wheel zooms already reached the others.
- Box plots and candlesticks: clicking anywhere on a box or candle selects it (and raises
  `OnPointClick`). Before, they were hit-tested as points at the category's center and median,
  ignoring each series' own slot, so most clicks on a box missed.
- Pies, donuts and radial bars: hovering a legend item highlights its slice or bar (in every ring)
  and fades the others; before, nothing happened, as the legend lists categories while the chart
  only had one highlightable group per series. Slices and radial bars are now scene groups keyed
  by category. With `SeriesLayer="Canvas"`, only the plot's top-level series groups move to the
  canvas (nested keyed groups stay with their parent).
- Shared tooltips with irregular readings: a line whose nearest point was a little too far from
  the hovered time (e.g. readings logged on change, or a silence) was left out of the tooltip,
  although its line runs through that time. Its nearest point is listed now, unless a gap or the
  end of the line lies between.
- No more native "Chart" tooltip over the whole chart: the SVG's `<title>` is gone (`aria-label`
  still names the chart for screen readers).
- Clicking a heatmap cell anywhere in it counts as a click on it (before, only near its center).
- JSON specs with `timeBucket`: bucketed DateTime values were turned into DateTimeOffsets with the
  machine's local offset; they stay DateTimes now.

## 0.1.50-preview (2026-11-21)

The first release with notes: everything since the start, restructured to the new design
([Design.md](Design.md)), milestones 1 to 6. The earlier previews on NuGet, 0.1.2-preview and
0.1.14-preview, were snapshots of this work; the API changes listed as breaking below are
breaking for them.

### Blazor

- API review before the first release:
  - **Breaking:** the JS callbacks (`OnPointClicked`, `OnZoom`, `OnZoomReset`, `OnResize`,
    `OnTooltipHover`, `OnAnnotationDragged`) are no longer public methods on `RioChart`; the JS
    module calls a private callback object. Use the events (`OnPointClick`, `VisibleRangeChanged`,
    `OnAnnotationChanged`, …) and `ZoomToAsync`/`ResetZoomAsync`.
  - **Breaking:** rendering internals are internal: `SvgSceneRenderer`, `SvgWriter`,
    `CanvasSeriesWriter` (Blazor), `CoordinateSystem` and `ChartSpecJson.Options` (Core; use
    `ChartSpecJson.Parse`/`Serialize`, which fill in defaults), `SvgExporter.N`/`PathData` and
    `SkiaRenderer.ParseColor`/`ToSkPath` (Skia), and `Spec`/`ItemList` on series and annotation
    components.
  - **Breaking:** `ChartTexts.Format(template, provider, count, fallback, args)` is internal;
    `texts.Fill(key, provider, args)` fills one of a set's templates with the English fallback.
  - **Breaking:** `ExportOptions.Scale` is a `double` (was `float`), like
    `ChartImageOptions.Scale`; `SkiaRenderer.Encode` takes a `double` scale, and a scale that is not
    positive draws at 1.
  - **Breaking:** `PieSeries.InnerRadius`/`StartAngle` and `RadialBarSeries.StartAngle`/`EndAngle`
    are `double?` like the spec: null is the chart's default. This also fixes nested pie rings
    without an `InnerRadius`: the innermost had no hole, unlike the same chart from a JSON spec (0.4).
  - Every public type and member has an XML doc comment, and the build now warns about missing
    ones (CS1591).
- **Breaking:** the `<Chart>` component is renamed to `<RioChart>` (`RioChart<TItem>`), so it does
  not clash with an app's own `Chart` types or other libraries. Series and annotation components
  keep their names. `ChartRazor` now generates `<RioChart>`.
- Horizontal bars (`Horizontal="true"` on `RioChart`, `ChartSpec.Horizontal`, `"horizontal": true`
  or `"type": "horizontalBar"` in JSON): categories down the side (first on top, so sorted data
  reads as a ranking), values along the bottom with vertical grid lines. Grouped or stacked bars,
  negatives to the left, values at the bar ends, long names shortened with "…" (full in the
  tooltip), value lines and bands across the rows with their labels above. `YAxis` formats the
  values. Hover outlines a bar. Docs: a horizontal example on the Bars page.
- Range areas (`<RangeAreaSeries LowValue HighValue>`, `SeriesType.RangeArea`, `"rangeArea"` or
  `"range"` with a `"low"` field in JSON): a light band between a low and a high value per x,
  usually behind a line. `SeriesData` has an optional `Low` column (`WithLow`, in input order like
  the y values). The tooltip shows "low – high"; points missing either end are gaps; ranges are
  never stacked. When aggregating, the band keeps the lowest low (and in specs with average or sum,
  the highest high). Docs: a range example on Lines and areas.
- Heatmaps (`<HeatmapSeries>`, `SeriesType.Heatmap`, `"heatmap"` in JSON): every heatmap series
  is a row (named by the series; `groupBy` in JSON specs makes one row per value), its x values the
  columns (categories, or numbers and times with cells as wide as the data's spacing) and its y
  values the color: one hue, more opaque for higher values, so it works in light and dark themes
  and in exports. The legend is a color scale; `Max` fixes the full-color value, `DataLabels` writes
  values in the cells with contrast. Hover outlines a cell; arrow keys move between cells and rows.
  Docs: a Heatmap page (traffic by weekday and hour, conversion per channel).
- Samples: every example (docs site, demo app, MCP sample data, READMEs, doc comments and the JSON
  schema's example) now uses an online store (sessions, orders, revenue in €, stock, response times)
  instead of building climate data. The MCP sample's files are now `orders.csv`,
  `revenue-by-category.json` and `stores.csv`; the demo's export route is `/export/traffic.{svg|png}`.
- Scatter series (`<ScatterSeries>`, `SeriesType.Scatter`, `"scatter"` or `"points"` in JSON): a
  translucent marker per point (`MarkerSize`, default 5 px) and an optional dashed least-squares
  `TrendLine`. Numeric x axes get a nice range so points are not on the edge; the tooltip shows the
  nearest point in 2D with the x axis title ("Outdoor °C: -2.2"), so charts with scatter series are
  not shared. Legend swatch is a dot; the legend tooltip and compact summary give the number of
  points, the y range and whether y rises with x. Works with zoom, keyboard, canvas, export, Skia
  and spec-to-Razor. Docs: a Scatter page (outdoor compensation, energy signature).
- Fixed: interactive charts failed in published (trimmed) Blazor WebAssembly apps, including the
  docs site, with "ConstructorContainsNullParameterNames": the JS interop payloads were anonymous
  types serialized by reflection. They are now source-generated UTF-8 JSON (`Rendering/Interop.cs`),
  which is also native AOT safe. A test fails if a JS call gets anything but primitives or bytes.
- Legend tooltips: hovering or focusing a legend item shows a summary: a series' range, last
  value and trend (`ChartDescriber.DescribeSeries`), a slice's value and share, and for "Other"
  every slice it holds. Interactive charts show it in a card like the chart tooltip; exports and
  static pages get an SVG `<title>`. `LegendEntry.Tooltip` has the text.
- Pies: slices grouped by `GroupSmallSlices` are one "Other" legend item (in the slice's color)
  instead of listing each grouped slice; "Other" can be hidden from the legend like any slice
  (`ChartLayout.OtherSliceKey` in `HiddenSeries`).
- Visual snapshot tests: 18 reference charts (all series types, axes, annotations, time zones,
  compact, auto-compact, languages, edge cases) are compared as SVG with
  `tests/RioCharts.Tests/Snapshots`; a Test workflow runs all tests on Linux for every push.
- Auto-compact (`AutoCompact`, default on; `"autoCompact"` in JSON specs): as a normal chart
  gets small it leaves out the x axis title (below 180 px high), then the legend and y axis title
  (160 and 150 px high, or under 200 px wide), the title (120 px high) and the axis labels (x below
  90 px high, y below 200 px wide). A right legend moves to the top under 400 px wide, and an
  automatic legend taller than 30% of the chart is left out. Below 120 × 60 px the chart is laid out
  as a compact chart. Sizes scale with the font size. `LegendMode.Show` keeps the legend.
  `AutoCompact="false"` keeps everything at any size. Pie and radial bar charts leave out the
  legend and title the same way. Charts that are small today look different (less chrome).
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
- Fixed: the `RioCharts.Mcp` tool package carried SkiaSharp's native Windows PDBs (about 100 MB
  each for x86, x64 and arm64). They made the package 162 MB and, not being portable PDBs, made
  nuget.org reject its symbol package (0.1.14-preview has no symbols). The tool now leaves native
  PDBs out (symbols 0.2 MB), and the release workflow checks that every PDB in a symbol package
  is portable before pushing.
- `RioCharts.Mcp` carries SkiaSharp's native library for the common platforms only: Windows x64,
  arm64 and x86, macOS, and Linux x64 and arm64 (glibc and musl). Dropped: 32-bit ARM and x86
  Linux, Android (Bionic), LoongArch and RISC-V. The package went from 162 MB to 45 MB. The
  libraries (`RioCharts.Skia`) still bring every platform SkiaSharp supports.

### AI

- **Breaking:** `ChartTool.ChartCreated` is raised for render_chart too (after the file is
  written, when `OutputDirectory` is set), so an app can show or keep the images an agent asks
  for without digging them out of the tool results. The new `ChartToolResult.Tool` says which tool
  made the chart. A handler that only wants create_chart charts checks `Tool`; a render_chart
  image can be SVG, with `Png` null.
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

- Code samples in the README, the NuGet readme and the docs passed object values with quotes
  inside an attribute (`YAxis="new AxisSpec { Title = "Sessions" }"`), which does not compile;
  they now use `YAxis="@(new AxisSpec { Title = "Sessions" })"`.
- Performance check with large data (numbers in `docs/implementation-notes.md`):
  - Pies with many slices: outside labels are limited to what fits down each side (the largest
    slices keep theirs; all slices stay in the tooltip), and the legend and color lookups are no
    longer quadratic. 1000 slices: from about 1 s (10 000: minutes) to under 40 ms.
  - Large line and area series: the screen reader description and legend tooltips no longer copy
    every point, and x pixels are mapped only for the points drawn. A 1M-point line lays out in
    about 40 ms instead of 100 ms and allocates 0.3 MB instead of 38 MB. In the browser, zooming
    the docs site's 200k-point chart (WebAssembly) went from about 0.6 s to 0.1 s.
  - Stacked series with the same x values stack per index: three stacked 1M-point areas from
    590 ms to 120 ms.
  - CSV datasets are read straight into columns with a fast path for ISO times: 200k rows in
    about half the time and memory.
  - PNG export uses one PNG filter (Up) instead of trying all: the same file size in about half
    the encoding time.
  - Large scatter series are thinned: above the downsampling threshold, at most three markers per
    square one marker radius wide, which renders the same (translucent markers are solid by then)
    while outliers and sparse areas keep every point. 100k points in a cloud draw about 20k, with a
    fifth of the tooltip data; the trend line still uses every point. Docs: a 100 000-point scatter
    on the Large data page.
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
