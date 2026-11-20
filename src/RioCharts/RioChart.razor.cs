using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using RioCharts.Layout;
using RioCharts.Rendering;

namespace RioCharts;

/// <summary>
/// Renders a chart as SVG. Layout is computed in C# (<see cref="ChartLayoutEngine"/>) and
/// drawn directly in Razor; interactions use plain Blazor events.
/// </summary>
public partial class RioChart : IAsyncDisposable
{
    private readonly string _id = "rio" + Guid.NewGuid().ToString("N")[..8];

    // Zoom/pan state: _zoom is the visible X range (null = full range). _lastParamRange tracks the
    // XAxisRange parameter so a parent that does not bind it does not reset the zoom on re-render.
    private AxisRange? _zoom;
    private AxisRange? _lastParamRange;

    // JS interop (only loaded when pan/zoom or crosshair is enabled).
    private ElementReference _svg;
    private IJSObjectReference? _module;
    private DotNetObjectReference<RioChart>? _dotnet;
    private string? _lastJsOptions;
    private bool _forceJsUpdate;
    private bool _disposed;

    [Inject] private IJSRuntime JS { get; set; } = null!;
    private ChartLayout _layout = null!;
    private Chart _chart = null!;
    private string _summary = "";

    // Interaction state.
    private HoverTarget? _hover;
    private HoverTarget? _focus;

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

    /// <summary>Colors for series without an explicit color. Defaults to <see cref="ChartPalette.Default"/>.</summary>
    [Parameter] public IReadOnlyList<string>? Palette { get; set; }

    /// <summary>XY series with more points than this are downsampled (LTTB). 0 disables.</summary>
    [Parameter] public int DownsampleThreshold { get; set; } = 1000;

    /// <summary>Extra CSS class on the outer container.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>
    /// Drag to pan and wheel/pinch to zoom the X axis; double-click resets. Keyboard: + and -
    /// zoom, Shift+arrows pan, 0 resets. Uses a small JS module.
    /// </summary>
    [Parameter] public bool EnablePanZoom { get; set; }

    /// <summary>Show crosshair lines with axis values following the mouse. Uses a small JS module.</summary>
    [Parameter] public bool EnableCrosshair { get; set; }

    // Event callbacks.
    [Parameter] public EventCallback<ChartEventArgs> OnSeriesClick { get; set; }
    [Parameter] public EventCallback<ChartEventArgs> OnSeriesHover { get; set; }
    [Parameter] public EventCallback<ChartEventArgs> OnSeriesSelected { get; set; }
    [Parameter] public EventCallback<AxisRangeChangedArgs> OnAxisRangeChanged { get; set; }
    [Parameter] public EventCallback<ISeries> OnLegendItemClick { get; set; }

    // Two-way bindings.
    [Parameter] public ISeries? SelectedSeries { get; set; }
    [Parameter] public EventCallback<ISeries?> SelectedSeriesChanged { get; set; }

    /// <summary>Visible X range; overrides the X axis range when set (zoom/pan).</summary>
    [Parameter] public AxisRange? XAxisRange { get; set; }
    [Parameter] public EventCallback<AxisRange?> XAxisRangeChanged { get; set; }

    /// <summary>The most recent layout (useful for tests and diagnostics).</summary>
    public ChartLayout CurrentLayout => _layout;

    protected override void OnParametersSet()
    {
        if (!Equals(XAxisRange, _lastParamRange))
        {
            _zoom = XAxisRange;
            _lastParamRange = XAxisRange;
        }

        if (SelectedSeriesChanged.HasDelegate || SelectedSeries is not null)
        {
            foreach (var s in Series ?? [])
            {
                s.IsSelected = ReferenceEquals(s, SelectedSeries);
            }
        }

        Recompute();
    }

    /// <summary>
    /// Recomputes the layout and re-renders. Call after changing series data in place
    /// (for example appending points) without changing component parameters.
    /// </summary>
    public void Refresh()
    {
        Recompute();
        StateHasChanged();
    }

