# Chart Component Design Specification

The chart component is designed to be modular, extensible, and SVG-renderer friendly. It separates data model, layout, and rendering, supporting multiple chart types and axes.

## Implementation Status

Implemented: everything below except the items under "Not yet implemented".

Where the code differs from the sketches in this document:

- **Colors:** `XYSeries.StrokeColor` and `XYZSeries.FillColor` default to null, meaning "next color from
  `Chart.Palette`" (`ChartPalette.Default` or `ChartPalette.HighContrast`), instead of "blue" for everything.
- **ISeries:** `Visible` and `IsSelected` are settable on the interface so the legend and clicks can change them.
- **Points:** `XYPoint` and `XYZPoint` are `readonly record struct`s (cheaper for large datasets).
  Date values are OLE Automation dates; `new XYPoint(DateTime, y)` converts.
- **Additions:** `XYSeries.ShowLine` (false = scatter), `XYSeries.FillOpacity` (`FillColor = "auto"` uses the
  line color), `ValueSeries.Type` (Pie/Donut/Funnel), `ValueSeries.InnerRadiusRatio`, `ValueSeries.ShowLabels`,
  `ValuePoint.Visible`, `XYZSeries.Type` (Bubble/Heatmap), `Chart.Palette`, `Chart.DownsampleThreshold`.
- **Layout output:** the layout engine does not mutate `Axis`. It returns a `ChartLayout` with `AxisLayout`
  objects (computed min/max, ticks, pixel mapping) and `SeriesLayout`/`SliceLayout` per series.
  Screen points are `ScreenPoint` (doubles) rather than `PointF`.
- **Multiple Y axes:** even indexes are drawn on the left, odd on the right, stacking outward.
  Only the first Y axis draws horizontal grid lines.
- **Legend:** Top/Bottom/Left/Right take space from the plot; corner positions overlay the plot
  (for pie/donut/funnel-only charts they sit beside it instead). Pie slices with the same label across
  several series share one legend item.
- **Ranges:** heatmap and date axes use the data range as is (no rounding out); date ticks of a month or
  more land on the first of the month. Bubble axes get 10% padding so bubbles are not cut off.
- **Errors:** layout problems (invalid range, missing Y axis) are collected in `ChartLayout.Warnings`
  rather than logged.
- **Caching:** layout is recomputed when parameters change or `Refresh()` is called, not on hover or
  focus changes. In-place data changes need `Refresh()`.
- **Tooltip `CustomFormat`** arguments: {0} series name, {1} Y/value, {2} X/label, {3} Z or slice fraction.

- **Pan, zoom and crosshair** (`EnablePanZoom`, `EnableCrosshair`, both off by default) use the
  collocated module `RioChart.razor.js`, loaded only when one of them is on. Zoom and pan act on the
  X axis only and stay inside the full data range. During a gesture JS previews the change with an SVG
  transform on the plot content; Blazor is called once when the gesture ends (wheel: after 250 ms idle)
  and re-renders at the new range. Double-click or "Reset zoom" shows everything again. Keyboard:
  `+`/`-` zoom, Shift+arrows pan, `0`/Home reset. When zoomed, the Y axis fits the visible points and
  LTTB downsampling runs on the visible part only, so zooming in shows more detail. The visible range
  is `XAxisRange` (two-way bindable); `OnAxisRangeChanged` reports old and new ranges. `ZoomTo`,
  `ZoomBy`, `PanBy` and `ResetZoom` are public methods on the component.

Not yet implemented:

- Pattern fills for series.
- `ObservableCollection`/`INotifyPropertyChanged` auto-refresh and update throttling.
- Rendering only the visible viewport for very large datasets (data is downsampled and clipped instead).

## Chart Model

### Chart Container
class Chart
{
    string? Title;
    double Width = 800;
    double Height = 600;
    ChartMargin Margin = new(60, 40, 60, 80); // Top, Right, Bottom, Left
    string BackgroundColor = "white";
    Axis? XAxis;
    List<Axis> YAxes = new(); // Empty list by default, not null
    LegendSettings Legend = new();
    List<ISeries> Series = new(); // Empty list by default, not null
    TooltipSettings Tooltip = new();
    bool EnableInteractions = true;
    bool EnableAnimations = true;
}
class ChartMargin
{
    double Top, Right, Bottom, Left;
}
- Title: optional chart title
- Width/Height: chart dimensions in pixels
- Margin: space around the plot area for axes and labels
- BackgroundColor: chart background
- XAxis: optional horizontal axis
- YAxes: multiple vertical axes (support different scales per series), empty list by default
- Legend: configurable position, visibility, font
- Series: collection of ISeries objects, empty list by default
- Tooltip: tooltip configuration
- EnableInteractions: enable/disable mouse interactions
- EnableAnimations: enable/disable transitions

