using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using RioCharts.Blazor.Rendering;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Definitions;
using RioCharts.Core.Text;

namespace RioCharts.Blazor;

/// <summary>
/// A chart. Series are added as child components:
/// <code>&lt;RioChart Items="readings"&gt;&lt;LineSeries XValue="r =&gt; r.Time" YValue="r =&gt; r.Sessions" /&gt;&lt;/RioChart&gt;</code>
/// The chart fills its container's width and lays out again when the width changes.
/// </summary>
public partial class RioChart<TItem> : IChartContext
{
    private const double FallbackWidth = 640;
    private const double CompactFallbackWidth = 160;

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
    private DotNetObjectReference<JsCallbacks>? _dotnet;
    private object? _sentInteraction;
    private (double Min, double Max)? _range;

    // Linked charts (Group): the plot insets the group agreed on, so the plots line up.
    private PlotInsets? _groupInsets;
    private string? _lastGroup;
    private ChartRange? _lastVisibleRangeParameter;

    // The range changed from code or a parameter (not from the JS module): linked charts follow on
    // the next update. A drag, pan or wheel zoom already reaches them from the module.
    private bool _shareZoom;
    private string? _viewerZone;
    private bool _disposed;
    private IReadOnlyDictionary<string, SeriesData> _layoutData = new Dictionary<string, SeriesData>();
    private (ChartLayoutResult Layout, TooltipModel Model)? _tooltip;
    private TooltipHost<TItem>? _tooltipHost;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    /// <summary>Items shared by all series that do not set their own.</summary>
    [Parameter] public IEnumerable<TItem>? Items { get; set; }

    /// <summary>The series and annotation components, e.g. &lt;LineSeries&gt; and &lt;ReferenceLine&gt;.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Height in pixels. Default 300, or the cascaded <see cref="RioChartDefaults"/>.</summary>
    [Parameter] public double Height { get; set; } = 300;

    /// <summary>Fixed width in pixels. Default: the container's width (responsive).</summary>
    [Parameter] public double? Width { get; set; }

    /// <summary>Title shown above the chart.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>The horizontal axis: title, range, scale and label format. Default: the scale is picked from the data.</summary>
    [Parameter] public AxisOptions? XAxis { get; set; }

    /// <summary>The primary vertical axis (id "y" unless set).</summary>
    [Parameter] public AxisOptions? YAxis { get; set; }

    /// <summary>Additional vertical axes; series pick one with <c>YAxisId</c>.</summary>
    [Parameter] public IReadOnlyList<AxisOptions>? YAxes { get; set; }

    /// <summary>Legend settings. Default: at the top, shown when there is more than one series.</summary>
    [Parameter] public LegendOptions? Legend { get; set; }

    /// <summary>Tooltip settings. On and shared across series by default.</summary>
    [Parameter] public TooltipOptions? Tooltip { get; set; }

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
    [Parameter] public IReadOnlyList<Annotation>? Annotations { get; set; }

    /// <summary>
    /// Display time zone: "UTC", "local" (the viewer's browser zone) or an IANA id such as
    /// "Europe/Stockholm". Default: none, times are shown as given. With a zone, times are plotted
    /// on instants (no doubling back at DST changes) and labelled in that zone.
    /// </summary>
    [Parameter] public string? TimeZone { get; set; }

    /// <summary>With a TimeZone: how DateTime values with Kind Unspecified are read (UTC or the display zone).</summary>
    [Parameter] public UnspecifiedTimes? UnspecifiedTimes { get; set; }

    /// <summary>Culture for labels. Default: the cascaded <see cref="RioChartDefaults"/>, else the current culture.</summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>
    /// The chart's own texts ("Reset zoom", "No data available", the download menu, screen reader
    /// texts), e.g. <c>ChartTexts.Swedish with { ResetZoom = "Visa allt" }</c>. Default: a
    /// <see cref="ChartTexts"/> cascaded by the app, else <see cref="ChartTexts.For"/> the
    /// <see cref="Culture"/> or, without one, the current UI culture.
    /// </summary>
    [Parameter] public ChartTexts? Texts { get; set; }

    /// <summary>Texts for every chart below, e.g. <c>&lt;CascadingValue Value="texts"&gt;</c> around the router.</summary>
    [CascadingParameter] private ChartTexts? CascadedTexts { get; set; }

    /// <summary>The texts in use.</summary>
    private ChartTexts CurrentTexts => Texts ?? CascadedTexts ?? ChartTexts.For(Culture ?? CultureInfo.CurrentUICulture);