    private void Recompute()
    {
        _chart = new Chart
        {
            Title = Title,
            Width = Width,
            Height = Height,
            Margin = Margin ?? new ChartMargin(string.IsNullOrEmpty(Title) ? 30 : 60, 40, 60, 80),
            BackgroundColor = BackgroundColor,
            XAxis = XAxis,
            YAxes = YAxes ?? [],
            Legend = Legend ?? new LegendSettings(),
            Series = Series ?? [],
            Tooltip = Tooltip ?? new TooltipSettings(),
            EnableInteractions = EnableInteractions,
            EnableAnimations = EnableAnimations,
            Palette = Palette ?? ChartPalette.Default,
            DownsampleThreshold = DownsampleThreshold,
        };

        _layout = ChartLayoutEngine.Compute(_chart, _zoom);
        _summary = ChartSummary.Describe(_layout);

        // Drop hover/focus that no longer points at something drawable.
        if (_hover is not null && !IsValid(_hover))
        {
            _hover = null;
        }

        if (_focus is not null && !IsValid(_focus))
        {
            _focus = null;
        }
    }

    // ---------- Hover / click on points and slices ----------

    private sealed record HoverTarget(int SeriesIndex, int ScreenIndex, bool IsSlice);

    private bool IsValid(HoverTarget target)
    {
        var layout = FindLayout(target.SeriesIndex);
        if (layout is null || !layout.Series.Visible)
        {
            return false;
        }

        return target.IsSlice
            ? target.ScreenIndex < (layout.Slices?.Count ?? 0)
            : target.ScreenIndex < layout.ScreenPoints.Count;
    }

    private SeriesLayout? FindLayout(int seriesIndex) => _layout.Series.FirstOrDefault(s => s.SeriesIndex == seriesIndex);

    private async Task PointEnter(SeriesLayout s, int screenIndex, bool isSlice)
    {
        if (!EnableInteractions)
        {
            return;
        }

        _hover = new HoverTarget(s.SeriesIndex, screenIndex, isSlice);
        await OnSeriesHover.InvokeAsync(EventArgsFor(s, screenIndex, isSlice, null));
    }

    private void PointLeave()
    {
        _hover = null;
    }

    private async Task PointClick(SeriesLayout s, int screenIndex, bool isSlice, MouseEventArgs e)
    {
        if (!EnableInteractions)
        {
            return;
        }

        var args = EventArgsFor(s, screenIndex, isSlice, e);
        await OnSeriesClick.InvokeAsync(args);
        await ToggleSelection(s.Series, args);
    }

    private async Task ToggleSelection(ISeries series, ChartEventArgs args)
    {
        var select = !series.IsSelected;
        foreach (var other in _chart.Series)
        {
            other.IsSelected = false;
        }

        series.IsSelected = select;
        var selected = select ? series : null;
        SelectedSeries = selected;
        await SelectedSeriesChanged.InvokeAsync(selected);
        await OnSeriesSelected.InvokeAsync(args);
    }

    private async Task ClearSelection()
    {
        if (_chart.Series.Any(s => s.IsSelected))
        {
            foreach (var s in _chart.Series)
            {
                s.IsSelected = false;
            }

            SelectedSeries = null;
            await SelectedSeriesChanged.InvokeAsync(null);
            await OnSeriesSelected.InvokeAsync(new ChartEventArgs());
        }
    }

    private static ChartEventArgs EventArgsFor(SeriesLayout s, int screenIndex, bool isSlice, MouseEventArgs? e)
    {
        var button = e is null ? MouseButton.None : (MouseButton)Math.Clamp((int)e.Button, -1, 2);
        if (isSlice && s.Slices is { } slices && screenIndex < slices.Count)
        {
            var slice = slices[screenIndex];
            return new ChartEventArgs { Series = s.Series, PointIndex = slice.PointIndex, X = slice.PointIndex, Y = slice.Point.Value, Button = button };
        }

        var pointIndex = screenIndex < s.PointIndices.Count ? s.PointIndices[screenIndex] : -1;
        var (x, y) = s.Series switch
        {
            XYSeries xy when pointIndex >= 0 => (xy.Points[pointIndex].X, xy.Points[pointIndex].Y),
            XYZSeries xyz when pointIndex >= 0 => (xyz.Points[pointIndex].X, xyz.Points[pointIndex].Y),
            _ => (0d, 0d),
        };
        return new ChartEventArgs { Series = s.Series, PointIndex = pointIndex, X = x, Y = y, Button = button };
    }

