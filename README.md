# RioCharts

SVG charts for Blazor, rendered directly in Razor with no JavaScript.

- Line, scatter and area charts (`XYSeries`), with multiple Y axes
- Pie, donut and funnel charts (`ValueSeries`)
- Bubble charts and heatmaps (`XYZSeries`)
- Tooltips, click to select, click the legend to show or hide series
- Keyboard navigation and screen reader descriptions
- Large datasets are downsampled with LTTB (over 1000 points by default)
- Optional pan, zoom (wheel, pinch, keyboard) and crosshair on the X axis

Charts render without JavaScript. Pan/zoom and crosshair load a small JS module only when turned on.

The full specification is in [Design.md](Design.md).

## Usage

Reference `src/RioCharts` from your Blazor app and add `@using RioCharts` to `_Imports.razor`.
The component's styles are included automatically through `{YourApp}.styles.css`.

```razor
<RioChart Title="Revenue" Width="800" Height="400"
          XAxis="xAxis" Series="series"
          OnSeriesClick="args => selected = $"{args.Series?.Name}: {args.Y}"" />

@code {
    private string selected = "";
    private readonly Axis xAxis = new() { Title = "Month" };
    private readonly List<ISeries> series =
    [
        new XYSeries
        {
            Name = "Revenue",
            Marker = MarkerStyle.Circle,
            Points = [new(1, 120), new(2, 135), new(3, 128), new(4, 150)],
        },
    ];
}
```

Other series types:

```csharp
new ValueSeries { Type = ValueSeriesType.Donut, Points = [new("Alpha", 42), new("Beta", 27)] };
new XYZSeries { Type = XYZSeriesType.Bubble, Points = [new(48, 82, 67), new(12, 77, 1400)] };
new XYSeries { Name = "Area", FillColor = "auto", Points = ... };      // area chart
new XYSeries { Name = "Dots", ShowLine = false, Marker = MarkerStyle.Diamond, Points = ... }; // scatter
```

Dates: use `new XYPoint(DateTime, y)` and an axis with `LabelFormat = AxisLabelFormat.Date`
(or `Custom` with a format string such as `"MMM dd"`).

Live data: change `Points` in place and call `Refresh()` on the component (via `@ref`).

Pan, zoom and crosshair:

```razor
<RioChart Series="series" EnablePanZoom="true" EnableCrosshair="true" @bind-XAxisRange="visible" />

@code {
    private AxisRange? visible; // null = all data
}
```

Drag to pan, scroll or pinch to zoom, double-click to reset. Keyboard: `+`/`-` zoom, Shift+arrows pan, `0` resets.

## Projects

| Path | What |
|------|------|
| `src/RioCharts` | The component library |
| `tests/RioCharts.Tests` | xUnit tests for layout, scales and SVG helpers |
| `samples/RioCharts.Demo` | Blazor Server demo with every chart type |

```powershell
dotnet test
dotnet run --project samples/RioCharts.Demo
```
