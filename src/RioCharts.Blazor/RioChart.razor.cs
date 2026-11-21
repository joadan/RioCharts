using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using RioCharts.Blazor.Rendering;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Specs;
using RioCharts.Core.Text;

namespace RioCharts.Blazor;

/// <summary>
/// A chart. Series are added as child components:
/// <code>&lt;RioChart Items="readings"&gt;&lt;LineSeries XValue="r =&gt; r.Time" YValue="r =&gt; r.Temp" /&gt;&lt;/RioChart&gt;</code>
/// The chart fills its container's width and lays out again when the width changes.
/// </summary>
public partial class RioChart<TItem> : IChartContext
{
    private const double FallbackWidth = 640;

    private readonly string _id = "rio" + Guid.NewGuid().ToString("N")[..8];
    private readonly List<ISeriesSource> _series = [];
    private readonly List<IAnnotationSource> _annotations = [];
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private int _generatedIds;

    private ElementReference _container;
    private ChartLayoutResult? _layout;
    private bool _dirty = true;
    private object? _lastParameters;
    private IReadOnlyCollection<string>? _lastHiddenParameter;

    private double? _measuredWidth;
    private ITextMeasurer _measurer = ApproximateTextMeasurer.Instance;
    private IJSObjectReference? _module;
    private DotNetObjectReference<RioChart<TItem>>? _dotnet;
    private object? _sentInteraction;
    private (double Min, double Max)? _range;
    private ChartRange? _lastVisibleRangeParameter;
    private string? _viewerZone;
    private bool _disposed;
    private IReadOnlyDictionary<string, SeriesData> _layoutData = new Dictionary<string, SeriesData>();
    private (ChartLayoutResult Layout, TooltipModel Model)? _tooltip;
    private TooltipHost<TItem>? _tooltipHost;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    /// <summary>Items shared by all series that do not set their own.</summary>
    [Parameter] public IEnumerable<TItem>? Items { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Height in pixels. Default 300.</summary>
    [Parameter] public double Height { get; set; } = 300;

    /// <summary>Fixed width in pixels. Default: the container's width (responsive).</summary>
    [Parameter] public double? Width { get; set; }

    [Parameter] public string? Title { get; set; }

    [Parameter] public AxisSpec? XAxis { get; set; }

    /// <summary>The primary vertical axis (id "y" unless set).</summary>
    [Parameter] public AxisSpec? YAxis { get; set; }

    /// <summary>Additional vertical axes; series pick one with <c>YAxisId</c>.</summary>
    [Parameter] public IReadOnlyList<AxisSpec>? YAxes { get; set; }

    [Parameter] public LegendSpec? Legend { get; set; }

    /// <summary>Tooltip settings. On and shared across series by default.</summary>
    [Parameter] public TooltipSpec? Tooltip { get; set; }

    /// <summary>
    /// Text for the tooltip header from the hovered point, e.g. <c>p =&gt; $"Week {Iso(p.X)}"</c>;
    /// null keeps the default (the formatted x, or the series name for nested pies). Runs once per
    /// layout for the points that can be hovered, not on pointer moves.
    /// </summary>
    [Parameter] public Func<TooltipPoint<TItem>, string?>? TooltipHeader { get; set; }

    /// <summary>
    /// Custom tooltip content (Razor), rendered inside the tooltip card. The crosshair still follows
    /// the pointer in the browser; the content is rendered by .NET when the hovered x changes, so on
    /// Blazor Server it follows one network round trip behind. Prefer <see cref="TooltipHeader"/>
    /// and the series' <c>TooltipValue</c> when text is enough.
    /// </summary>
    [Parameter] public RenderFragment<TooltipContext<TItem>>? TooltipTemplate { get; set; }

    /// <summary>Annotations as data (in addition to &lt;ReferenceLine&gt; and &lt;ReferenceBand&gt; children).</summary>
    [Parameter] public IReadOnlyList<AnnotationSpec>? Annotations { get; set; }

    /// <summary>
    /// Display time zone: "UTC", "local" (the viewer's browser zone) or an IANA id such as
    /// "Europe/Stockholm". Default: none, times are shown as given. With a zone, times are plotted
    /// on instants (no doubling back at DST changes) and labelled in that zone.
    /// </summary>
    [Parameter] public string? TimeZone { get; set; }

    /// <summary>With a TimeZone: how DateTime values with Kind Unspecified are read (UTC or the display zone).</summary>
    [Parameter] public UnspecifiedTimes? UnspecifiedTimes { get; set; }

    /// <summary>Culture for labels. Default: the current culture.</summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>Ids of hidden series. Two-way bindable with <c>@bind-HiddenSeries</c>.</summary>
    [Parameter] public IReadOnlyCollection<string>? HiddenSeries { get; set; }

    [Parameter] public EventCallback<IReadOnlyCollection<string>> HiddenSeriesChanged { get; set; }

    /// <summary>Raised after a legend click has toggled a series.</summary>
    [Parameter] public EventCallback<LegendClickEventArgs> OnLegendClick { get; set; }

    /// <summary>Raised when a point or bar is clicked.</summary>
    [Parameter] public EventCallback<PointClickEventArgs<TItem>> OnPointClick { get; set; }

    /// <summary>Raised when a reference line or band is clicked (it becomes a focusable button).</summary>
    [Parameter] public EventCallback<AnnotationClickEventArgs> OnAnnotationClick { get; set; }

    /// <summary>
    /// Raised when the user has dragged a reference line (<c>Draggable</c>). For lines in
    /// <see cref="Annotations"/>, update the spec's value here; components use <c>@bind-Value</c>.
    /// </summary>
    [Parameter] public EventCallback<AnnotationChangedEventArgs> OnAnnotationChanged { get; set; }

    /// <summary>
    /// Zoom and pan on the x axis: drag to zoom in, Shift+drag to pan, Ctrl/⌘+wheel to zoom at the
    /// pointer, double-click or the reset button to zoom out. Not for category axes.
    /// </summary>
    [Parameter] public bool Zoomable { get; set; }

    /// <summary>The visible x range (null: all data). Two-way bindable with <c>@bind-VisibleRange</c>.</summary>
    [Parameter] public ChartRange? VisibleRange { get; set; }

    [Parameter] public EventCallback<ChartRange?> VisibleRangeChanged { get; set; }

    /// <summary>Raised before a zoom or pan is applied; the handler may change or cancel it.</summary>
    [Parameter] public EventCallback<ZoomChangingEventArgs> OnZoomChanging { get; set; }

    /// <summary>
    /// Charts with the same group name share the crosshair and (when zoomable) the zoom. They
    /// should have the same kind of x axis.
    /// </summary>
    [Parameter] public string? Group { get; set; }

    /// <summary>
    /// Draw the series as SVG (default) or on a canvas: no DOM per series, for large or many series.
    /// Axes, labels and annotations stay SVG (annotations then sit under the series).
    /// </summary>
    [Parameter] public SeriesLayer SeriesLayer { get; set; }

    /// <summary>Line and area series with more points than this are downsampled (LTTB). Default: twice the plot width, four times on a canvas.</summary>
    [Parameter] public int? DownsampleThreshold { get; set; }

    /// <summary>Live data (series with a <c>Buffer</c>): the shortest time between redraws, in milliseconds. Default 100.</summary>
    [Parameter] public int RefreshInterval { get; set; } = 100;

    /// <summary>
    /// Keyboard navigation (default on): the chart is a tab stop, and with it focused the arrow keys
    /// move between points (Up/Down: between series; pies: between slices), showing the tooltip and
    /// announcing the point to screen readers; Enter clicks it, Escape leaves. Handled in the browser.
    /// </summary>
    [Parameter] public bool KeyboardNavigation { get; set; } = true;

    /// <summary>Extra CSS class on the container.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>The latest layout: scene, scales, hit-test index and warnings.</summary>
    public ChartLayoutResult? Layout => _layout;

    /// <summary>
    /// A chart spec (e.g. from JSON or an AI agent) instead of child components: series read their
    /// x and y fields from <see cref="Datasets"/> or the spec's inline data. Layout options such as
    /// Height, Culture and HiddenSeries still come from the component.
    /// </summary>
    [Parameter] public ChartSpec? Spec { get; set; }

    /// <summary>Datasets that a <see cref="Spec"/> refers to, by name.</summary>
    [Parameter] public IReadOnlyDictionary<string, ChartDataset>? Datasets { get; set; }

    /// <summary>Problems binding <see cref="Spec"/> to the data (missing fields, wrong types); empty when fine.</summary>
    public IReadOnlyList<SpecError> SpecErrors => _bound?.Errors ?? [];

    /// <summary>The chart model: the <see cref="Spec"/> parameter, or one built from the parameters and child components (for saving, or as a starting point for edits).</summary>
    public ChartSpec GetSpec() => Spec ?? BuildSpec();

    private SpecBindResult? _bound;
    private (ChartSpec?, object?) _boundFrom;

    /// <summary>The spec being drawn: the bound <see cref="Spec"/>, or the one from parameters and child components.</summary>
    private ChartSpec CurrentSpec() => Spec is not null && _bound is not null ? _bound.Spec : BuildSpec();

    private string ContainerStyle => string.Create(CultureInfo.InvariantCulture,
        $"height:{Height:0.##}px;{(Width is { } w ? $"width:{w:0.##}px;" : "")}");

    // ---------- IChartContext ----------

    object? IChartContext.DefaultItems => Items;

    string IChartContext.Register(ISeriesSource series, string? requestedId)
    {
        if (!_series.Contains(series))
        {
            _series.Add(series);
        }

        var id = requestedId;
        if (string.IsNullOrEmpty(id) || _series.Any(s => !ReferenceEquals(s, series) && s.Spec.Id == id))
        {
            id = $"series-{++_generatedIds}";
        }

        Invalidate();
        return id;
    }

    void IChartContext.Unregister(ISeriesSource series)
    {
        if (!_disposed && _series.Remove(series))
        {
            Invalidate();
        }
    }

    void IChartContext.SeriesChanged(ISeriesSource series) => Invalidate();

    private int _refreshPending;

    /// <summary>Coalesces live updates: at most one layout per <see cref="RefreshInterval"/>.</summary>
    void IChartContext.DataArrived()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshPending, 1) == 1)
        {
            return;
        }

        _ = RefreshLaterAsync();
    }