### Axis
class Axis
{
    string? Title;
    double? Minimum;
    double? Maximum;
    bool AutoRange = true;
    int TargetTickCount = 6;
    bool ShowGridLines = true;
    string GridLineColor = "#e0e0e0";
    double GridLineWidth = 1;
    AxisLabelFormat LabelFormat = AxisLabelFormat.Auto;
    string? CustomFormatString; // e.g., "C2" for currency, "P0" for percentage
    double LabelRotation = 0; // degrees
    string LabelColor = "black";
    float LabelFontSize = 11;
}
enum AxisLabelFormat { Auto, Number, Currency, Percentage, Date, Time, DateTime, Custom }
- Optional min/max; automatic range if null
- Supports tick calculation and optional grid lines
- LabelFormat: predefined or custom format strings
- LabelRotation: for long labels (e.g., 45° for dates)
- GridLineColor/Width: customizable grid appearance

### Legend Settings
enum LegendPosition { Top, Bottom, Left, Right, TopLeft, TopRight, BottomLeft, BottomRight }
class LegendSettings
{
    bool Visible = true;
    LegendPosition Position = TopRight;
    float FontSize = 12;
    string FontColor = "black";
    float MarkerSize = 10;
    string BackgroundColor = "white";
    string BorderColor = "#cccccc";
    double BorderWidth = 1;
    double Padding = 8;
    double Opacity = 0.9;
}
- Visible: show/hide legend
- Position: placement on chart
- Font styling: size and color
- MarkerSize: size of color/symbol indicators
- Background/Border: visual container styling
- Padding: internal spacing
- Opacity: semi-transparent background support

### Tooltip Settings
class TooltipSettings
{
    bool Enabled = true;
    string BackgroundColor = "rgba(50, 50, 50, 0.9)";
    string TextColor = "white";
    float FontSize = 12;
    double Padding = 8;
    double BorderRadius = 4;
    string? CustomFormat; // e.g., "{0}: {1:N2}" for series name + value
}
- Enabled: show/hide tooltips on hover
- Styling: background, text, padding, rounded corners
- CustomFormat: format string for tooltip content

## Series Types

### Base Interface
interface ISeries
{
    string Name { get; }
    bool Visible { get; }
    int RenderOrder { get; } // Z-order for overlapping series
    double Opacity { get; } // 0.0 to 1.0
}
- Name: series identifier for legend
- Visible: show/hide series
- RenderOrder: control draw order (lower values draw first)
- Opacity: transparency level

### XYSeries (Line, Scatter, Area)
class XYSeries : ISeries
{
    string Name;
    bool Visible = true;
    int RenderOrder = 0;
    double Opacity = 1.0;
    List<XYPoint> Points = new();
    string StrokeColor = "blue";
    double StrokeWidth = 2;
    string? FillColor; // for area charts
    MarkerStyle Marker = MarkerStyle.None;
    double MarkerSize = 6;
    string? MarkerColor; // defaults to StrokeColor if null
    int YAxisIndex = 0; // reference to YAxes
    bool IsSelected = false; // for interaction state
}
enum MarkerStyle { None, Circle, Square, Triangle, Diamond, Cross, Plus }
class XYPoint
{
    double X;
    double Y;
}
- FillColor: enables area chart rendering when set
- Marker: scatter plot point style
- MarkerSize/MarkerColor: marker appearance
- IsSelected: highlight state for user interaction

### ValueSeries (Pie, Donut, Funnel)
class ValueSeries : ISeries
{
    string Name;
    bool Visible = true;
    int RenderOrder = 0;
    double Opacity = 1.0;
    List<ValuePoint> Points = new();
    bool IsSelected = false;
}
class ValuePoint
{
    string Label;
    double Value;
    string? Color; // optional per-slice color
    bool IsExploded = false; // for pie/donut charts
}
- IsExploded: offset slice from center for emphasis
- Color: override default color palette per point