    /// <summary>Ids of hidden series. Two-way bindable with <c>@bind-HiddenSeries</c>.</summary>
    [Parameter] public IReadOnlyCollection<string>? HiddenSeries { get; set; }

    /// <summary>Called with the new hidden series when the user toggles one in the legend.</summary>
    [Parameter] public EventCallback<IReadOnlyCollection<string>> HiddenSeriesChanged { get; set; }

    /// <summary>Raised after a legend click has toggled a series.</summary>
    [Parameter] public EventCallback<LegendClickEventArgs> OnLegendClick { get; set; }

    /// <summary>
    /// Raised when a point or bar is clicked. Lines, areas and scatter points: a click within 16 px of a point (the closest
    /// point of any series wins), so a click on a line next to a point picks it, but one between points far apart picks
    /// nothing. Bars: anywhere on the bar. Pies, heatmaps, treemaps and calendars: the slice or cell.
    /// </summary>
    [Parameter] public EventCallback<PointClickEventArgs<TItem>> OnPointClick { get; set; }

    /// <summary>
    /// Raised when an annotation is clicked: a reference line or band, a point annotation or a callout (each becomes a
    /// focusable button). A click on a point annotation's marker, label or callout box is the annotation's, even when it
    /// sits on a data point; elsewhere a point or bar under the pointer wins over an annotation underneath.
    /// </summary>
    [Parameter] public EventCallback<AnnotationClickEventArgs> OnAnnotationClick { get; set; }

    /// <summary>
    /// Raised when the user has dragged a reference line (<c>Draggable</c>). For lines in
    /// <see cref="Annotations"/>, update the definition's value here; components use <c>@bind-Value</c>.
    /// </summary>
    [Parameter] public EventCallback<AnnotationChangedEventArgs> OnAnnotationChanged { get; set; }

    /// <summary>
    /// Zoom and pan on the x axis: drag to zoom in, Shift+drag to pan, Ctrl/⌘+wheel to zoom at the
    /// pointer, pinch or two-finger drag on touch screens, double-click or the reset button to zoom
    /// out. Not for category axes.
    /// </summary>
    [Parameter] public bool Zoomable { get; set; }

    /// <summary>The visible x range (null: all data). Two-way bindable with <c>@bind-VisibleRange</c>.</summary>
    [Parameter] public ChartRange? VisibleRange { get; set; }

    /// <summary>Called with the new visible range after a zoom, pan or reset (null: all data).</summary>
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
    /// Compact chart (sparkline): no axes, grid, legend or title; the series fill the box and the y
    /// range fits the data (bars keep zero). Set a small <see cref="Height"/> too, e.g. 32. Keyboard
    /// navigation is off unless <see cref="KeyboardNavigation"/> is set; the chart has a short text summary.
    /// </summary>
    [Parameter] public bool Compact { get; set; }

    /// <summary>
    /// Default true: as the chart gets small (a narrow container, a low <see cref="Height"/>), it
    /// leaves out axis titles first, then the legend, the title and the axis labels, so the plot keeps
    /// the space; the tooltip still shows the series and values. Below 120 × 60 px it becomes a
    /// compact chart. False keeps everything at any size.
    /// </summary>
    [Parameter] public bool AutoCompact { get; set; } = true;

    /// <summary>
    /// Bar, box plot, waterfall and dumbbell charts: they run left to right, with the categories down the side
    /// (first on top). For rankings and long category names. <see cref="YAxis"/> (title, format, min, max) applies to the values.
    /// </summary>
    [Parameter] public bool Horizontal { get; set; }

    /// <summary>
    /// Maps: how the round world is drawn flat. <see cref="MapProjection.EqualEarth"/> (default) keeps
    /// every country's true relative size; <see cref="MapProjection.Mercator"/> is the projection of
    /// web maps, with familiar shapes but areas that grow toward the poles. Overrides a
    /// <see cref="Definition"/>'s projection unless left at the default.
    /// </summary>
    [Parameter] public MapProjection Projection { get; set; }

    /// <summary>
    /// False draws plain SVG without the JS module: no tooltip, hover, zoom, keyboard navigation,
    /// download button or width measuring. For many small charts (compact charts in a table) or static
    /// pages. Set <see cref="Width"/> for exact geometry; otherwise the chart is laid out at a default
    /// width (640, compact 160) and scaled to fit. Read once, when the chart first renders.
    /// </summary>
    [Parameter] public bool Interactive { get; set; } = true;

