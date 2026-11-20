using Microsoft.AspNetCore.Components;
using RioCharts.Core;
using RioCharts.Core.Data;

namespace RioCharts.Blazor;

/// <summary>
/// Base for series components. A series renders nothing itself: it turns its parameters into
/// a <see cref="SeriesSpec"/> and columnar <see cref="SeriesData"/> and registers with the chart.
/// Data is reprocessed only when <see cref="Items"/> or <see cref="Data"/> is a new reference or
/// <see cref="DataVersion"/> changes; changing a list in place does not trigger an update.
/// </summary>
public abstract class SeriesBase<TItem> : ComponentBase, ISeriesSource, IDisposable
{
    private object? _lastItems;
    private SeriesData? _lastData;
    private object? _lastVersion;
    private (Aggregate?, bool) _lastAggregate;
    private IReadOnlyList<int[]>? _groups;
    private bool _registered;
    private bool _hadTooltipValue;
    private string _id = "";

    [CascadingParameter] internal IChartContext? Chart { get; set; }

    /// <summary>Unique id (used by <c>HiddenSeries</c>). Defaults to <see cref="Name"/>, else generated.</summary>
    [Parameter] public string? Id { get; set; }

    /// <summary>Name shown in the legend.</summary>
    [Parameter] public string? Name { get; set; }

    /// <summary>Items to plot. Defaults to the chart's Items.</summary>
    [Parameter] public IEnumerable<TItem>? Items { get; set; }

    /// <summary>X value: a number, DateTime, DateTimeOffset, DateOnly or string (category).</summary>
    [Parameter] public Func<TItem, object?>? XValue { get; set; }

    /// <summary>Y value; null leaves a gap.</summary>
    [Parameter] public Func<TItem, double?>? YValue { get; set; }

    /// <summary>
    /// Combines items with the same x into one point: the Sum, Average, Min, Max or Last of
    /// <see cref="YValue"/>, or the Count of items (needs no YValue). Use <see cref="YAggregate"/>
    /// for anything else. Without either, items with the same x are drawn on top of each other.
    /// </summary>
    [Parameter] public Aggregate? Aggregate { get; set; }

    /// <summary>
    /// Combines items with the same x into one point, with y computed from the group, e.g.
    /// <c>g => g.Sum(o => o.Amount)</c>; null leaves a gap. Used instead of <see cref="YValue"/>
    /// and <see cref="Aggregate"/> when set. A clicked point's items are in
    /// <c>PointClickEventArgs.Items</c>.
    /// </summary>
    [Parameter] public Func<IReadOnlyList<TItem>, double?>? YAggregate { get; set; }

    /// <summary>Columnar data (fast path for large series); used instead of Items when set.</summary>
    [Parameter] public SeriesData? Data { get; set; }

    /// <summary>Change this to reprocess the data after changing Items in place.</summary>
    [Parameter] public object? DataVersion { get; set; }

    /// <summary>CSS color. Defaults to the next palette color.</summary>
    [Parameter] public string? Color { get; set; }

    /// <summary>Draw order override (default: areas, then bars, then lines).</summary>
    [Parameter] public int? ZIndex { get; set; }

    /// <summary>Id of the vertical axis to use (see <c>Chart.YAxes</c>). Default: the primary axis.</summary>
    [Parameter] public string? YAxisId { get; set; }

    /// <summary>Stack group: series of the same type with the same stack name are stacked.</summary>
    [Parameter] public string? Stack { get; set; }

    /// <summary>Show values on the chart. Default: off, except for pie, donut and radial bar.</summary>
    [Parameter] public bool? DataLabels { get; set; }

    /// <summary>.NET format string for values in tooltips and data labels, e.g. "0.0".</summary>
    [Parameter] public string? Format { get; set; }

    /// <summary>
    /// Text for this series' value in the tooltip, e.g. <c>p =&gt; $"{p.Y:0.0} °C"</c> or
    /// <c>p =&gt; $"{p.Y:0.0} °C (setpoint {p.Item!.Setpoint} °C)"</c>; null keeps the default
    /// (<see cref="Format"/>). Runs once per layout for the points that can be hovered, not on
    /// pointer moves.
    /// </summary>
    [Parameter] public Func<TooltipPoint<TItem>, string?>? TooltipValue { get; set; }

    protected abstract SeriesType Type { get; }