### XYZSeries (Bubble, Heatmap)
class XYZSeries : ISeries
{
    string Name;
    bool Visible = true;
    int RenderOrder = 0;
    double Opacity = 1.0;
    List<XYZPoint> Points = new();
    int YAxisIndex = 0;
    double MinBubbleSize = 5;
    double MaxBubbleSize = 50;
    string? FillColor = "blue";
    string? StrokeColor;
    double StrokeWidth = 1;
    bool IsSelected = false;
}
class XYZPoint
{
    double X;
    double Y;
    double Z; // size/intensity
}
- MinBubbleSize/MaxBubbleSize: scale Z values to pixel radius
- FillColor/StrokeColor: bubble appearance
- Z value maps to bubble size (bubble chart) or color intensity (heatmap)

## Layout Engine
- Responsible for mapping data to screen coordinates
- Handles margins, multiple Y axes, scaling, min/max, tick calculation, and downsampling
- Computes automatic ranges when Axis.AutoRange is true
- Validates axis ranges (ensures min < max, handles empty data)
- Applies downsampling algorithm (LTTB - Largest Triangle Three Buckets) for large datasets
- Caches layout results to avoid redundant calculations on re-render
- Produces SeriesLayout:
class SeriesLayout
{
    ISeries Series;
    List<PointF> ScreenPoints; // XY position for rendering
    List<double>? BubbleSizes; // for XYZSeries bubble radii in pixels
    Axis LayoutXAxis; // axis with computed range/ticks
    Axis LayoutYAxis; // axis with computed range/ticks
}
- ValueSeries stores slice angles instead of pixel points
- BubbleSizes: pre-calculated pixel radii for bubble charts

### Error Handling
- Empty data: render empty chart with axes and message "No data available"
- Invalid axis range (min >= max): use default range [0, 100] and log warning
- Missing YAxis reference: default to YAxes[0] if available
- Null or empty Points: skip series rendering but show in legend (grayed out)

## SVG Renderer

### Responsibilities
- Axes: render <line> for X/Y axes, <text> for tick labels, grid lines if enabled
- Series: XYSeries -> <path> for lines, optional <circle> for scatter points; ValueSeries -> <path> slices for pie/donut; XYZSeries -> <circle> for bubble with radius from Z
- Legend: <rect> + <text> for each series, positioned according to LegendSettings

### Performance Tips
- Use one <path> per series for line charts
- Downsample large datasets (>1000 points) using LTTB algorithm to reduce DOM elements
- Reuse <path> elements instead of recreating
- Only render visible points within the axis range
- Debounce layout recalculations during window resize (300ms)
- Use CSS transforms for animations instead of re-rendering SVG
- Implement virtual rendering for extremely large datasets (render only viewport)

### Rendering Pipeline
Chart Model (Series + Axes + Legend)
      ↓
Layout Engine (compute pixel positions, angles)
      ↓
SVG Renderer (draw axes, series, legend)
      ↓
Output: <svg> element

### Multi-Axis Support
- Chart.YAxes stores multiple vertical axes
- XYSeries.YAxisIndex selects which axis to map
- Each axis can have independent min/max, ticks, and labels
- Layout engine maps series Y values according to axis scale

## Extensibility
- Add new series types by creating new ISeries implementations
- Add custom renderers for new point types
- Supports multiple chart types in one chart (mixed line/scatter, pie, bubble)

## Interactivity & Events

### Interaction Approaches

**Approach 1: Pure Blazor (Recommended for basic charts)**
- Use @onclick, @onmouseenter, @onmouseleave on SVG elements
- Update Blazor state to toggle CSS classes for hover/selection
- Tooltip rendered as Blazor component
- Good for: static charts, simple click interactions, Blazor WASM
- Limitations: not suitable for real-time mouse tracking, pan/zoom

**Approach 2: Hybrid (JS for high-frequency, Blazor for state)**
- JS handles mousemove, wheel, touch events
- JS updates DOM directly for crosshair, tooltip position
- JS calls Blazor only for significant events (click, selection)
- Good for: advanced interactive charts, Blazor Server
- Complexity: requires JS interop code

### User Interactions
- Hover: highlight series, show tooltip with data point info
- Click: select/deselect series, toggle visibility from legend
- Pan: drag to shift axis range (when enabled)
- Zoom: mouse wheel or pinch to zoom in/out on data
- Crosshair: vertical/horizontal lines following mouse position

### Event Model
class ChartEventArgs
{
    ISeries? Series;
    int PointIndex;
    double X, Y;
    MouseButton Button;
}
- SeriesHover(ChartEventArgs)
- SeriesClick(ChartEventArgs)
- SeriesSelected(ChartEventArgs)
- AxisRangeChanged(Axis, double oldMin, double oldMax)
- LegendItemClick(ISeries)

