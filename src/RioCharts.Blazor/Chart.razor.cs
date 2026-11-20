using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Text;

namespace RioCharts.Blazor;

/// <summary>
/// A chart. Series are added as child components:
/// <code>&lt;Chart Items="readings"&gt;&lt;LineSeries XValue="r =&gt; r.Time" YValue="r =&gt; r.Temp" /&gt;&lt;/Chart&gt;</code>
/// The chart fills its container's width and lays out again when the width changes.
/// </summary>
public partial class Chart<TItem> : IChartContext
{
    private const double FallbackWidth = 640;

    private readonly string _id = "rio" + Guid.NewGuid().ToString("N")[..8];
    private readonly List<ISeriesSource> _series = [];
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
    private DotNetObjectReference<Chart<TItem>>? _dotnet;
    private bool _disposed;

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

    [Parameter] public AxisSpec? YAxis { get; set; }

    [Parameter] public LegendSpec? Legend { get; set; }

    /// <summary>Culture for labels. Default: the current culture.</summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>Ids of hidden series. Two-way bindable with <c>@bind-HiddenSeries</c>.</summary>
    [Parameter] public IReadOnlyCollection<string>? HiddenSeries { get; set; }

    [Parameter] public EventCallback<IReadOnlyCollection<string>> HiddenSeriesChanged { get; set; }

    /// <summary>Raised after a legend click has toggled a series.</summary>
    [Parameter] public EventCallback<LegendClickEventArgs> OnLegendClick { get; set; }

    /// <summary>Extra CSS class on the container.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>The latest layout: scene, scales, hit-test index and warnings.</summary>
    public ChartLayoutResult? Layout => _layout;

    /// <summary>The chart model built from the parameters and series components.</summary>
    public ChartSpec Spec => BuildSpec();

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

        // Re-layout only when the chart's own parameters changed (series report their own changes).
        var parameters = (Height, Width, Title, XAxis, YAxis, Legend, Culture?.Name);
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
        Legend = Legend ?? new LegendSpec(),
        Series = _series.Select(s => s.Spec).ToList(),
    };

    private ChartLayoutResult EnsureLayout()
    {
        if (_layout is not null && !_dirty)
        {
            return _layout;
        }

        var data = new Dictionary<string, SeriesData>(StringComparer.Ordinal);
        foreach (var s in _series)
        {
            data.TryAdd(s.Spec.Id, s.Data);
        }

        _layout = ChartLayout.Compute(BuildSpec(), data, new LayoutOptions
        {
            Width = Width ?? _measuredWidth ?? FallbackWidth,
            Height = Height,
            Measurer = _measurer,
            Culture = Culture ?? CultureInfo.CurrentCulture,
            HiddenSeries = _hidden,
        });
        _dirty = false;
        return _layout;
    }

    // ---------- Legend ----------

    private async Task HandleInteraction(NodeInteraction interaction)
    {
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

    // ---------- Responsive sizing and font metrics (JS) ----------

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _disposed)
        {
            return;
        }

        try
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/RioCharts.Blazor/Chart.razor.js");
            _dotnet = DotNetObjectReference.Create(this);
            var info = await _module.InvokeAsync<ContainerInfo>("init", _container, _dotnet);
            var changed = false;
            if (info.Widths is { Length: > 0 } widths)
            {
                _measurer = new WidthTableTextMeasurer(widths);
                changed = true;
            }

            if (info.Width > 0 && Width is null)
            {
                _measuredWidth = info.Width;
                changed = true;
            }

            if (changed)
            {
                Invalidate();
            }
        }
        catch (JSDisconnectedException)
        {
        }
        catch (TaskCanceledException)
        {
        }
        catch (JSException)
        {
            // No JS (e.g. static rendering): keep the approximate layout.
        }
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

    private sealed record ContainerInfo(double Width, double[]? Widths);
}