    /// <summary>
    /// Keyboard navigation (default on, off for compact charts): the chart is a tab stop, and with it focused the arrow keys
    /// move between points (Up/Down: between series; pies: between slices), showing the tooltip and
    /// announcing the point to screen readers; Enter clicks it, Escape leaves. With <see cref="Zoomable"/>,
    /// + and - zoom around the focused point, Shift+Left/Right pans and 0 resets. Handled in the browser.
    /// </summary>
    [Parameter] public bool? KeyboardNavigation { get; set; }

    /// <summary>
    /// False turns off all motion: the short fade when the series first appear, and the fades of the
    /// tooltip, buttons and dimmed series. Charts never animate data changes, zoom or streaming either
    /// way, and motion is always off when the user asks for reduced motion. To change only the fade's
    /// length, set the CSS variable <c>--rio-enter-duration</c> (default <c>0.35s</c>; <c>0s</c> turns it off).
    /// </summary>
    [Parameter] public bool Animate { get; set; } = true;

    /// <summary>Extra CSS class on the container.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>The latest layout: scene, scales, hit-test index and warnings.</summary>
    internal ChartLayoutResult? Layout => _layout;

    /// <summary>
    /// A chart definition (e.g. from JSON or an AI agent) instead of child components: series read their
    /// x and y fields from <see cref="Datasets"/> or the definition's inline data. Layout options such as
    /// Height, Culture and HiddenSeries still come from the component.
    /// </summary>
    [Parameter] public ChartDefinition? Definition { get; set; }

    /// <summary>Datasets that a <see cref="Definition"/> refers to, by name.</summary>
    [Parameter] public IReadOnlyDictionary<string, ChartDataset>? Datasets { get; set; }

    /// <summary>Problems binding <see cref="Definition"/> to the data (missing fields, wrong types); empty when fine.</summary>
    public IReadOnlyList<DefinitionError> DefinitionErrors => _bound?.Errors ?? [];

    /// <summary>The chart model: the <see cref="Definition"/> parameter, or one built from the parameters and child components (for saving, or as a starting point for edits).</summary>
    public ChartDefinition GetDefinition() => Definition ?? BuildDefinition();

    private DefinitionBindResult? _bound;
    private (ChartDefinition?, object?, string?) _boundFrom;

    /// <summary>The definition being drawn: the bound <see cref="Definition"/>, or the one from parameters and child components.</summary>
    private ChartDefinition CurrentDefinition() => Definition is not null && _bound is not null
        ? (Compact && !_bound.Definition.Compact) || (!AutoCompact && _bound.Definition.AutoCompact) || (Horizontal && !_bound.Definition.Horizontal)
            || (Projection != MapProjection.EqualEarth && Projection != _bound.Definition.Projection)
            ? _bound.Definition with
            {
                Compact = Compact || _bound.Definition.Compact,
                AutoCompact = AutoCompact && _bound.Definition.AutoCompact,
                Horizontal = Horizontal || _bound.Definition.Horizontal,
                Projection = Projection != MapProjection.EqualEarth ? Projection : _bound.Definition.Projection,
            }
            : _bound.Definition
        : BuildDefinition();

    /// <summary>The canvas series layer needs the JS module; without it the series stay SVG.</summary>
    private bool UsesCanvas => SeriesLayer == SeriesLayer.Canvas && Live;

    /// <summary>
    /// Interactive and rendered interactively. Static server rendering and prerendering have no JS,
    /// so they draw plain SVG: no tab stop, download button, hover layer or (blank) canvas.
    /// </summary>
    private bool Live => Interactive && RendererInfo.IsInteractive;

    /// <summary>Compact from the parameter or the definition.</summary>
    private bool IsCompact => Compact || Definition?.Compact == true;

    /// <summary>The width to lay out at before (or without) measuring the container.</summary>
    private double DefaultWidth => IsCompact ? CompactFallbackWidth : FallbackWidth;