## Accessibility

### ARIA Support
- <svg role="img" aria-label="{Chart.Title}">
- Each series: aria-label="{Series.Name}: {Points.Count} data points"
- Axis labels: aria-label for min/max values
- Legend items: role="button" for clickable items

### Keyboard Navigation
- Tab: navigate between series and legend items
- Enter/Space: toggle series visibility
- Arrow keys: move focus between data points
- Escape: clear selection

### Screen Reader Support
- Provide text alternative summarizing chart data
- Announce data ranges and key statistics
- Describe trends (increasing, decreasing, stable)

### Color Contrast
- Ensure text meets WCAG AA standards (4.5:1 contrast ratio)
- Provide configurable high-contrast color palettes
- Support pattern fills in addition to colors for series distinction

## Data Update Strategy

### Update Types
- Full Refresh: replace entire Series list, recalculate layout
- Incremental: append new points to existing series, update layout for new data only
- Streaming: continuous real-time updates with sliding window

### Observable Pattern
- Chart.Series as ObservableCollection for automatic UI updates
- INotifyPropertyChanged for series property changes
- Throttle updates to avoid excessive re-renders (e.g., max 60 FPS)

### Caching
- Cache SeriesLayout until data or dimensions change
- Invalidate cache on Series/Axis/Dimension changes
- Preserve zoom/pan state across data updates

## Blazor Component Integration

### Component API
```razor
<RioChart Width="800" 
          Height="600"
          Title="@title"
          XAxis="@xAxis"
          YAxes="@yAxes"
          Series="@series"
          EnableInteractions="true"
          EnableAnimations="true"
          OnSeriesClick="HandleSeriesClick"
          OnSeriesHover="HandleSeriesHover"
          OnAxisRangeChanged="HandleAxisRangeChanged" />
```

### Component Parameters
```csharp
[Parameter] public string? Title { get; set; }
[Parameter] public double Width { get; set; } = 800;
[Parameter] public double Height { get; set; } = 600;
[Parameter] public ChartMargin? Margin { get; set; }
[Parameter] public string BackgroundColor { get; set; } = "white";
[Parameter] public Axis? XAxis { get; set; }
[Parameter] public List<Axis>? YAxes { get; set; }
[Parameter] public LegendSettings? Legend { get; set; }
[Parameter] public List<ISeries>? Series { get; set; }
[Parameter] public TooltipSettings? Tooltip { get; set; }
[Parameter] public bool EnableInteractions { get; set; } = true;
[Parameter] public bool EnableAnimations { get; set; } = true;

// Event Callbacks
[Parameter] public EventCallback<ChartEventArgs> OnSeriesClick { get; set; }
[Parameter] public EventCallback<ChartEventArgs> OnSeriesHover { get; set; }
[Parameter] public EventCallback<ChartEventArgs> OnSeriesSelected { get; set; }
[Parameter] public EventCallback<AxisRangeChangedArgs> OnAxisRangeChanged { get; set; }
[Parameter] public EventCallback<ISeries> OnLegendItemClick { get; set; }
```

### Component Lifecycle
```csharp
protected override void OnInitialized()
{
    // Initialize default axes, legend if not provided
    // Set up internal state
}

protected override void OnParametersSet()
{
    // Detect parameter changes
    // Invalidate layout cache if data/dimensions changed
    // Validate configuration (axis ranges, series references)
}

protected override bool ShouldRender()
{
    // Only re-render if meaningful changes occurred
    // Avoid re-render during hover/interaction states handled by JS
    return _layoutInvalidated || _configurationChanged;
}

protected override void OnAfterRender(bool firstRender)
{
    if (firstRender && EnableInteractions && _advancedInteractionsEnabled)
    {
        // Optional: Initialize JS interop only if pan/zoom/crosshair needed
        await JSRuntime.InvokeVoidAsync("RioCharts.initializeInteractions", _elementRef, DotNetObjectReference.Create(this));
    }
}

public override async ValueTask DisposeAsync()
{
    // Clean up JS event listeners (if used)
    if (_advancedInteractionsEnabled)
    {
        await JSRuntime.InvokeVoidAsync("RioCharts.dispose", _elementRef);
    }
}
```

