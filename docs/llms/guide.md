# RioCharts

> Charts for .NET: line, area, bar, scatter, bubble, pie, donut, radial bar, gauge, bullet, radar, heatmap, box plot, histogram, dumbbell, range bar (timeline), candlestick, waterfall, treemap and world map, interactive in Blazor, as PNG and SVG on the server, and from AI agents. This guide covers the Blazor component, the usual way into an app. The chart is laid out in .NET and rendered as SVG; a small JS module only measures text and handles the pointer. Packages are previews (APIs change before 1.0) and target .NET 10 (LTS) and .NET 11.

This is the complete guide for coding agents: setup, rules, examples that compile, and every component parameter. Docs with live examples: https://riocharts.com/

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

.NET MAUI Blazor Hybrid apps (`dotnet new maui-blazor`) use the same package and usings, nothing else: the chart runs in the app's WebView and is always interactive, so no render mode is needed. Tested on Windows; Android and iOS not yet.

Other packages, only when needed: `RioCharts.Skia` (PNG/SVG export on the server, `ChartExport`), `RioCharts.AI` (chart tools for AI agents), `RioCharts.Core` (the engine alone; RioCharts.Blazor already brings it).

## How it works

- `<RioChart>` holds series as child components (`<LineSeries>`, `<BarSeries>`, ...) and annotations (`<ReferenceLine>`, ...). Series render nothing themselves; the chart lays them out together.
- `Items` goes on the chart (shared by all series) or on each series. `TItem` is inferred from `Items`; set `TItem` on `<RioChart>` when only the series have `Items` or when the chart has event handlers (`OnPointClick`, ...).
- `XValue` returns the x value: `DateTime`/`DateTimeOffset` give a time axis, numbers a linear axis, strings a category axis (categories keep the order of the items).
- `YValue` returns `double?`; `null` leaves a gap. Cast `decimal` to `double`.
- Data over several powers of ten (1 to 1,000,000): a log y axis, `YAxis="@(new AxisOptions { Scale = ScaleType.Log })"` (`"scale": "log"` in JSON definitions). Values of zero or below are left out.
- Several items with the same x: `Aggregate="Aggregate.Sum"` (also Average, Min, Max, Count, Last), or `YAggregate="g => ..."` with your own calculation over the group.
- Error bars (bar, line and scatter series): `ErrorValue="m => m.StdDev"` (plus or minus, per item), `Error="2.5"` (the same for every point), or `ErrorLowValue` and `ErrorHighValue` (the ends, as values). JSON: `errorField`, `error`, `errorLow` and `errorHigh`.
- Time x per hour, day, month...: `Bucket="TimeBucket.Day"` on line, area and bar series gives one point per bucket, combined with `Aggregate` (average when not set). `TimeBucket.Auto` picks the bucket from the data's time span (about 50 points). No need to group in the query.
- Updates: selectors run once per data change. The chart reprocesses a series when `Items` is a new reference or `DataVersion` changes. Changing a list in place does nothing until you assign a new list or change `DataVersion`. For streaming data use a `SeriesBuffer`.
- Size: the chart fills its container's width; `Height` is 300 px by default.
- Number formats are .NET format strings (`Format="#,0 €"`, `"0.0 °C"`, `"0'%'"`) in the chart's `Culture` (default: the current culture).
- Colors: CSS colors or theme variables, e.g. `Color="#2563eb"` or `Color="--rio-negative"`. The palette and light/dark theme come from CSS variables (`--rio-series-1` ... `--rio-series-10`, `--rio-text`, ...).
- Namespaces beyond the two usings: `RioCharts.Core.Data` (`SeriesData`, `SeriesBuffer`), `RioCharts.Core.Definitions` (`ChartDataset`, `ChartDefinitionJson`).

## Choosing a series