    private async Task RefreshLaterAsync()
    {
        try
        {
            await Task.Delay(Math.Max(16, RefreshInterval));
            await InvokeAsync(() =>
            {
                Interlocked.Exchange(ref _refreshPending, 0);
                if (!_disposed)
                {
                    Invalidate();
                }
            });
        }
        catch (ObjectDisposedException)
        {
        }
    }

    void IChartContext.AnnotationChanged(IAnnotationSource annotation)
    {
        if (!_annotations.Contains(annotation))
        {
            _annotations.Add(annotation);
        }

        Invalidate();
    }

    void IChartContext.Unregister(IAnnotationSource annotation)
    {
        if (!_disposed && _annotations.Remove(annotation))
        {
            Invalidate();
        }
    }

    private void Invalidate()
    {
        _dirty = true;
        StateHasChanged();
    }

    // ---------- Parameters and layout ----------

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(HiddenSeries, _lastHiddenParameter))
        {
            _lastHiddenParameter = HiddenSeries;
            _hidden.Clear();
            if (HiddenSeries is not null)
            {
                _hidden.UnionWith(HiddenSeries);
            }

            _dirty = true;
        }

        // Re-layout only when the chart's own parameters changed (child components report their own changes).
        if (!ReferenceEquals(VisibleRange, _lastVisibleRangeParameter))
        {
            _lastVisibleRangeParameter = VisibleRange;
            _range = ToRange(VisibleRange);
            _dirty = true;
        }

        var parameters = (Height, Width, Title, XAxis, YAxis, YAxes, Legend, Tooltip, Annotations, Culture?.Name, OnAnnotationClick.HasDelegate, TimeZone, UnspecifiedTimes, SeriesLayer, DownsampleThreshold, Spec, Datasets,
            TooltipHeader is not null, TooltipTemplate is not null);
        if (!Equals(parameters, _lastParameters))
        {
            _lastParameters = parameters;
            _dirty = true;
        }
    }

    /// <summary>Lays out again, e.g. after changing data in place without a new reference or DataVersion.</summary>
    public void Refresh() => Invalidate();

    private ChartSpec BuildSpec() => new()
    {
        Title = Title,
        X = XAxis ?? new AxisSpec(),
        Y = YAxis ?? new AxisSpec(),
        YAxes = YAxes ?? [],
        Legend = Legend ?? new LegendSpec(),
        Tooltip = Tooltip ?? new TooltipSpec(),
        Series = _series.Select(s => s.Spec).ToList(),
        Annotations = (Annotations ?? []).Concat(_annotations.Select(a => a.Spec)).ToList(),
        TimeZone = TimeZone,
        UnspecifiedTimes = UnspecifiedTimes,
    };

    private ChartLayoutResult EnsureLayout()
    {
        if (_layout is not null && !_dirty)
        {
            return _layout;
        }

        IReadOnlyDictionary<string, SeriesData> data;
        if (Spec is not null)
        {
            // Bind once per spec/datasets pair; legend toggles and resizes reuse it.
            if (_bound is null || !Equals(_boundFrom, (Spec, (object?)Datasets)))
            {
                _bound = ChartSpecBinder.Bind(Spec, Datasets ?? new Dictionary<string, ChartDataset>());
                _boundFrom = (Spec, Datasets);
            }

            data = _bound.Data;
        }
        else
        {
            _bound = null;
            var collected = new Dictionary<string, SeriesData>(StringComparer.Ordinal);
            foreach (var s in _series)
            {
                collected.TryAdd(s.Spec.Id, s.Data);
            }

            data = collected;
        }

        _layoutData = data;
        _layout = ChartLayout.Compute(CurrentSpec(), data, new LayoutOptions
        {
            Width = Width ?? _measuredWidth ?? FallbackWidth,
            Height = Height,
            Measurer = _measurer,
            Culture = Culture ?? CultureInfo.CurrentCulture,
            HiddenSeries = _hidden,
            InteractiveAnnotations = OnAnnotationClick.HasDelegate,
            DownsampleThreshold = DownsampleThreshold ?? (SeriesLayer == SeriesLayer.Canvas ? Math.Max(2000, (int)((Width ?? _measuredWidth ?? FallbackWidth) * 4)) : null),
            XRange = _range,
            ViewerTimeZone = _viewerZone,
        });
        _dirty = false;
        return _layout;
    }

    // ---------- Legend and annotation clicks ----------

    private async Task HandleInteraction(NodeInteraction interaction)
    {
        if (interaction.Kind == "annotation")
        {
            var label = CurrentSpec().Annotations.Select((a, i) => (a, i)).FirstOrDefault(t => (t.a.Id ?? $"annotation-{t.i + 1}") == interaction.Id).a?.Label;
            await OnAnnotationClick.InvokeAsync(new AnnotationClickEventArgs(interaction.Id, label));
            return;
        }

        if (interaction.Kind != "legend")
        {
            return;
        }

        var id = interaction.Id;
        var visible = _hidden.Remove(id);
        if (!visible)
        {
            _hidden.Add(id);
        }

        _dirty = true;
        var hidden = _hidden.ToList();
        if (HiddenSeriesChanged.HasDelegate)
        {
            // The parent will pass this list back as HiddenSeries; it matches the current state.
            _lastHiddenParameter = hidden;
            await HiddenSeriesChanged.InvokeAsync(hidden);
        }

        var name = _series.FirstOrDefault(s => s.Spec.Id == id)?.Spec.DisplayName ?? id;
        await OnLegendClick.InvokeAsync(new LegendClickEventArgs(id, name, visible));
    }

    /// <summary>Called by JS when a draggable line is dropped (value in data units).</summary>
    [JSInvokable]
    public async Task OnAnnotationDragged(string id, double value)
    {
        var all = CurrentSpec().Annotations;
        var index = -1;
        for (var i = 0; i < all.Count && index < 0; i++)
        {
            if ((all[i].Id ?? $"annotation-{i + 1}") == id)
            {
                index = i;
            }
        }

        if (index < 0 || !double.IsFinite(value))
        {
            return;
        }

        var spec = all[index];
        var kind = spec.Axis == AnnotationAxis.X ? _layout?.XKind ?? XKind.Numeric : XKind.Numeric;
        object result = kind == XKind.Time && _layout?.Time is { } time ? time.ToDateTimeOffset(value) : AxisValues.FromAxis(value, kind);

        // Components bound with @bind-Value update themselves; the event is raised either way.
        var offset = (Annotations ?? []).Count;
        if (index >= offset && index - offset < _annotations.Count)
        {
            await _annotations[index - offset].DraggedAsync(result);
        }

        await OnAnnotationChanged.InvokeAsync(new AnnotationChangedEventArgs(id, spec.Label, result));
        _dirty = true;
        StateHasChanged();
    }

    /// <summary>Called by JS when a point or bar is clicked.</summary>
    [JSInvokable]
    public async Task OnPointClicked(string seriesId, int index)
    {
        // The data the layout was computed from: a live buffer may have moved on since.
        var series = SourceOf(seriesId);
        if (series is null || DataOf(seriesId) is not { } data || index < 0 || index >= data.Count)
        {
            return;
        }

        var source = data.ToSourceIndex(index);
        var xValue = TooltipBuilder.XValueOf(data, index, _layout?.Time);
        var inputs = series.InputIndexes(source);
        var items = inputs.Select(series.ItemAt).OfType<TItem>().ToList();
        var input = inputs.Count > 0 ? inputs[0] : source;
        await OnPointClick.InvokeAsync(new PointClickEventArgs<TItem>(seriesId, series.Spec.DisplayName, items.FirstOrDefault(), input, xValue, data.Y[index])
        {
            Items = items,
        });
    }

    // ---------- Custom tooltips ----------

    private ISeriesSource? SourceOf(string seriesId) => Spec is null ? _series.FirstOrDefault(s => s.Spec.Id == seriesId) : null;

    private SeriesData? DataOf(string seriesId) => _layoutData.TryGetValue(seriesId, out var data) ? data : null;

    /// <summary>The layout's tooltip model with TooltipHeader and the series' TooltipValue applied (once per layout).</summary>
    private TooltipModel CustomTooltip(ChartLayoutResult layout)
    {
        if (_tooltip is { } cached && ReferenceEquals(cached.Layout, layout))
        {
            return cached.Model;
        }

        var model = TooltipBuilder.Customize(layout.Tooltip, DataOf, SourceOf, layout.Time, TooltipHeader);
        _tooltip = (layout, model);
        return model;
    }

    private RenderFragment TooltipHostFragment => builder =>
    {
        builder.OpenComponent<TooltipHost<TItem>>(0);
        builder.AddComponentParameter(1, nameof(TooltipHost<TItem>.Template), TooltipTemplate);
        builder.AddComponentReferenceCapture(2, host => _tooltipHost = (TooltipHost<TItem>)host);
        builder.CloseComponent();
    };

    /// <summary>Called by JS when the hovered points change and the chart has a TooltipTemplate.</summary>
    [JSInvokable]
    public Task OnTooltipHover(string[] seriesIds, int[] indexes)
    {
        if (TooltipTemplate is null || _tooltipHost is null || _layout is null)
        {
            return Task.CompletedTask;
        }

        var context = TooltipBuilder.Context<TItem>(CustomTooltip(_layout), DataOf, SourceOf, _layout.Time, seriesIds, indexes);
        return context is null ? Task.CompletedTask : _tooltipHost.ShowAsync(context);
    }

    // ---------- Responsive sizing, font metrics and pointer interaction (JS) ----------

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed)
        {
            return;
        }

        await FocusExportMenuAsync();
        try
        {
            if (firstRender)
            {
                _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/RioCharts.Blazor/RioChart.razor.js");
                _dotnet = DotNetObjectReference.Create(this);
                var info = await _module.InvokeAsync<ChartContainerInfo>("init", _container, _dotnet);
                var changed = false;
                if (info.Widths is { Length: > 0 } widths)
                {
                    _measurer = new WidthTableTextMeasurer(widths);
                    changed = true;
                }

                _viewerZone = info.TimeZone;
                if (info.Width > 0 && Width is null)
                {
                    _measuredWidth = info.Width;
                    changed = true;
                }

                if (changed)
                {
                    Invalidate();
                    return; // the next render sends the interaction data for the real layout
                }
            }

            await SyncInteractionAsync();
        }
        catch (JSDisconnectedException)
        {
        }
        catch (TaskCanceledException)
        {
        }
        catch (JSException)
        {
            // No JS (e.g. static rendering): keep the approximate layout, no tooltips.
        }
    }

    /// <summary>
    /// Sends the drawn points (pixel positions and formatted values) to JS once per layout, so
    /// pointer moves are handled in the browser without a round trip.
    /// </summary>
    private async Task SyncInteractionAsync()
    {
        if (_module is null || _layout is null)
        {
            return;
        }

        var keys = KeyboardEnabled(_layout);
        var key = (_layout, OnPointClick.HasDelegate, Zoomable, Group, SeriesLayer, TooltipTemplate is not null, keys);
        if (Equals(key, _sentInteraction))
        {
            return;
        }

        _sentInteraction = key;
        if (SeriesLayer == SeriesLayer.Canvas)
        {
            await _module.InvokeVoidAsync("paint", _container, CanvasSeriesWriter.Build(_layout.Scene));
        }

        var model = CustomTooltip(_layout);
        var click = OnPointClick.HasDelegate;
        var scale = _layout.XScale;
        var zoom = Zoomable && scale is not null && _layout.HasData;
        var drags = _layout.DragHandles;
        object? payload = model.Enabled || click || zoom || keys || drags.Count > 0 || (Group is not null && scale is not null)
            ? new
            {
                drags = drags.Select(d => new { id = d.Id, x = d.IsX, pos = d.Pixel, s = new { d0 = d.DomainMin, d1 = d.DomainMax, r0 = d.RangeStart, r1 = d.RangeEnd } }),
                plot = new { x = model.Plot.X, y = model.Plot.Y, w = model.Plot.Width, h = model.Plot.Height },
                tooltip = model.Enabled,
                template = model.Enabled && TooltipTemplate is not null,
                shared = model.Shared,
                band = model.BandWidth,
                click,
                keys,
                zoom,
                zoomed = _layout.Zoomed,
                group = Group,
                xscale = scale is null ? null : new { d0 = scale.DomainMin, d1 = scale.DomainMax, r0 = scale.RangeStart, r1 = scale.RangeEnd },
                series = model.Series.Select(s => new
                {
                    id = s.Id,
                    name = s.Name,
                    color = SvgWriter.Color(s.Color),
                    bar = s.Type == SeriesType.Bar,
                    snap = s.Snap,
                    barWidth = s.BarWidth,
                    barOffset = s.BarOffset,
                    x = s.X,
                    y = s.Y,
                    index = s.Index,
                    xl = s.XLabels,
                    v = s.Values,
                }),
                slices = model.Slices.Select(s => new
                {
                    id = s.SeriesId,
                    index = s.Index,
                    header = s.Header,
                    label = s.Label,
                    value = s.Value,
                    color = SvgWriter.Color(s.Color),
                    cx = s.Cx,
                    cy = s.Cy,
                    r0 = s.R0,
                    r1 = s.R1,
                    a0 = s.A0,
                    a1 = s.A1,
                    end = s.HitEnd,
                }),
            }
            : null;
        await _module.InvokeVoidAsync("update", _container, payload);
    }

    // ---------- Keyboard ----------

    /// <summary>Whether the chart is a tab stop with arrow-key navigation: on, with points or slices to move between.</summary>
    private bool KeyboardEnabled(ChartLayoutResult layout) =>
        KeyboardNavigation && layout.HasData && (layout.Tooltip.Series.Any(s => s.X.Length > 0) || layout.Tooltip.Slices.Count > 0);

    /// <summary>The instructions screen readers read with the chart (aria-describedby).</summary>
    private string KeyboardHelp(ChartLayoutResult layout)
    {
        var polar = layout.Tooltip.Slices.Count > 0;
        var series = layout.Tooltip.Series.Count(s => s.X.Length > 0);
        var move = polar ? "Use the arrow keys to move between slices." :
            series > 1 ? "Use Left and Right to move between points, Up and Down to change series." : "Use Left and Right to move between points.";
        return OnPointClick.HasDelegate ? $"{move} Press Enter to select a point, Escape to leave." : $"{move} Press Escape to leave.";
    }

    // ---------- Zoom and pan ----------

    /// <summary>The visible x range, or null when all data is shown.</summary>
    public ChartRange? GetVisibleRange() => _range is { } r ? FromRange(r) : null;

    /// <summary>Shows the given x range (clamped to the data).</summary>
    public Task ZoomToAsync(ChartRange range) => ToRange(range) is { } r ? ApplyRangeAsync(r) : Task.CompletedTask;

    /// <summary>Shows all data again.</summary>
    public Task ResetZoomAsync() => ApplyRangeAsync(null);

    /// <summary>Moves the visible range by a fraction of its width (negative: left).</summary>
    public Task PanAsync(double fraction)
    {
        if (_layout?.XScale is not { } scale)
        {
            return Task.CompletedTask;
        }

        var shift = (scale.DomainMax - scale.DomainMin) * fraction;
        return ApplyRangeAsync((scale.DomainMin + shift, scale.DomainMax + shift));
    }

    /// <summary>Called by JS at the end of a drag, pan or wheel zoom (data units).</summary>
    [JSInvokable]
    public Task OnZoom(double min, double max) => ApplyRangeAsync((min, max));

    [JSInvokable]
    public Task OnZoomReset() => ApplyRangeAsync(null);

    // The Reset zoom button: linked charts (same Group) reset together, as with a double-click.
    private async Task OnResetZoomClick()
    {
        if (Group is not null && _module is not null)
        {
            try
            {
                if (await _module.InvokeAsync<bool>("resetZoom", _container))
                {
                    return;
                }
            }
            catch (JSDisconnectedException)
            {
                return;
            }
        }

        await ResetZoomAsync();
    }

    private async Task ApplyRangeAsync((double Min, double Max)? range)
    {
        if (range is { } r)
        {
            range = Clamp(r);
            if (range is null)
            {
                return;
            }
        }

        if (OnZoomChanging.HasDelegate)
        {
            var args = new ZoomChangingEventArgs { Range = range is { } x ? FromRange(x) : null };
            await OnZoomChanging.InvokeAsync(args);
            if (args.Cancel)
            {
                return;
            }

            range = args.Range is null ? null : ToRange(args.Range) is { } changed ? Clamp(changed) : null;
        }

        if (Equals(range, _range))
        {
            return;
        }

        _range = range;
        _dirty = true;
        var bound = GetVisibleRange();
        if (VisibleRangeChanged.HasDelegate)
        {
            _lastVisibleRangeParameter = bound;
            await VisibleRangeChanged.InvokeAsync(bound);
        }

        StateHasChanged();
    }

    /// <summary>Keeps the range inside the data and not narrower than a millionth of it.</summary>
    private (double Min, double Max)? Clamp((double Min, double Max) range)
    {
        var (min, max) = range.Min <= range.Max ? range : (range.Max, range.Min);
        if (_layout?.XExtent is not { } extent || !double.IsFinite(min) || !double.IsFinite(max))
        {
            return max > min ? (min, max) : null;
        }

        var full = extent.Max - extent.Min;
        var width = Math.Min(Math.Max(max - min, full * 1e-6), full);
        if (width >= full * 0.999)
        {
            return null; // all data: no zoom
        }

        min = Math.Clamp(min, extent.Min, extent.Max - width);
        return (min, min + width);
    }

    private ChartRange FromRange((double Min, double Max) r) => _layout?.XKind != XKind.Time
        ? new ChartRange(r.Min, r.Max)
        : _layout.Time is { } time
            ? new ChartRange(time.ToDateTimeOffset(r.Min), time.ToDateTimeOffset(r.Max))
            : new ChartRange(TimeValue.ToDateTime(r.Min), TimeValue.ToDateTime(r.Max));

    private (double Min, double Max)? ToRange(ChartRange? range)
    {
        if (range is null)
        {
            return null;
        }

        var kind = _layout?.XKind ?? XKind.Numeric;
        double Convert(object value) => kind == XKind.Time && _layout?.Time is { } time ? time.ToInstant(value) : AxisValue.ToDouble(value, kind);
        var (min, max) = (Convert(range.Min), Convert(range.Max));
        return double.IsFinite(min) && double.IsFinite(max) && max != min ? (Math.Min(min, max), Math.Max(min, max)) : null;
    }

    /// <summary>Called by the JS ResizeObserver when the container width changes.</summary>
    [JSInvokable]
    public void OnResize(double width)
    {
        if (width > 0 && Width is null && Math.Abs(width - (_measuredWidth ?? 0)) >= 1)
        {
            _measuredWidth = width;
            Invalidate();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("dispose", _container);
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


}