    // ---------- Legend ----------

    private async Task LegendClick(LegendItemLayout item)
    {
        if (!EnableInteractions)
        {
            return;
        }

        if (item.Point is not null)
        {
            // Slices with the same label share one legend item; toggle them together.
            var visible = !item.Point.Visible;
            foreach (var point in _chart.Series.OfType<ValueSeries>().SelectMany(v => v.Points))
            {
                if (point is not null && string.Equals(point.Label, item.Point.Label, StringComparison.Ordinal))
                {
                    point.Visible = visible;
                }
            }
        }
        else
        {
            item.Series.Visible = !item.Series.Visible;
        }

        Recompute();
        await OnLegendItemClick.InvokeAsync(item.Series);
    }

    private async Task LegendKeyDown(LegendItemLayout item, KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or " " or "Spacebar")
        {
            await LegendClick(item);
        }
    }

    // ---------- Zoom and pan ----------

    /// <summary>True when the X axis shows less than the full data range.</summary>
    public bool IsZoomed => _layout.XAxis?.IsZoomed == true;

    /// <summary>Shows the given X range (clamped to the data); null shows everything.</summary>
    public Task ZoomTo(AxisRange? range) => SetXRange(range);

    /// <summary>Shows the full X range.</summary>
    public Task ResetZoom() => SetXRange(null);

    /// <summary>Zooms the X axis around the center of the visible range. Factor above 1 zooms in.</summary>
    public Task ZoomBy(double factor)
    {
        if (_layout.XAxis is not { } x)
        {
            return Task.CompletedTask;
        }

        var current = new AxisRange(x.Min, x.Max);
        return SetXRange(ZoomMath.Zoom(current, FullXRange(x), factor, (x.Min + x.Max) / 2));
    }

    /// <summary>Pans the X axis by a fraction of the visible span (negative = left).</summary>
    public Task PanBy(double fraction)
    {
        if (_layout.XAxis is not { } x)
        {
            return Task.CompletedTask;
        }

        var current = new AxisRange(x.Min, x.Max);
        return SetXRange(ZoomMath.Pan(current, FullXRange(x), (x.Max - x.Min) * fraction));
    }

    private static AxisRange FullXRange(AxisLayout x) => new(x.FullMin, x.FullMax);

    private async Task SetXRange(AxisRange? range)
    {
        if (_layout.XAxis is not { } x)
        {
            return;
        }

        var full = FullXRange(x);
        if (range is not null)
        {
            range = ZoomMath.Clamp(range, full);
            if (ZoomMath.IsFull(range, full))
            {
                range = null;
            }
        }

        _forceJsUpdate = true;
        if (Equals(range, _zoom))
        {
            StateHasChanged();
            return;
        }

        var (oldMin, oldMax) = (x.Min, x.Max);
        _zoom = range;
        Recompute();
        StateHasChanged();

        var newX = _layout.XAxis!;
        await XAxisRangeChanged.InvokeAsync(range);
        await OnAxisRangeChanged.InvokeAsync(new AxisRangeChangedArgs(newX.Axis, oldMin, oldMax, newX.Min, newX.Max));
    }

    /// <summary>Called from JS when a pan, wheel or pinch gesture ends.</summary>
    [JSInvokable]
    public Task OnJsRangeChanged(double min, double max) => SetXRange(new AxisRange(min, max));

    /// <summary>Called from JS on double-click inside the plot.</summary>
    [JSInvokable]
    public Task OnJsResetZoom() => SetXRange(null);

    private bool NeedsJs => (EnablePanZoom || EnableCrosshair) && EnableInteractions && _layout.HasCartesian;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed || (_module is null && !NeedsJs))
        {
            return;
        }

        var options = JsOptions();
        var json = System.Text.Json.JsonSerializer.Serialize(options);
        try
        {
            if (_module is null)
            {
                _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/RioCharts/RioChart.razor.js");
                _dotnet = DotNetObjectReference.Create(this);
                await _module.InvokeVoidAsync("init", _svg, _dotnet, options);
            }
            else if (_forceJsUpdate || json != _lastJsOptions)
            {
                await _module.InvokeVoidAsync("update", _svg, options);
            }

            _lastJsOptions = json;
            _forceJsUpdate = false;
        }
        catch (JSDisconnectedException)
        {
            // Circuit gone; nothing to update.
        }
        catch (TaskCanceledException)
        {
        }
    }

    private object JsOptions()
    {
        var plot = _layout.Plot;
        var x = _layout.XAxis;
        var y = _layout.YAxes.Count > 0 ? _layout.YAxes[0] : null;
        return new
        {
            panZoom = EnablePanZoom && EnableInteractions && _layout.HasCartesian,
            crosshair = EnableCrosshair && EnableInteractions && _layout.HasCartesian,
            locale = CultureInfo.CurrentCulture.Name,
            plot = new { left = plot.Left, top = plot.Top, width = plot.Width, height = plot.Height },
            x = x is null ? null : JsAxis(x),
            y = y is null ? null : JsAxis(y),
        };
    }

    private static object JsAxis(AxisLayout axis)
    {
        var isDate = AxisLabelFormatter.IsDateFormat(axis.Axis);

        // Crosshair labels: one decimal finer than the ticks; date axes show time below one-day steps.
        var decimals = isDate ? (axis.TickStep < 1 ? 1 : 0) : Math.Min(10, AxisLabelFormatter.Decimals(axis.TickStep) + 1);
        return new { min = axis.Min, max = axis.Max, fullMin = axis.FullMin, fullMax = axis.FullMax, isDate, decimals };
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("dispose", _svg);
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
            catch (TaskCanceledException)
            {
            }
        }

        _dotnet?.Dispose();
        GC.SuppressFinalize(this);
    }

    // ---------- Keyboard navigation ----------

    private List<SeriesLayout> NavigableSeries() =>
        _layout.Series
            .Where(s => s.Series.Visible && !s.IsEmpty)
            .OrderBy(s => s.SeriesIndex)
            .ToList();

    private static int ItemCount(SeriesLayout s) => s.Slices?.Count ?? s.ScreenPoints.Count;

    private async Task SvgKeyDown(KeyboardEventArgs e)
    {
        if (!EnableInteractions)
        {
            return;
        }

        if (e.Key == "Escape")
        {
            _focus = null;
            _hover = null;
            await ClearSelection();
            return;
        }

        if (EnablePanZoom && _layout.HasCartesian)
        {
            switch (e.Key)
            {
                case "+" or "=":
                    await ZoomBy(1.5);
                    return;
                case "-" or "_":
                    await ZoomBy(1 / 1.5);
                    return;
                case "0" or "Home":
                    await ResetZoom();
                    return;
                case "ArrowLeft" when e.ShiftKey:
                    await PanBy(-0.1);
                    return;
                case "ArrowRight" when e.ShiftKey:
                    await PanBy(0.1);
                    return;
            }
        }

        var series = NavigableSeries();
        if (series.Count == 0)
        {
            return;
        }

        var current = _focus is null ? -1 : series.FindIndex(s => s.SeriesIndex == _focus.SeriesIndex);
        if (current < 0)
        {
            current = Math.Max(0, series.FindIndex(s => s.Series.IsSelected));
            if (e.Key is "ArrowRight" or "ArrowLeft" or "ArrowUp" or "ArrowDown")
            {
                SetFocus(series[current], 0);
                return;
            }
        }

        var layout = series[current];
        var index = _focus?.ScreenIndex ?? 0;
        switch (e.Key)
        {
            case "ArrowRight":
                SetFocus(layout, Math.Min(index + 1, ItemCount(layout) - 1));
                break;
            case "ArrowLeft":
                SetFocus(layout, Math.Max(index - 1, 0));
                break;
            case "ArrowDown":
                var next = series[(current + 1) % series.Count];
                SetFocus(next, Math.Min(index, ItemCount(next) - 1));
                break;
            case "ArrowUp":
                var previous = series[(current - 1 + series.Count) % series.Count];
                SetFocus(previous, Math.Min(index, ItemCount(previous) - 1));
                break;
            case "Enter" or " " or "Spacebar" when _focus is not null:
                var args = EventArgsFor(layout, _focus.ScreenIndex, _focus.IsSlice, null);
                await OnSeriesClick.InvokeAsync(args);
                await ToggleSelection(layout.Series, args);
                break;
        }
    }

    private void SetFocus(SeriesLayout layout, int index)
    {
        _focus = new HoverTarget(layout.SeriesIndex, Math.Max(0, index), layout.Slices is not null);
    }

    // ---------- Tooltip ----------

    private sealed record TooltipContent(double X, double Y, string Text, string Color);

    private TooltipContent? CurrentTooltip()
    {
        var target = _hover ?? _focus;
        if (target is null || !_chart.Tooltip.Enabled || !EnableInteractions)
        {
            return null;
        }

        var s = FindLayout(target.SeriesIndex);
        if (s is null)
        {
            return null;
        }

        var culture = CultureInfo.CurrentCulture;
        var format = _chart.Tooltip.CustomFormat;

        if (target.IsSlice)
        {
            if (s.Slices is not { } slices || target.ScreenIndex >= slices.Count)
            {
                return null;
            }

            var slice = slices[target.ScreenIndex];
            var text = !string.IsNullOrEmpty(format)
                ? SafeFormat(culture, format, s.Series.Name, slice.Point.Value, slice.Point.Label, slice.Fraction)
                : string.Format(culture, "{0}: {1:N2} ({2:P1})", slice.Point.Label, slice.Point.Value, slice.Fraction);
            return new TooltipContent(slice.LabelPosition.X, slice.LabelPosition.Y, text, slice.Color);
        }

        if (target.ScreenIndex >= s.ScreenPoints.Count || target.ScreenIndex >= s.PointIndices.Count)
        {
            return null;
        }

        var pos = s.ScreenPoints[target.ScreenIndex];
        var pointIndex = s.PointIndices[target.ScreenIndex];
        double x, y;
        double? z = null;
        switch (s.Series)
        {
            case XYSeries xy:
                (x, y) = (xy.Points[pointIndex].X, xy.Points[pointIndex].Y);
                break;
            case XYZSeries xyz:
                (x, y, z) = (xyz.Points[pointIndex].X, xyz.Points[pointIndex].Y, xyz.Points[pointIndex].Z);
                break;
            default:
                return null;
        }

        string content;
        if (!string.IsNullOrEmpty(format))
        {
            content = SafeFormat(culture, format, s.Series.Name, y, x, z ?? 0);
        }
        else
        {
            var xs = FormatValue(s.LayoutXAxis, x);
            var ys = FormatValue(s.LayoutYAxis, y);
            var xName = s.LayoutXAxis?.Axis.Title ?? "X";
            var yName = s.LayoutYAxis?.Axis.Title ?? "Y";
            content = $"{s.Series.Name}\n{xName}: {xs}\n{yName}: {ys}";
            if (z is { } zv)
            {
                content += "\n" + string.Format(culture, "Z: {0:N2}", zv);
            }
        }

        return new TooltipContent(pos.X, pos.Y, content, s.Color);
    }

    /// <summary>Tooltip value: whole numbers without decimals, others two decimals finer than the ticks.</summary>
    private static string FormatValue(AxisLayout? axis, double value)
    {
        var step = Math.Abs(value - Math.Round(value)) < 1e-9 ? 1 : (axis?.TickStep ?? 1) / 100;
        return AxisLabelFormatter.Format(axis?.Axis, value, step);
    }

    private static string SafeFormat(CultureInfo culture, string format, params object[] args)
    {
        try
        {
            return string.Format(culture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    // ---------- Rendering helpers ----------

    private string ClipId => _id + "-clip";

    private static string N(double v) => Svg.N(v);

    private string ContainerStyle => $"width:{N(Width)}px;";

    private string SvgClass
    {
        get
        {
            var classes = "rio-chart-svg";
            if (EnableAnimations)
            {
                classes += " rio-animate";
            }

            if (_chart.Series.Any(s => s.IsSelected && s.Visible))
            {
                classes += " rio-has-selection";
            }

            if (EnablePanZoom && EnableInteractions && _layout.HasCartesian)
            {
                classes += " rio-pannable";
            }

            return classes;
        }
    }

    private bool IsHovered(SeriesLayout s) => _hover?.SeriesIndex == s.SeriesIndex;

    private string SeriesClass(SeriesLayout s)
    {
        var c = "rio-series";
        if (IsHovered(s))
        {
            c += " rio-series-hover";
        }

        if (s.Series.IsSelected)
        {
            c += " rio-series-selected";
        }

        return c;
    }

    private double LineWidth(XYSeries xy, SeriesLayout s) =>
        xy.StrokeWidth + (xy.IsSelected ? 2 : IsHovered(s) ? 1 : 0);

    private static string FillFor(XYSeries xy, SeriesLayout s) =>
        string.Equals(xy.FillColor, "auto", StringComparison.OrdinalIgnoreCase) ? s.Color : xy.FillColor!;

    private static string SeriesAriaLabel(SeriesLayout s)
    {
        var count = s.Series switch
        {
            XYSeries xy => xy.Points.Count,
            XYZSeries xyz => xyz.Points.Count,
            ValueSeries v => v.Points.Count,
            _ => 0,
        };
        var name = string.IsNullOrWhiteSpace(s.Series.Name) ? "Series" : s.Series.Name;
        return string.Create(CultureInfo.InvariantCulture, $"{name}: {count} data points");
    }

    /// <summary>Invisible hit targets are only drawn when interactions are on and the count is reasonable.</summary>
    private bool DrawHitTargets(SeriesLayout s) => EnableInteractions && s.ScreenPoints.Count <= 2000;

    private static double HitRadius(XYSeries xy) => Math.Max(xy.Marker == MarkerStyle.None ? 0 : xy.MarkerSize / 2 + 2, 7);

    private ScreenPoint? FocusPoint()
    {
        if (_focus is null || _focus.IsSlice)
        {
            return null;
        }

        var s = FindLayout(_focus.SeriesIndex);
        return s is not null && _focus.ScreenIndex < s.ScreenPoints.Count ? s.ScreenPoints[_focus.ScreenIndex] : null;
    }

    private double XLabelExtent()
    {
        var x = _layout.XAxis;
        if (x is null)
        {
            return 0;
        }

        var size = x.Axis.LabelFontSize;
        if (x.Axis.LabelRotation == 0)
        {
            return size;
        }

        var longest = x.Ticks.Select(t => ChartLayoutEngine.EstimateTextWidth(x.FormatLabel(t), size)).DefaultIfEmpty(0).Max();
        var radians = Math.Abs(x.Axis.LabelRotation) * Math.PI / 180;
        return longest * Math.Sin(radians) + size * Math.Cos(radians);
    }

    private static double YLabelExtent(AxisLayout axis) =>
        axis.Ticks.Select(t => ChartLayoutEngine.EstimateTextWidth(axis.FormatLabel(t), axis.Axis.LabelFontSize)).DefaultIfEmpty(0).Max();

    private string TooltipStyle(TooltipContent t)
    {
        var tip = _chart.Tooltip;
        var left = t.X / Width * 100;
        var top = t.Y / Height * 100;
        var below = t.Y < Height * 0.25;
        var transform = below ? "translate(-50%, 14px)" : "translate(-50%, calc(-100% - 14px))";
        return string.Create(CultureInfo.InvariantCulture,
            $"left:{left:0.###}%;top:{top:0.###}%;transform:{transform};background:{tip.BackgroundColor};color:{tip.TextColor};" +
            $"font-size:{tip.FontSize}px;padding:{tip.Padding}px;border-radius:{tip.BorderRadius}px;border-left:3px solid {t.Color};");
    }
}