| To show | Component | Key parameters |
| --- | --- | --- |
| Change over time, trends | `LineSeries` | `XValue`, `YValue`, `Curve` |
| Volume over time, parts of a total over time | `AreaSeries` | `Stack` to stack |
| Compare categories | `BarSeries` | `Aggregate`, `Stack`, `Horizontal="true"` on the chart |
| Shares per x value (100 % stacked) | `BarSeries`, `AreaSeries` | `YAxis="@(new AxisOptions { Stacking = StackMode.Percent })"` on the chart |
| Min to max band | `RangeAreaSeries` | `LowValue`, `YValue` |
| Relation between two numbers | `ScatterSeries` | `XValue` and `YValue` numeric, `TrendLine` |
| Three numbers per item (x, y and a size) | `ScatterSeries` with `SizeValue` | bubbles; `SizeName` labels the size in the tooltip |
| Shares of a whole | `PieSeries`, `DonutSeries` | `GroupSmallSlices` |
| Progress toward a goal | `RadialBarSeries` | `Max` |
| One value on a scale (load, how full), with ranges and a target | `GaugeSeries` | `Value` (or `YValue` per item, `XValue` its label), `Max`, `Target`, `ColorBands` |
| Values against targets in little space (KPIs) | `BulletSeries` | one per measure with `Value`, `Max`, `Target`; or Items with `XValue`, `YValue`, `TargetValue` |
| Several measures per item | `RadarSeries` | one series per item |
| Values over two categories (hour × day) | `HeatmapSeries` | one series per row (`Name`); `XValue` the column, `YValue` the color |
| Spread of values | `BoxPlotSeries` | raw values, or `Q1Value`, `MedianValue`, `Q3Value` |
| Distribution of values (e.g. response times) | `HistogramSeries` | one item per value, `XValue` the value; `BinWidth` or `BinCount` |
| Two values per category (before/after, plan/actual) | `DumbbellSeries` | `XValue` the category, `FromValue` the first value, `YValue` the second; `Horizontal="true"` on the chart for rows |
| Periods per row on a time axis (schedule, states over time) | `RangeBarSeries` | `RowValue`, `StartValue`, `EndValue`; one series per state |
| Open, high, low, close | `CandlestickSeries` | `OpenValue`, `HighValue`, `LowValue`, `CloseValue`, or raw values with `Bucket` |
| From a start value to an end value (result, budget) | `WaterfallSeries` | `YValue` the change, `IsTotal` marks totals |
| Hierarchy by size | `TreemapSeries` | `XValue` (path or name), `ParentValue`, `VisibleLevels` |
| Values per country, places on a world map | `MapSeries`, `MapBubbleSeries` | `MapSeries`: `XValue` the country code ("SE") or English name, `YValue` the value; `MapBubbleSeries`: `XValue` the place's name, `YValue` its size, `Latitude`, `Longitude`; `Projection="MapProjection.Mercator"` on the chart for the web-map look |

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
- Animation options (ApexCharts `Animations`, Chart.js `animation`): there are none. Charts are not animated; the series only fade in on first render. `Animate="false"` turns that off.
- Setting a width: not needed, the chart follows its container. Set `Height` for another height.
- Types not found (`SeriesData`, `SeriesBuffer`, `ChartDataset`): add `@using RioCharts.Core.Data` or `@using RioCharts.Core.Definitions`.
- Hard-coded colors for light and dark: use theme variables such as `--rio-negative` and `--rio-positive`; they follow the theme.

## Reference

Every public component and its parameters, from the XML docs. Types in `RioCharts.Core` and `RioCharts.Blazor` used by parameters follow the components.

{{api}}

## More

- Getting started: https://riocharts.com/getting-started
- All chart types with live examples: https://riocharts.com/lines-and-areas
- Chart definitions (JSON) and the playground: https://riocharts.com/definitions
- AI agents and MCP (`riocharts-mcp` renders charts to PNG and turns definitions into Razor): https://riocharts.com/ai
- Source: https://github.com/joadan/RioCharts
