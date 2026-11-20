# RioCharts

A pure .NET chart library, with Blazor as the first target. C# does the math and layout,
Razor renders SVG, and a tiny JS module only measures the container and the font.

> **Status: early preview.** APIs will change without notice before 1.0. It is built for our
> own teams' needs; issues are welcome, but there is no roadmap promise.

Named after Rio, and *río* means river: fitting for flowing lines and time series.

## What works today (milestone 1)

- Line, area and bar series (grouped bars), with linear, monotone and step lines
- Linear, time and category axes, picked automatically from the data
- Nice ticks; time ticks that switch between minutes, hours, days, weeks, months and years
- Compact, culture-aware labels (1.2k, 3.4M); dates adapt to the visible range
- Gaps for missing values; clean states for empty data, a single point and flat data
- Legend (shown when there is more than one series); click to hide and show series
- Responsive width with real font measurement; fixed height (300 px by default)
- Light and dark themes through CSS variables; colorblind-aware palette
- LTTB downsampling for large series, and a columnar fast path for big data
- Screen-reader description of every chart (series, extremes, trend)

Not yet: tooltips and crosshair, stacking, multiple Y axes, annotations, pie and donut,
zoom and pan. See [Design.md](Design.md) for the design and milestones.

## Usage

Reference `RioCharts.Blazor` and add `@using RioCharts.Blazor` and `@using RioCharts.Core`
to `_Imports.razor`. Styles come with `{YourApp}.styles.css` automatically.

```razor
<Chart Items="readings">
    <LineSeries XValue="r => r.Timestamp" YValue="r => r.SupplyTemp" />
</Chart>
```

Several series, options and two-way bound legend state:

```razor
<Chart TItem="Reading" Title="Air handling unit" YAxis="new AxisSpec { Title = "°C" }" @bind-HiddenSeries="hidden">
    <LineSeries Items="supply" Name="Supply" XValue="r => r.Time" YValue="r => r.Value" Curve="CurveType.Monotone" />
    <AreaSeries Items="extract" Name="Extract" XValue="r => r.Time" YValue="r => r.Value" />
    <BarSeries Items="energy" Name="Energy" XValue="r => r.Time" YValue="r => r.Value" />
</Chart>
```

- `XValue` may return a number, `DateTime`, `DateTimeOffset`, `DateOnly` or `string` (category).
- `YValue` returns `double?`; `null` leaves a gap. Cast `decimal` values to `double`.
- Data is processed when `Items` is a new reference or `DataVersion` changes, not on every render.

Large data, as plain arrays (no per-item cost):

```razor
<Chart TItem="double">
    <LineSeries Name="Sensor" Data="SeriesData.From(timestamps, values)" />
</Chart>
```

Theming: set any of `--rio-text`, `--rio-text-muted`, `--rio-grid`, `--rio-axis` and
`--rio-series-1` … `--rio-series-10` anywhere above the chart. Dark mode follows
`prefers-color-scheme`, or an ancestor with `data-theme="dark"` or class `dark`.

Times are shown exactly as given: no time zone conversion.

## Projects

| Path | What |
|------|------|
| `src/RioCharts.Core` | Scales, ticks, layout, path builders, scene, hit-testing, chart spec. No UI dependencies. |
| `src/RioCharts.Blazor` | `<Chart>` and series components, SVG scene renderer, JS module. |
| `tests/RioCharts.Tests` | xUnit tests for Core and the SVG writer. |
| `samples/RioCharts.Demo` | Blazor Server demo of everything above. |

```powershell
dotnet test
dotnet run --project samples/RioCharts.Demo --launch-profile http
```

See [CHANGELOG.md](CHANGELOG.md) for changes.
