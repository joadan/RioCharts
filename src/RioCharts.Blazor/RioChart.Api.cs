using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scales;

namespace RioCharts.Blazor;

// The rest of the code API: series visibility and highlight, coordinates, and batched changes.
public partial class RioChart<TItem>
{
    private string? _highlighted;
    private string? _sentHighlight;
    private int _batch;

    /// <summary>
    /// Raised before a legend click shows or hides a series; set <c>Cancel</c> to keep it as it is.
    /// <see cref="OnLegendClick"/> follows only when the series was toggled.
    /// </summary>
    [Parameter] public EventCallback<LegendClickingEventArgs> OnLegendClicking { get; set; }

    // ---------- Treemap drill-down ----------

    private string? _treemapPath;
    private string? _lastTreemapPathParameter;

    /// <summary>
    /// Treemaps: the group shown (drill-down), as a path with the series' separator, e.g.
    /// <c>"Asia"</c> or <c>"Asia/India"</c>; null shows the whole tree. Two-way bindable with
    /// <c>@bind-TreemapPath</c>, e.g. to keep it in the page's URL.
    /// </summary>
    [Parameter] public string? TreemapPath { get; set; }

    /// <summary>Raised when the group shown changes (two-way binding of <see cref="TreemapPath"/>).</summary>
    [Parameter] public EventCallback<string?> TreemapPathChanged { get; set; }

    /// <summary>
    /// Treemaps: raised when the shown group changes (a click on a group or the path bar, or
    /// <see cref="DrillDownAsync"/>), with its path (e.g. "Asia/India"); null for the whole tree.
    /// </summary>
    [Parameter] public EventCallback<string?> OnDrillDown { get; set; }

    /// <summary>Treemaps: the group shown, as a path with the series' separator; null when the whole tree is shown (or the path is not there).</summary>
    public string? GetTreemapPath() => _layout is { } layout && !_dirty ? layout.TreemapPath : _treemapPath;

    // TreemapPath set by the parent: shown on the next layout.
    private void TreemapParametersSet()
    {
        if (TreemapPath != _lastTreemapPathParameter)
        {
            _lastTreemapPathParameter = TreemapPath;
            _treemapPath = string.IsNullOrWhiteSpace(TreemapPath) ? null : TreemapPath;
            _dirty = true;
        }
    }

    /// <summary>
    /// Treemaps: shows a group (drill-down), e.g. <c>"Asia"</c> or <c>"Asia/India"</c> (the series'
    /// path separator), as clicking it does; null or "" shows the whole tree. A path that is not
    /// there shows the whole tree.
    /// </summary>
    public async Task DrillDownAsync(string? path)
    {
        path = string.IsNullOrWhiteSpace(path) ? null : path;
        if (path == _treemapPath)
        {
            return;
        }

        _treemapPath = path;
        _dirty = true;
        if (TreemapPathChanged.HasDelegate)
        {
            // The parent will pass this path back as TreemapPath; it matches the current state.
            _lastTreemapPathParameter = path;
            await TreemapPathChanged.InvokeAsync(path);
        }

        StateHasChanged();
        await OnDrillDown.InvokeAsync(path);
    }

    // ---------- Series ----------

    /// <summary>Whether a series is shown (not hidden through the legend, <see cref="HiddenSeries"/> or code).</summary>
    public bool IsSeriesVisible(string seriesId) => !_hidden.Contains(seriesId);

    /// <summary>Shows a hidden series (raises <c>HiddenSeriesChanged</c>).</summary>
    public Task ShowSeriesAsync(string seriesId) => SetSeriesVisibleAsync(seriesId, true);

    /// <summary>Hides a series; it keeps its color and legend item (raises <c>HiddenSeriesChanged</c>).</summary>
    public Task HideSeriesAsync(string seriesId) => SetSeriesVisibleAsync(seriesId, false);

    /// <summary>Shows a hidden series or hides a shown one, as a legend click does (without the legend events).</summary>
    public Task ToggleSeriesAsync(string seriesId) => SetSeriesVisibleAsync(seriesId, !IsSeriesVisible(seriesId));

    private async Task SetSeriesVisibleAsync(string seriesId, bool visible)
    {
        if (string.IsNullOrEmpty(seriesId) || visible == IsSeriesVisible(seriesId))
        {
            return;
        }

        if (visible)
        {
            _hidden.Remove(seriesId);
        }
        else
        {
            _hidden.Add(seriesId);
        }

        _dirty = true;
        var hidden = _hidden.ToList();
        if (HiddenSeriesChanged.HasDelegate)
        {
            // The parent will pass this list back as HiddenSeries; it matches the current state.
            _lastHiddenParameter = hidden;
            await HiddenSeriesChanged.InvokeAsync(hidden);
        }

        StateHasChanged();
    }