    private string ContainerStyle => string.Create(CultureInfo.InvariantCulture,
        $"height:{Height:0.##}px;{(Width is { } w ? $"width:{w:0.##}px;" : "")}");

    // ---------- IChartContext ----------

    object? IChartContext.DefaultItems => Items;

    private (string?, UnspecifiedTimes?, string?) _bucketZoneKey;
    private TimeConversion? _bucketZone;

    TimeConversion? IChartContext.BucketZone
    {
        get
        {
            var key = (TimeZone, UnspecifiedTimes, _viewerZone);
            if (key != _bucketZoneKey)
            {
                (_bucketZoneKey, _bucketZone) = (key, TimeConversion.For(TimeZone, UnspecifiedTimes, _viewerZone));
            }

            return _bucketZone;
        }
    }

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

    /// <summary>Defaults for every chart below, cascaded by the app (see <see cref="RioChartDefaults"/>).</summary>
    [CascadingParameter] private RioChartDefaults? Defaults { get; set; }

    [Inject] private IServiceProvider Services { get; set; } = null!;

    // Defaults registered with AddRioCharts (optional), looked up once.
    private RioChartDefaults? _registeredDefaults;
    private bool _lookedUpDefaults;

    private RioChartDefaults? RegisteredDefaults
    {
        get
        {
            if (!_lookedUpDefaults)
            {
                _registeredDefaults = Services.GetService(typeof(RioChartDefaults)) as RioChartDefaults;
                _lookedUpDefaults = true;
            }

            return _registeredDefaults;
        }
    }

    /// <inheritdoc />
    public override Task SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        ApplyDefaults(parameters);
        return base.SetParametersAsync(ParameterView.Empty);
    }

    // Parameters the chart does not set take the cascaded default, else the built-in one. Done on every
    // parameter set, so a changed default reaches charts that do not set the parameter themselves.
    private void ApplyDefaults(ParameterView parameters)
    {
        // Each setting on its own: the cascaded defaults' value, else the registered one (AddRioCharts), else built in.
        var (d, r) = (Defaults, RegisteredDefaults);
        bool Unset<T>(string name) => !parameters.TryGetValue<T>(name, out _);
        if (Unset<double>(nameof(Height)))
        {
            Height = d?.Height ?? r?.Height ?? 300;
        }

        if (Unset<CultureInfo?>(nameof(Culture)))
        {
            Culture = d?.Culture ?? r?.Culture;
        }

        if (Unset<string?>(nameof(TimeZone)))
        {
            TimeZone = d?.TimeZone ?? r?.TimeZone;
        }

        if (Unset<UnspecifiedTimes?>(nameof(UnspecifiedTimes)))
        {
            UnspecifiedTimes = d?.UnspecifiedTimes ?? r?.UnspecifiedTimes;
        }

        if (Unset<AxisOptions?>(nameof(XAxis)))
        {
            XAxis = d?.XAxis ?? r?.XAxis;
        }

        if (Unset<AxisOptions?>(nameof(YAxis)))
        {
            YAxis = d?.YAxis ?? r?.YAxis;
        }

        if (Unset<LegendOptions?>(nameof(Legend)))
        {
            Legend = d?.Legend ?? r?.Legend;
        }

        if (Unset<TooltipOptions?>(nameof(Tooltip)))
        {
            Tooltip = d?.Tooltip ?? r?.Tooltip;
        }

        if (Unset<bool>(nameof(Animate)))
        {
            Animate = d?.Animate ?? r?.Animate ?? true;
        }

        if (Unset<bool?>(nameof(KeyboardNavigation)))
        {
            KeyboardNavigation = d?.KeyboardNavigation ?? r?.KeyboardNavigation;
        }

        if (Unset<string?>(nameof(Class)))
        {
            Class = d?.Class ?? r?.Class;
        }
    }

    private void Invalidate()
    {
        _dirty = true;
        StateHasChanged();
    }

    // ---------- Parameters and layout ----------

    /// <inheritdoc />
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
            _shareZoom = true;
        }

        SelectionParametersSet();
        TreemapParametersSet();
        if (Group != _lastGroup)
        {
            _lastGroup = Group;
            if (_groupInsets is not null)
            {
                _groupInsets = null;
                _dirty = true;
            }
        }