### Rendering Strategy
- Component renders SVG markup directly in Blazor (no JSInterop for rendering)
- Use @key directive on series elements for efficient diffing
- Leverage Blazor's incremental DOM updates for series changes
- Apply CSS classes for hover/selection states
- Use ElementReference + JS interop only for advanced interactions (optional)

### Two-Way Binding Support
```csharp
// Optional: support @bind-SelectedSeries
[Parameter] public ISeries? SelectedSeries { get; set; }
[Parameter] public EventCallback<ISeries?> SelectedSeriesChanged { get; set; }

// Optional: support @bind-XAxisRange for zoom/pan
[Parameter] public AxisRange? XAxisRange { get; set; }
[Parameter] public EventCallback<AxisRange?> XAxisRangeChanged { get; set; }
```

### JavaScript Interop (Optional)

**When JS Interop is NOT needed:**
- Basic charts with click interactions only (@onclick in Blazor)
- Static charts (no interactions)
- Simple selection/highlighting via Blazor state
- CSS-only tooltips (using :hover)
- Click-to-zoom or button-based zoom controls

**When JS Interop IS beneficial:**
- Real-time hover effects (crosshair, tooltip following mouse)
- Smooth pan/zoom via mouse drag or wheel
- High-frequency interactions (would cause excessive SignalR traffic in Blazor Server)
- Touch gestures (pinch-to-zoom)
- Performance-critical animations

**Pure Blazor Approach (No JS):**
```razor
<svg @onclick="HandleClick" @onmousemove="HandleMouseMove">
    <!-- SVG content -->
</svg>

@code {
    private void HandleClick(MouseEventArgs e)
    {
        // Handle clicks purely in Blazor
        // Works fine for basic interactions
    }

    private void HandleMouseMove(MouseEventArgs e)
    {
        // Avoid this in Blazor Server - too many SignalR round-trips!
        // OK in WASM for simple scenarios
    }
}
```

**JS Interop Approach (Advanced Interactions):**
File: RioCharts.js
```javascript
window.RioCharts = {
    initializeInteractions: function(element, dotNetHelper) {
        element.addEventListener('mousemove', (e) => {
            // Handle high-frequency events in JS
            // Only callback to Blazor when needed (debounced)
            updateCrosshair(e); // Pure JS/CSS, no Blazor callback
        });

        element.addEventListener('click', (e) => {
            // Callback to Blazor for important events
            dotNetHelper.invokeMethodAsync('OnChartClick', {
                x: e.offsetX,
                y: e.offsetY
            });
        });
    },
    dispose: function(element) {
        // Remove event listeners
    }
};
```

**Recommendation:**
- Start with pure Blazor approach (no JS interop)
- Add JS interop only if you need:
  - Pan/zoom functionality
  - Real-time crosshair
  - Smooth tooltip following mouse
  - Touch gestures
- For Blazor Server: minimize callbacks to avoid latency
- For Blazor WASM: more flexibility, but still use JS for 60fps interactions

### CSS Isolation
File: RioChart.razor.css
```css
/* Scoped styles for the component */
.chart-container { position: relative; }
.chart-svg { display: block; }
.chart-tooltip { position: absolute; pointer-events: none; }
.series-hover { stroke-width: 3px; filter: brightness(1.2); }
.series-selected { stroke-width: 4px; }
```

### Performance Optimization
- Use RenderFragment for custom legend/tooltip templates (advanced scenarios)
- Implement virtualization for extremely large datasets (render visible range only)
- Debounce parameter changes to avoid excessive re-renders
- Use @key on series to help Blazor's diffing algorithm
- Consider StaticRenderFragment for static axes/grid (Blazor 8+)

### Usage Example
```razor
@page "/chart-demo"

<RioChart Width="1000" 
          Height="500"
          Title="Sales Data"
          Series="@series"
          OnSeriesClick="@(args => selectedPoint = $"{args.Series.Name}: {args.Y}")" />

<p>Selected: @selectedPoint</p>

@code {
    private List<ISeries> series = new();
    private string selectedPoint = "None";

    protected override void OnInitialized()
    {
        series.Add(new XYSeries
        {
            Name = "Revenue",
            StrokeColor = "blue",
            Points = GenerateData()
        });
    }
}
```

### Considerations for Blazor Server vs. WebAssembly
- Server: Minimize JS interop calls to reduce latency
- Server: Debounce hover events to avoid excessive SignalR traffic
- WASM: Can handle more frequent updates without network overhead
- WASM: Consider rendering large charts client-side for better performance