    bool ISeriesSource.HasTooltipValue => TooltipValue is not null;

    string? ISeriesSource.FormatTooltipValue(TooltipPointInfo point) =>
        TooltipValue?.Invoke(TooltipBuilder.Point<TItem>(point, this));

    protected virtual SeriesSpec CreateSpec(string id) => new()
    {
        Id = id,
        Type = Type,
        Name = Name,
        Color = Color,
        ZIndex = ZIndex,
        YAxisId = YAxisId,
        Stack = Stack,
        DataLabels = DataLabels,
        Format = Format,
    };

    object? ISeriesSource.ItemAt(int inputIndex) =>
        inputIndex >= 0 && inputIndex < ItemList.Count ? ItemList[inputIndex] : null;

    IReadOnlyList<int> ISeriesSource.InputIndexes(int sourceIndex) =>
        _groups is null ? [sourceIndex] : sourceIndex >= 0 && sourceIndex < _groups.Count ? _groups[sourceIndex] : [];

    public SeriesSpec Spec { get; private set; } = new() { Id = "" };

    SeriesData ISeriesSource.Data
    {
        get
        {
            // Live data: take a fresh snapshot only when the buffer changed since the last layout.
            if (_buffer is { } buffer && buffer.Version != _bufferVersion)
            {
                _bufferVersion = buffer.Version;
                _data = buffer.ToSeriesData();
            }

            return _data;
        }
    }

    private SeriesData _data = SeriesData.Empty;
    private SeriesBuffer? _buffer;
    private long _bufferVersion = -1;

    /// <summary>
    /// Live data (used instead of Items and Data when set). Append to it from any thread; the
    /// chart redraws at most every <c>Chart.RefreshInterval</c> milliseconds.
    /// </summary>
    [Parameter] public SeriesBuffer? Buffer { get; set; }

    private void OnBufferChanged() => Chart?.DataArrived();

    /// <summary>The items behind the plotted points (index = the point's source index).</summary>
    public IReadOnlyList<TItem> ItemList { get; private set; } = [];

    protected override void OnParametersSet()
    {
        if (Chart is null)
        {
            throw new InvalidOperationException($"{GetType().Name} must be placed inside a <RioChart>.");
        }

        if (!_registered)
        {
            _id = Chart.Register(this, Id ?? Name);
            _registered = true;
        }

        var changed = false;
        if (!ReferenceEquals(Buffer, _buffer))
        {
            if (_buffer is not null)
            {
                _buffer.Changed -= OnBufferChanged;
            }

            _buffer = Buffer;
            _bufferVersion = -1;
            if (_buffer is not null)
            {
                _buffer.Changed += OnBufferChanged;
                ItemList = [];
                _groups = null;
            }
            else
            {
                _lastItems = _lastData = null; // fall back to Items/Data below
                _lastVersion = new object();
            }

            changed = true;
        }

        var items = Items ?? Chart.DefaultItems as IEnumerable<TItem>;
        var aggregateKey = (Aggregate, YAggregate is not null);
        if (_buffer is null && (!ReferenceEquals(items, _lastItems) || aggregateKey != _lastAggregate)
            || !ReferenceEquals(Data, _lastData) || !Equals(DataVersion, _lastVersion))
        {
            _lastItems = items;
            _lastData = Data;
            _lastVersion = DataVersion;
            _lastAggregate = aggregateKey;
            _groups = null;
            var aggregate = YAggregate ?? (Aggregate is { } a ? Aggregation.For(a, YValue) : null);
            if (Data is not null)
            {
                _data = Data;
                ItemList = [];
            }
            else if (items is not null && XValue is not null && aggregate is not null)
            {
                ItemList = items as IReadOnlyList<TItem> ?? items.ToList();
                _data = SeriesData.FromGroups(ItemList, XValue, aggregate, out var groups);
                _groups = groups;
            }
            else if (items is not null && XValue is not null && YValue is not null)
            {
                ItemList = items as IReadOnlyList<TItem> ?? items.ToList();
                _data = SeriesData.FromItems(ItemList, XValue, YValue);
            }
            else
            {
                _data = SeriesData.Empty;
                ItemList = [];
            }

            changed = true;
        }

        var spec = CreateSpec(_id);
        if (spec != Spec)
        {
            Spec = spec;
            changed = true;
        }

        // Adding or removing a formatter re-applies tooltips (a new lambda each render does not).
        if ((TooltipValue is not null) != _hadTooltipValue)
        {
            _hadTooltipValue = TooltipValue is not null;
            changed = true;
        }

        if (changed)
        {
            Chart.SeriesChanged(this);
        }
    }