        var parameters = (Height, Width, Title, XAxis, YAxis, YAxes, Legend, Tooltip, Annotations, Culture?.Name, OnAnnotationClick.HasDelegate, TimeZone, UnspecifiedTimes, SeriesLayer, DownsampleThreshold, Definition, Datasets,
            TooltipHeader is not null, TooltipTemplate is not null, Compact, AutoCompact, Horizontal, Projection, CurrentTexts);
        if (!Equals(parameters, _lastParameters))
        {
            _lastParameters = parameters;
            _dirty = true;
        }
    }

    /// <summary>Lays out again, e.g. after changing data in place without a new reference or DataVersion.</summary>
    public void Refresh() => Invalidate();

    private ChartDefinition BuildDefinition() => new()
    {
        Title = Title,
        Compact = Compact,
        AutoCompact = AutoCompact,
        Horizontal = Horizontal,
        Projection = Projection,
        X = XAxis ?? new AxisOptions(),
        Y = YAxis ?? new AxisOptions(),
        YAxes = YAxes ?? [],
        Legend = Legend ?? new LegendOptions(),
        Tooltip = Tooltip ?? new TooltipOptions(),
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
        if (Definition is not null)
        {
            // Bind once per definition/datasets pair; legend toggles and resizes reuse it.
            if (_bound is null || !Equals(_boundFrom, (Definition, (object?)Datasets, _viewerZone)))
            {
                _bound = ChartDefinitionBinder.Bind(Definition, Datasets ?? new Dictionary<string, ChartDataset>(), viewerTimeZone: _viewerZone);
                _boundFrom = (Definition, Datasets, _viewerZone);
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
        SelectionForLayout(out var selectedRange, out var selectedPoints);
        var options = new LayoutOptions
        {
            Width = Width ?? _measuredWidth ?? DefaultWidth,
            Height = Height,
            Measurer = _measurer,
            Culture = Culture ?? CultureInfo.CurrentCulture,
            Texts = CurrentTexts,
            HiddenSeries = _hidden,
            InteractiveAnnotations = OnAnnotationClick.HasDelegate,
            DownsampleThreshold = DownsampleThreshold ?? (UsesCanvas ? Math.Max(2000, (int)((Width ?? _measuredWidth ?? DefaultWidth) * 4)) : null),
            XRange = _range,
            ViewerTimeZone = _viewerZone,
            MinInsets = Group is null ? null : _groupInsets,
            SelectedRange = selectedRange,
            SelectedPoints = selectedPoints,
            TreemapPath = _treemapPath,
            InteractiveTreemap = Live,
            TreemapDrillOnItems = !OnPointClick.HasDelegate && Selection != ChartSelectionMode.Points,
            MapRegion = _treemapPath,
            InteractiveMap = Live && !OnPointClick.HasDelegate && Selection != ChartSelectionMode.Points,
        };

        try
        {
            _layout = ChartLayout.Compute(CurrentDefinition(), data, options);
        }
        catch (Exception e)
        {
            // A bug, or an exception from the app's own code (a LabelFormatter): log it and draw this chart's empty state
            // with a message, so one chart does not take the whole page down.
            Logger?.LogError(e, "RioChart '{Title}' could not be laid out; it shows a placeholder instead.", Title);
            var texts = CurrentTexts;
            _layoutData = new Dictionary<string, SeriesData>();
            _layout = ChartLayout.Compute(new ChartDefinition { Title = Title }, _layoutData,
                options with { Texts = texts with { NoData = texts.ChartFailed } });
        }

        _dirty = false;
        return _layout;
    }

    // The app's logger, when it has one (looked up once).
    private ILogger? _logger;
    private bool _lookedUpLogger;

    private ILogger? Logger
    {
        get
        {
            if (!_lookedUpLogger)
            {
                _logger = (Services.GetService(typeof(ILoggerFactory)) as ILoggerFactory)?.CreateLogger("RioCharts.Blazor.RioChart");
                _lookedUpLogger = true;
            }

            return _logger;
        }
    }

    // ---------- Legend and annotation clicks ----------

    private async Task HandleInteraction(NodeInteraction interaction)
    {
        if (interaction.Kind == "annotation")
        {
            var label = CurrentDefinition().Annotations.Select((a, i) => (a, i)).FirstOrDefault(t => (t.a.Id ?? $"annotation-{t.i + 1}") == interaction.Id).a?.Label;
            await OnAnnotationClick.InvokeAsync(new AnnotationClickEventArgs(interaction.Id, label));
            return;
        }

        if (interaction.Kind is "treemap" or "map")
        {
            await DrillDownAsync(interaction.Id);
            return;
        }

        if (interaction.Kind != "legend")
        {
            return;
        }

        await LegendClickedAsync(interaction.Id);
    }

    /// <summary>Called by JS when a draggable line is dropped (value in data units).</summary>
    internal async Task OnAnnotationDragged(string id, double value)
    {
        var all = CurrentDefinition().Annotations;
        var index = -1;
        for (var i = 0; i < all.Count && index < 0; i++)
        {
            if ((all[i].Id ?? $"annotation-{i + 1}") == id)
            {
                index = i;
            }
        }

        if (_layout?.DragHandles.FirstOrDefault(h => h.Id == id) is { Log: true })
        {
            value = Math.Pow(10, value); // JS dragged in log10 units
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
    internal async Task OnPointClicked(string seriesId, int index, bool additive = false)
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
        await SelectPointAsync(items, additive);
    }

    // ---------- Custom tooltips ----------

    private ISeriesSource? SourceOf(string seriesId) => Definition is null ? _series.FirstOrDefault(s => s.Spec.Id == seriesId) : null;

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

    // SvgSceneRenderer is internal, and Razor only resolves public components as tags.
    private RenderFragment SceneFragment(Scene scene) => builder =>
    {
        builder.OpenComponent<SvgSceneRenderer>(0);
        builder.AddComponentParameter(1, nameof(SvgSceneRenderer.Scene), scene);
        builder.AddComponentParameter(2, nameof(SvgSceneRenderer.IdPrefix), _id);
        builder.AddComponentParameter(3, nameof(SvgSceneRenderer.OnInteraction), EventCallback.Factory.Create<NodeInteraction>(this, HandleInteraction));
        builder.AddComponentParameter(4, nameof(SvgSceneRenderer.OmitSeries), UsesCanvas);
        builder.AddComponentParameter(5, nameof(SvgSceneRenderer.LegendTips), Live);
        builder.CloseComponent();
    };

    private RenderFragment TooltipHostFragment => builder =>
    {
        builder.OpenComponent<TooltipHost<TItem>>(0);
        builder.AddComponentParameter(1, nameof(TooltipHost<TItem>.Template), TooltipTemplate);
        builder.AddComponentReferenceCapture(2, host => _tooltipHost = (TooltipHost<TItem>)host);
        builder.CloseComponent();
    };

    /// <summary>Called by JS when the hovered points change and the chart has a TooltipTemplate.</summary>
    internal Task OnTooltipHover(string[] seriesIds, int[] indexes)
    {
        if (TooltipTemplate is null || _tooltipHost is null || _layout is null)
        {
            return Task.CompletedTask;
        }

        var context = TooltipBuilder.Context<TItem>(CustomTooltip(_layout), DataOf, SourceOf, _layout.Time, seriesIds, indexes);
        return context is null ? Task.CompletedTask : _tooltipHost.ShowAsync(context);
    }

    // ---------- Responsive sizing, font metrics and pointer interaction (JS) ----------

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed)
        {
            return;
        }

        await SyncRangeItemsAsync();
        await FocusExportMenuAsync();
        try
        {
            // Not interactive: plain SVG, the JS module is never loaded.
            if (_module is null && !Interactive)
            {
                return;
            }

            if (firstRender)
            {
                _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/RioCharts.Blazor/RioChart.razor.js");
                _dotnet = DotNetObjectReference.Create(new JsCallbacks(this));
                var info = ChartContainerInfo.From(await _module.InvokeAsync<System.Text.Json.JsonElement>("init", _container, _dotnet));
                var changed = false;
                if (info.Widths is { Length: > 0 } widths)
                {
                    _measurer = new WidthTableTextMeasurer(widths);
                    changed = true;
                }

                // "local" is the browser's zone: series that bucket by time group again in it.
                changed |= info.TimeZone != _viewerZone && string.Equals(TimeZone, "local", StringComparison.OrdinalIgnoreCase);
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
    /// pointer moves are handled in the browser without a round trip. Called only from
    /// OnAfterRenderAsync, inside its try/catch for disconnects and JS errors (hence BL0016 off).
    /// </summary>
#pragma warning disable BL0016
    private async Task SyncInteractionAsync()
    {
        if (_module is null || _layout is null)
        {
            return;
        }

        var keys = KeyboardEnabled(_layout);
        var key = (_layout, OnPointClick.HasDelegate, Zoomable, Group, SeriesLayer, TooltipTemplate is not null, keys, Selection);
        if (Equals(key, _sentInteraction))
        {
            return;
        }

        _sentInteraction = key;
        if (UsesCanvas)
        {
            await _module.InvokeVoidAsync("paint", _container, InteropJson.Bytes(CanvasSeriesWriter.Build(_layout.Scene)));
        }

        var model = CustomTooltip(_layout);
        var click = OnPointClick.HasDelegate || Selection == ChartSelectionMode.Points;
        var scale = _layout.XScale;
        var select = Selection == ChartSelectionMode.Range && scale is not null && _layout.ZoomsX && _layout.HasData ? "range" : Selection == ChartSelectionMode.Points ? "points" : null;
        var zoom = Zoomable && scale is not null && _layout.ZoomsX && _layout.HasData;
        var drags = _layout.DragHandles;
        // UTF-8 JSON from source-generated metadata (Rendering/Interop.cs): trimming and AOT safe.
        byte[]? payload = model.Enabled || click || zoom || keys || select is not null || drags.Count > 0 || (Group is not null && scale is not null)
            ? InteropJson.Bytes(new UpdateModel(
                Drags: [.. drags.Select(d => new DragModel(d.Id, d.IsX, d.Pixel, new ScaleModel(d.DomainMin, d.DomainMax, d.RangeStart, d.RangeEnd)))],
                Plot: new PlotModel(model.Plot.X, model.Plot.Y, model.Plot.Width, model.Plot.Height),
                Tooltip: model.Enabled,
                Template: model.Enabled && TooltipTemplate is not null,
                Shared: model.Shared,
                Band: model.BandWidth,
                Click: click,
                Keys: keys,
                Pos: keys ? CurrentTexts.Position : null,
                Zoom: zoom,
                Zoomed: _layout.Zoomed,
                Group: Group,
                // A log x scale goes out in log10 units, so the module's zoom and pan math stays linear (FromScaleUnits).
                Xscale: scale is null ? null
                    : scale.IsLog ? new ScaleModel(Math.Log10(scale.DomainMin), Math.Log10(scale.DomainMax), scale.RangeStart, scale.RangeEnd, L: true)
                    : new ScaleModel(scale.DomainMin, scale.DomainMax, scale.RangeStart, scale.RangeEnd),
                Series: [.. model.Series.Select(s => new SeriesModel(s.Id, s.Name, SvgWriter.Color(s.Color), s.Type is SeriesType.Bar or SeriesType.BoxPlot or SeriesType.Candlestick or SeriesType.Waterfall or SeriesType.Histogram, s.Step, s.Snap,
                    s.BarWidth, s.BarOffset, s.X, s.Y, s.Index, s.XLabels, s.Values, s.Type is SeriesType.Scatter or SeriesType.MapBubble, s.CellHeight, s.CellWidths,
                    s.PartLabels, s.PartValues, s.Spokes, s.CellHeights,
                    s.PointColors is { } colors ? [.. colors.Select(SvgWriter.Color)] : null,
                    s.Type is SeriesType.BoxPlot or SeriesType.Candlestick or SeriesType.Waterfall, s.Radii, s.Shapes))],
                Slices: [.. model.Slices.Select(s => new SliceModel(s.SeriesId, s.Index, s.Header, s.Label, s.Value, SvgWriter.Color(s.Color),
                    s.Cx, s.Cy, s.R0, s.R1, s.A0, s.A1, s.HitEnd,
                    s.Parts.Count > 0 ? [.. s.Parts.Select(p => p.Label)] : null, s.Parts.Count > 0 ? [.. s.Parts.Select(p => p.Value)] : null))],
                Need: Group is not null && _layout.NeededInsets is { } need ? new InsetsModel(need.Left, need.Right) : null,
                At: Group is not null && _groupInsets is { } at ? new InsetsModel(at.Left, at.Right) : null,
                Select: select,
                Sel: _selected.Count > 0 || _selectedRange is not null,
                Radar: model.Radar is { } radar ? new RadarModel(radar.Cx, radar.Cy, radar.Radius, radar.Count) : null))
            : null;
        await _module.InvokeVoidAsync("update", _container, payload);
        if (_shareZoom)
        {
            _shareZoom = false;
            if (Group is not null)
            {
                await _module.InvokeVoidAsync("zoomGroup", _container, _range?.Min, _range?.Max);
            }
        }

        await SendTooltipAsync(); // a tooltip shown from code moves with the new layout
        await SendHighlightAsync(); // set before the module was loaded
    }
#pragma warning restore BL0016

    // ---------- Keyboard ----------

    /// <summary>Whether the chart is a tab stop with arrow-key navigation: on, with points or slices to move between.</summary>
    private bool KeyboardEnabled(ChartLayoutResult layout) =>
        (KeyboardNavigation ?? !IsCompact) && Live && layout.HasData && (layout.Tooltip.Series.Any(s => s.X.Length > 0) || layout.Tooltip.Slices.Count > 0);

    /// <summary>The instructions screen readers read with the chart (aria-describedby).</summary>
    private string KeyboardHelp(ChartLayoutResult layout)
    {
        var texts = CurrentTexts;
        var polar = layout.Tooltip.Slices.Count > 0;
        var series = layout.Tooltip.Series.Count(s => s.X.Length > 0);
        var move = polar ? texts.KeysSlices : series > 1 ? texts.KeysPointsAndSeries : texts.KeysPoints;
        var zoom = Zoomable && layout.XScale is not null && layout.ZoomsX ? " " + texts.KeysZoom : "";
        return $"{move}{zoom} {(OnPointClick.HasDelegate ? texts.KeysSelect : texts.KeysLeave)}";
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

        // In pixels, so a log axis pans by the same ratio at both ends.
        var shift = (scale.RangeEnd - scale.RangeStart) * fraction;
        return ApplyRangeAsync((scale.Invert(scale.RangeStart + shift), scale.Invert(scale.RangeEnd + shift)));
    }

    /// <summary>Called by JS at the end of a drag, pan or wheel zoom (the x scale's units: log10 on a log axis).</summary>
    internal Task OnZoom(double min, double max) => ApplyRangeAsync((FromScaleUnits(min), FromScaleUnits(max)), share: false);

    /// <summary>A value from the JS module in data units: the module works in log10 units on a log x axis.</summary>
    internal double FromScaleUnits(double value) => _layout?.XScale is { IsLog: true } ? Math.Pow(10, value) : value;

    /// <summary>Called by JS on a double-click (zoom reset).</summary>
    internal Task OnZoomReset() => ApplyRangeAsync(null, share: false);

    // The Reset zoom and download buttons are 28 px squares.
    private const double ToolbarButton = 28;

    /// <summary>
    /// Where the Reset zoom button goes: in the top row, left of the download button, when it fits
    /// there (<see cref="ChartLayoutResult.TopRowFree"/>) without reaching the plot, so it does not
    /// cover the data; else inside the plot's top right corner.
    /// </summary>
    private string ResetZoomStyle(ChartLayoutResult layout)
    {
        var export = Exportable && Live && ExportFormats != ExportFormats.None ? ToolbarButton + 4 : 0;
        if (layout.TopRowFree is { } free && layout.Plot.Y >= ToolbarButton + 1 && layout.Scene.Width - export - ToolbarButton >= free.X)
        {
            return $"top:0px;right:{SvgWriter.N(export)}px";
        }

        return $"top:{SvgWriter.N(layout.Plot.Y + 4)}px;right:{SvgWriter.N(layout.Scene.Width - layout.Plot.Right + 4 + export)}px";
    }

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

    /// <param name="share">Linked charts follow (a change from code); false when it came from the JS module, which zooms them itself.</param>
    private async Task ApplyRangeAsync((double Min, double Max)? range, bool share = true)
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
        _shareZoom |= share;
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

    /// <summary>Called by JS when a linked chart (same Group) needs more space beside its plot: line up with it.</summary>
    internal void OnGroupInsets(double left, double right)
    {
        var insets = new PlotInsets(left, right);
        if (Group is not null && _groupInsets != insets)
        {
            _groupInsets = insets;
            Invalidate();
        }
    }

    /// <summary>Called by the JS ResizeObserver when the container width changes.</summary>
    internal void OnResize(double width)
    {
        if (width > 0 && Width is null && Math.Abs(width - (_measuredWidth ?? 0)) >= 1)
        {
            _measuredWidth = width;
            Invalidate();
        }
    }

    /// <summary>Releases the JS module and its event handlers.</summary>
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

    /// <summary>
    /// The object the JS module calls back into. Keeps the [JSInvokable] entry points off the
    /// component's public API; the method names are the ones the module invokes.
    /// </summary>
    private sealed class JsCallbacks(RioChart<TItem> chart)
    {
        [JSInvokable] public Task OnAnnotationDragged(string id, double value) => chart.OnAnnotationDragged(id, value);

        [JSInvokable] public Task OnPointClicked(string seriesId, int index, bool additive) => chart.OnPointClicked(seriesId, index, additive);

        [JSInvokable] public Task OnRangeSelected(double min, double max) => chart.OnRangeSelected(min, max);

        [JSInvokable] public Task OnSelectionCleared() => chart.OnSelectionCleared();

        [JSInvokable] public Task OnTooltipHover(string[] seriesIds, int[] indexes) => chart.OnTooltipHover(seriesIds, indexes);

        [JSInvokable] public Task OnZoom(double min, double max) => chart.OnZoom(min, max);

        [JSInvokable] public Task OnZoomReset() => chart.OnZoomReset();

        [JSInvokable] public void OnResize(double width) => chart.OnResize(width);

        [JSInvokable] public void OnGroupInsets(double left, double right) => chart.OnGroupInsets(left, right);
    }


}
