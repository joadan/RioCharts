# RioCharts

> Charts for Blazor in pure .NET: line, area, bar, scatter, pie, donut, radial bar, radar, heatmap, box plot, candlestick and treemap. The chart is laid out in .NET and rendered as SVG; a small JS module only measures text and handles the pointer. Packages are previews (APIs change before 1.0) and target .NET 10 (LTS) and .NET 11.

This is the complete guide for coding agents: setup, rules, examples that compile, and every component parameter. Docs with live examples: https://joadan.github.io/RioCharts/

## Setup

1. Add the package (a preview, so `--prerelease` is needed):

   ```shell
   dotnet add package RioCharts.Blazor --prerelease
   ```

2. Add two usings to `_Imports.razor`:

   ```razor
   @using RioCharts.Blazor
   @using RioCharts.Core
   ```

3. That is all. There are no services to register and no script or stylesheet tags: the styles come with the app's `{AssemblyName}.styles.css` bundle (already linked by the Blazor templates) and the JS module loads itself.

Tooltips, zoom, clicks, selection, keyboard navigation and downloads need an interactive page (`@rendermode InteractiveServer`, `InteractiveWebAssembly` or `InteractiveAuto`, or a global interactive render mode). On a static page the chart renders as plain SVG without interaction.

Other packages, only when needed: `RioCharts.Skia` (PNG/SVG export on the server, `ChartExport`), `RioCharts.AI` (chart tools for AI agents), `RioCharts.Core` (the engine alone; RioCharts.Blazor already brings it).

## How it works

- `<RioChart>` holds series as child components (`<LineSeries>`, `<BarSeries>`, ...) and annotations (`<ReferenceLine>`, ...). Series render nothing themselves; the chart lays them out together.
- `Items` goes on the chart (shared by all series) or on each series. `TItem` is inferred from `Items`; set `TItem` on `<RioChart>` when only the series have `Items` or when the chart has event handlers (`OnPointClick`, ...).
- `XValue` returns the x value: `DateTime`/`DateTimeOffset` give a time axis, numbers a linear axis, strings a category axis (categories keep the order of the items).
- `YValue` returns `double?`; `null` leaves a gap. Cast `decimal` to `double`.
- Several items with the same x: `Aggregate="Aggregate.Sum"` (also Average, Min, Max, Count, Last), or `YAggregate="g => ..."` with your own calculation over the group.
- Updates: selectors run once per data change. The chart reprocesses a series when `Items` is a new reference or `DataVersion` changes. Changing a list in place does nothing until you assign a new list or change `DataVersion`. For streaming data use a `SeriesBuffer`.
- Size: the chart fills its container's width; `Height` is 300 px by default.
- Number formats are .NET format strings (`Format="#,0 €"`, `"0.0 °C"`, `"0'%'"`) in the chart's `Culture` (default: the current culture).
- Colors: CSS colors or theme variables, e.g. `Color="#2563eb"` or `Color="--rio-negative"`. The palette and light/dark theme come from CSS variables (`--rio-series-1` ... `--rio-series-10`, `--rio-text`, ...).
- Namespaces beyond the two usings: `RioCharts.Core.Data` (`SeriesData`, `SeriesBuffer`), `RioCharts.Core.Specs` (`ChartDataset`, `ChartSpecJson`).

## Choosing a series

| To show | Component | Key parameters |
| --- | --- | --- |
| Change over time, trends | `LineSeries` | `XValue`, `YValue`, `Curve` |
| Volume over time, parts of a total over time | `AreaSeries` | `Stack` to stack |
| Compare categories | `BarSeries` | `Aggregate`, `Stack`, `Horizontal="true"` on the chart |
| Min to max band | `RangeAreaSeries` | `LowValue`, `YValue` |
| Relation between two numbers | `ScatterSeries` | `XValue` and `YValue` numeric, `TrendLine` |
| Shares of a whole | `PieSeries`, `DonutSeries` | `GroupSmallSlices` |
| Progress toward a goal | `RadialBarSeries` | `Max` |
| Several measures per item | `RadarSeries` | one series per item |
| Values over two categories (hour × day) | `HeatmapSeries` | one series per row (`Name`); `XValue` the column, `YValue` the color |
| Spread of values | `BoxPlotSeries` | raw values, or `Q1Value`, `MedianValue`, `Q3Value` |
| Open, high, low, close | `CandlestickSeries` | `OpenValue`, `HighValue`, `LowValue`, `CloseValue`, or raw values with `Bucket` |
| Hierarchy by size | `TreemapSeries` | `XValue` (path or name), `ParentValue`, `VisibleLevels` |

Check the parameters of each component in the reference below before using them.

## Examples

Each example is a complete component file. These exact files are compiled and rendered by the RioCharts tests.

{{examples}}

## Common mistakes

- No interactive render mode: the chart shows, but tooltips, zoom, clicks and downloads do nothing. Add `@rendermode InteractiveServer` (or WebAssembly/Auto) to the page.
- Changing a `List<T>` in place (`Add`, `Remove`) and expecting the chart to update: assign a new list, or change `DataVersion`.
- `YValue="x => x.Amount"` where `Amount` is `decimal`: write `(double)x.Amount`.
- Several series with their own `Items` and no `TItem` on `<RioChart>`: set `TItem="YourType"`.
- An event handler lambda (`OnPointClick="e => clicked = e"`) without `TItem`: Blazor cannot infer `TItem` through it and reports errors such as "Cannot convert lambda expression". Set `TItem="YourType"` on `<RioChart>`.
- Blazor-ApexCharts names: there is no `ApexPointSeries` or `SeriesType` parameter; each type is its own component (`<BarSeries>`, `<LineSeries>`, ...). `XValue`, `YValue`, `YAggregate` work as in ApexCharts.
- Setting a width: not needed, the chart follows its container. Set `Height` for another height.
- Types not found (`SeriesData`, `SeriesBuffer`, `ChartDataset`): add `@using RioCharts.Core.Data` or `@using RioCharts.Core.Specs`.
- Hard-coded colors for light and dark: use theme variables such as `--rio-negative` and `--rio-positive`; they follow the theme.

## Reference

Every public component and its parameters, from the XML docs. Types in `RioCharts.Core` and `RioCharts.Blazor` used by parameters follow the components.

{{api}}

## More

- Getting started: https://joadan.github.io/RioCharts/getting-started
- All chart types with live examples: https://joadan.github.io/RioCharts/lines-and-areas
- JSON specs and the playground: https://joadan.github.io/RioCharts/specs
- AI agents and MCP (`riocharts-mcp` renders charts to PNG and turns specs into Razor): https://joadan.github.io/RioCharts/ai
- Source: https://github.com/joadan/RioCharts