    // Series draw nothing themselves; the chart renders the scene.
    protected override bool ShouldRender() => false;

    public void Dispose()
    {
        if (_buffer is not null)
        {
            _buffer.Changed -= OnBufferChanged;
        }

        Chart?.Unregister(this);
        GC.SuppressFinalize(this);
    }
}

/// <summary>Line series.</summary>
public sealed class LineSeries<TItem> : SeriesBase<TItem>
{
    /// <summary>Line shape: Linear, Monotone (smooth, no overshoot) or Step.</summary>
    [Parameter] public CurveType Curve { get; set; } = CurveType.Linear;

    /// <summary>Line width in pixels.</summary>
    [Parameter] public double StrokeWidth { get; set; } = 2;

    protected override SeriesType Type => SeriesType.Line;

    protected override SeriesSpec CreateSpec(string id) => base.CreateSpec(id) with { Curve = Curve, StrokeWidth = StrokeWidth };
}

/// <summary>Area series, filled down to zero.</summary>
public sealed class AreaSeries<TItem> : SeriesBase<TItem>
{
    [Parameter] public CurveType Curve { get; set; } = CurveType.Linear;
    [Parameter] public double StrokeWidth { get; set; } = 2;

    protected override SeriesType Type => SeriesType.Area;

    protected override SeriesSpec CreateSpec(string id) => base.CreateSpec(id) with { Curve = Curve, StrokeWidth = StrokeWidth };
}

/// <summary>Bar series. Several bar series in one chart are grouped side by side.</summary>
public sealed class BarSeries<TItem> : SeriesBase<TItem>
{
    protected override SeriesType Type => SeriesType.Bar;
}

/// <summary>
/// Pie: one slice per category (the x value), sized by its share of the total. Several pie
/// series in one chart are drawn as nested rings, the first one outermost.
/// </summary>
public class PieSeries<TItem> : SeriesBase<TItem>
{
    /// <summary>Inner radius as a fraction of the outer radius; above 0 makes a donut.</summary>
    [Parameter] public double InnerRadius { get; set; }

    /// <summary>Start angle in degrees, clockwise from 12 o'clock.</summary>
    [Parameter] public double StartAngle { get; set; }

    /// <summary>End angle in degrees. Default: a full circle. 180 with StartAngle -90 makes a half donut.</summary>
    [Parameter] public double? EndAngle { get; set; }

    /// <summary>Group slices below this share of the total (e.g. 0.03) into one "Other" slice.</summary>
    [Parameter] public double? GroupSmallSlices { get; set; }

    protected override SeriesType Type => SeriesType.Pie;

    protected override SeriesSpec CreateSpec(string id) => base.CreateSpec(id) with
    {
        InnerRadius = InnerRadius,
        StartAngle = StartAngle,
        EndAngle = EndAngle,
        GroupSmallSlices = GroupSmallSlices,
    };
}

/// <summary>A pie with a hole (inner radius 0.6 by default); shows the total in the middle.</summary>
public sealed class DonutSeries<TItem> : PieSeries<TItem>
{
    public DonutSeries() => InnerRadius = 0.6;
}

/// <summary>Radial bars: one circular track per category, filled up to its value.</summary>
public sealed class RadialBarSeries<TItem> : SeriesBase<TItem>
{
    /// <summary>The value of a full track, e.g. 100 for percentages. Default: a nice value above the largest.</summary>
    [Parameter] public double? Max { get; set; }

    /// <summary>Inner radius as a fraction of the outer radius. Default 0.3.</summary>
    [Parameter] public double? InnerRadius { get; set; }

    /// <summary>Start angle in degrees, clockwise from 12 o'clock. Default 0.</summary>
    [Parameter] public double StartAngle { get; set; }

    /// <summary>End angle in degrees. Default 270.</summary>
    [Parameter] public double EndAngle { get; set; } = 270;

    protected override SeriesType Type => SeriesType.RadialBar;

    protected override SeriesSpec CreateSpec(string id) => base.CreateSpec(id) with
    {
        Max = Max,
        InnerRadius = InnerRadius,
        StartAngle = StartAngle,
        EndAngle = EndAngle,
    };
}