    /// <summary>A legend click: OnLegendClicking (may cancel), then toggle and OnLegendClick.</summary>
    private async Task LegendClickedAsync(string seriesId)
    {
        var visible = !IsSeriesVisible(seriesId);
        var name = _series.FirstOrDefault(s => s.Spec.Id == seriesId)?.Spec.DisplayName ?? seriesId;
        if (OnLegendClicking.HasDelegate)
        {
            var args = new LegendClickingEventArgs { SeriesId = seriesId, Name = name, Visible = visible };
            await OnLegendClicking.InvokeAsync(args);
            if (args.Cancel)
            {
                return;
            }
        }

        await SetSeriesVisibleAsync(seriesId, visible);
        await OnLegendClick.InvokeAsync(new LegendClickEventArgs(seriesId, name, visible));
    }

    /// <summary>
    /// Fades every series but this one (and their data labels), e.g. while a matching table row is
    /// hovered; null shows all again. Hovering or focusing a legend item does the same for its
    /// series meanwhile. SVG series layer; needs JS (interactive rendering).
    /// </summary>
    public Task HighlightSeriesAsync(string? seriesId)
    {
        _highlighted = string.IsNullOrEmpty(seriesId) ? null : seriesId;
        return SendHighlightAsync();
    }

    /// <summary>The series highlighted with <see cref="HighlightSeriesAsync"/>, or null.</summary>
    public string? GetHighlightedSeries() => _highlighted;

    private async Task SendHighlightAsync()
    {
        if (_module is null || _highlighted == _sentHighlight)
        {
            return; // sent after the next layout once the module is loaded
        }

        try
        {
            await _module.InvokeVoidAsync("highlight", _container, _highlighted);
            _sentHighlight = _highlighted;
        }
        catch (JSDisconnectedException)
        {
        }
    }

    // ---------- Coordinates ----------

    /// <summary>
    /// The chart position of a data value, e.g. to place your own element over the chart: x as a
    /// DateTime, number or category name, y on the given axis (default: the first). Positions
    /// outside the plot are returned too. Null before the first layout, for an x not on the axis,
    /// and for pies and horizontal bars.
    /// </summary>
    public ChartPoint? ValueToPixel(object x, double y, string? yAxis = null)
    {
        if (CartesianLayout() is not { } layout || YScaleOf(layout, yAxis) is not { } ys || !double.IsFinite(y) || Pixel(layout, x) is not { } px)
        {
            return null;
        }

        return new ChartPoint(px, ys.Map(y));
    }

    /// <summary>
    /// The data value at a chart position (CSS pixels from the chart's top left), e.g. from a mouse
    /// event: x as a DateTime, number or the nearest category's name, y on the given axis (default:
    /// the first). Null before the first layout, and for pies and horizontal bars.
    /// </summary>
    public ChartValue? PixelToValue(double x, double y, string? yAxis = null)
    {
        if (CartesianLayout() is not { } layout || YScaleOf(layout, yAxis) is not { } ys || !double.IsFinite(x) || !double.IsFinite(y))
        {
            return null;
        }

        object? value = null;
        if (layout.XKind == XKind.Category && layout.XBand is { Categories.Count: > 0 } band)
        {
            var nearest = 0;
            for (var i = 1; i < band.Categories.Count; i++)
            {
                if (Math.Abs(band.Center(i) - x) < Math.Abs(band.Center(nearest) - x))
                {
                    nearest = i;
                }
            }

            value = band.Categories[nearest];
        }
        else if (layout.XScale is { } scale)
        {
            var d = scale.Invert(x);
            // Each branch boxed on its own: a DateTime would otherwise widen to DateTimeOffset.
            value = layout.XKind != XKind.Time ? d
                : layout.Time is { } time ? (object)time.ToDateTimeOffset(d)
                : TimeValue.ToDateTime(d);
        }

        return value is null ? null : new ChartValue(value, ys.Invert(y));
    }

    private ChartLayoutResult? CartesianLayout() =>
        _layout is { } layout && layout.Tooltip.Slices.Count == 0 && !CurrentDefinition().Horizontal ? layout : null;

    private static ContinuousScale? YScaleOf(ChartLayoutResult layout, string? axis) =>
        axis is null ? layout.YScale : layout.YScales.TryGetValue(axis, out var scale) ? scale : null;

    // ---------- Batching ----------

    /// <summary>
    /// Runs several changes (zoom, selection, hidden series, …) and draws the chart once at the
    /// end instead of after each. Events and bound parameters are raised as the changes happen.
    /// Matters most on Blazor Server, where each render is a round trip.
    /// </summary>
    public async Task ApplyAsync(Func<Task> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        _batch++;
        try
        {
            await changes();
        }
        finally
        {
            _batch--;
        }

        if (_batch == 0)
        {
            StateHasChanged();
        }
    }

    /// <inheritdoc />
    protected override bool ShouldRender() => _batch == 0;
}
