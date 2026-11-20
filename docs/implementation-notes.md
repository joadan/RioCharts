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
- **Hit-test index** is built in Core (sorted pixel x per series, binary search) but not used by
  the Blazor side yet; tooltips in milestone 2 will send it to JS.
- **Target framework** is net10.0 only for now.
