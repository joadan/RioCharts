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
    /// Text for this series' value in the tooltip, e.g. <c>p =&gt; $"{p.Y:0} ms"</c> or
    /// <c>p =&gt; $"{p.Y:0} ms (target {p.Item!.Target} ms)"</c>; null keeps the default
    /// (<see cref="Format"/>). Runs once per layout for the points that can be hovered, not on
    /// pointer moves.
    /// </summary>
    [Parameter] public Func<TooltipPoint<TItem>, string?>? TooltipValue { get; set; }

    /// <summary>The series type this component registers.</summary>
    protected abstract SeriesType Type { get; }

    /// <summary>
    /// Adds what a series type needs beyond x and y (e.g. a range's low values) to data built from
    /// Items. <paramref name="groups"/>: the items of each point when aggregated, else null.
    /// </summary>
    private protected virtual SeriesData Complete(SeriesData data, IReadOnlyList<TItem> items, IReadOnlyList<int[]>? groups) => data;

    /// <summary>Settings of a series type that change how its data is built (e.g. a candle bucket): a change rebuilds it.</summary>
    private protected virtual object? DataSettings => null;

    private object? _lastDataSettings;

    bool ISeriesSource.HasTooltipValue => TooltipValue is not null;

    string? ISeriesSource.FormatTooltipValue(TooltipPointInfo point) =>
        TooltipValue?.Invoke(TooltipBuilder.Point<TItem>(point, this));

    /// <summary>The series' spec from the current parameters; derived series add their own settings.</summary>
    /// <param name="id">The id the chart assigned to the series.</param>
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

    internal SeriesSpec Spec { get; private set; } = new() { Id = "" };

    SeriesSpec ISeriesSource.Spec => Spec;

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
    internal IReadOnlyList<TItem> ItemList { get; private set; } = [];

    /// <summary>Registers the series with the chart and updates its data when Items, Data, DataVersion or Buffer changed.</summary>
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
        if (_buffer is null && (!ReferenceEquals(items, _lastItems) || aggregateKey != _lastAggregate || !Equals(DataSettings, _lastDataSettings))
            || !ReferenceEquals(Data, _lastData) || !Equals(DataVersion, _lastVersion))
        {
            _lastDataSettings = DataSettings;
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
                _data = Complete(_data, ItemList, groups);
            }
            else if (items is not null && XValue is not null && YValue is not null)
            {
                ItemList = items as IReadOnlyList<TItem> ?? items.ToList();
                _data = Complete(SeriesData.FromItems(ItemList, XValue, YValue), ItemList, null);
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
    /// <summary>Never renders: the chart draws the series.</summary>
    protected override bool ShouldRender() => false;

    /// <summary>Removes the series from the chart and stops listening to its <see cref="Buffer"/>.</summary>
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

    /// <summary>Points to mark with a dot, e.g. <c>PointHighlight.Last | PointHighlight.Max</c>. Default none.</summary>
    [Parameter] public PointHighlight Highlight { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Line;

    /// <inheritdoc />
    protected override SeriesSpec CreateSpec(string id) => base.CreateSpec(id) with { Curve = Curve, StrokeWidth = StrokeWidth, Highlight = Highlight };
}

/// <summary>Area series, filled down to zero.</summary>
public sealed class AreaSeries<TItem> : SeriesBase<TItem>
{
    /// <summary>Shape of the top edge: Linear, Monotone (smooth, no overshoot) or Step.</summary>
    [Parameter] public CurveType Curve { get; set; } = CurveType.Linear;

    /// <summary>Width of the top line in pixels. Default 2.</summary>
    [Parameter] public double StrokeWidth { get; set; } = 2;

    /// <summary>Points to mark with a dot, e.g. <c>PointHighlight.Last</c>. Default none.</summary>
    [Parameter] public PointHighlight Highlight { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Area;

    /// <inheritdoc />
    protected override SeriesSpec CreateSpec(string id) => base.CreateSpec(id) with { Curve = Curve, StrokeWidth = StrokeWidth, Highlight = Highlight };
}

/// <summary>
/// Scatter series: a marker per point, for how two measures relate, e.g. delivery distance
/// against delivery time. The tooltip shows the point nearest the pointer.
/// </summary>
public sealed class ScatterSeries<TItem> : SeriesBase<TItem>
{
    /// <summary>Marker diameter in pixels. Default 5.</summary>
    [Parameter] public double MarkerSize { get; set; } = 5;

    /// <summary>Draw a dashed least-squares trend line through the points.</summary>
    [Parameter] public bool TrendLine { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Scatter;

    /// <inheritdoc />
    protected override SeriesSpec CreateSpec(string id) => base.CreateSpec(id) with { MarkerSize = MarkerSize, TrendLine = TrendLine };
}

/// <summary>
/// One row of a heatmap: its x values are the columns, its y values the cell colors (more opaque for
/// higher values). Add one per row, e.g. per weekday; the Name labels the row. All rows share one
/// color scale (the first row's Color, Format and Max).
/// </summary>
public sealed class HeatmapSeries<TItem> : SeriesBase<TItem>
{
    /// <summary>The value that gets the full color (e.g. 100 for percentages). Default: the highest value.</summary>
    [Parameter] public double? Max { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Heatmap;

    /// <inheritdoc />
    protected override SeriesSpec CreateSpec(string id) => base.CreateSpec(id) with { Max = Max };
}

/// <summary>
/// Range area: a band between a low and a high value per x, e.g. the lowest and highest daily
/// revenue around its average. Usually drawn behind a line; the tooltip shows "low – high".
/// </summary>
public sealed class RangeAreaSeries<TItem> : SeriesBase<TItem>
{
    /// <summary>The low end of each point.</summary>
    [Parameter] public Func<TItem, double?>? LowValue { get; set; }

    /// <summary>The high end of each point (the same as <c>YValue</c>).</summary>
    [Parameter] public Func<TItem, double?>? HighValue { get; set; }

    /// <summary>Line shape of the band's edges: Linear or Monotone.</summary>
    [Parameter] public CurveType Curve { get; set; } = CurveType.Linear;

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.RangeArea;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (HighValue is not null)
        {
            YValue = HighValue;
        }

        base.OnParametersSet();
    }

    /// <inheritdoc />
    protected override SeriesSpec CreateSpec(string id) => base.CreateSpec(id) with { Curve = Curve };

    private protected override SeriesData Complete(SeriesData data, IReadOnlyList<TItem> items, IReadOnlyList<int[]>? groups)
    {
        if (LowValue is null)
        {
            return data;
        }

        if (groups is null)
        {
            return data.WithLow(items.Select(item => LowValue(item) ?? double.NaN).ToArray());
        }

        // Aggregated: the lowest low of each point's items (the high end uses Aggregate/YAggregate).
        var low = new double[groups.Count];
        for (var g = 0; g < groups.Count; g++)
        {
            low[g] = Aggregation.Apply(Core.Aggregate.Min, groups[g].Select(i => LowValue(items[i])).ToList()) ?? double.NaN;
        }

        return data.WithLow(low);
    }
}

/// <summary>
/// Box plot: the spread of values per x, e.g. supply temperatures per unit. Give the raw values
/// (<c>XValue</c> and <c>YValue</c>, one item per value: items with the same x form a box, with
/// quartiles, whiskers at 1.5 × the box length and outliers computed here), or boxes computed
/// elsewhere (<see cref="Q1Value"/>, <see cref="Q3Value"/> and the others, one item per box).
/// Several box series sit side by side like grouped bars.
/// </summary>
public sealed class BoxPlotSeries<TItem> : SeriesBase<TItem>
{
    private Func<IReadOnlyList<TItem>, double?>? _median;

    /// <summary>Precomputed boxes: the lower whisker end.</summary>
    [Parameter] public Func<TItem, double?>? LowValue { get; set; }

    /// <summary>Precomputed boxes: the lower quartile. With <see cref="Q3Value"/>, each item is one box.</summary>
    [Parameter] public Func<TItem, double?>? Q1Value { get; set; }

    /// <summary>Precomputed boxes: the median (the same as <c>YValue</c>).</summary>
    [Parameter] public Func<TItem, double?>? MedianValue { get; set; }

    /// <summary>Precomputed boxes: the upper quartile.</summary>
    [Parameter] public Func<TItem, double?>? Q3Value { get; set; }

    /// <summary>Precomputed boxes: the upper whisker end.</summary>
    [Parameter] public Func<TItem, double?>? HighValue { get; set; }

    /// <summary>Precomputed boxes: values beyond the whiskers, drawn as dots (optional).</summary>
    [Parameter] public Func<TItem, IEnumerable<double>?>? OutliersValue { get; set; }

    /// <summary>Precomputed boxes: how many values the box summarizes, for the tooltip (optional).</summary>
    [Parameter] public Func<TItem, int?>? CountValue { get; set; }

    private bool Precomputed => Q1Value is not null && Q3Value is not null;

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.BoxPlot;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (Precomputed)
        {
            YValue = MedianValue ?? YValue;
            YAggregate = null;
            Aggregate = null;
        }
        else
        {
            // Raw values: group the items by x (the medians are replaced by the boxes in Complete).
            YAggregate = _median ??= _ => 0;
            Aggregate = null;
        }

        base.OnParametersSet();
    }

    private protected override object? DataSettings => Precomputed;

    private protected override SeriesData Complete(SeriesData data, IReadOnlyList<TItem> items, IReadOnlyList<int[]>? groups)
    {
        if (groups is not null && YValue is { } value)
        {
            var boxes = new BoxSummary[groups.Count];
            for (var g = 0; g < boxes.Length; g++)
            {
                boxes[g] = BoxSummary.FromValues(groups[g].Select(i => value(items[i]) ?? double.NaN));
            }

            return data.WithBox(boxes);
        }

        if (groups is null && Precomputed)
        {
            double V(Func<TItem, double?>? f, TItem item) => f?.Invoke(item) ?? double.NaN;
            return data.WithBox(items.Select(item => BoxSummary.FromFiveNumbers(V(LowValue, item), V(Q1Value, item), V(YValue, item),
                V(Q3Value, item), V(HighValue, item), OutliersValue?.Invoke(item), CountValue?.Invoke(item) ?? 0)).ToArray());
        }

        return data;
    }
}

/// <summary>
/// Candlestick: how a value moved within each period: a wick from low to high and a body from open
/// to close, lightly filled when it rose and solid when it fell (theme colors <c>--rio-positive</c> and
/// <c>--rio-negative</c>). Give raw readings (<c>XValue</c> and <c>YValue</c>, one item per
/// reading) with a <see cref="Bucket"/> such as an hour or a day, or candles computed elsewhere
/// (<see cref="OpenValue"/>, <see cref="HighValue"/>, <see cref="LowValue"/> and <see cref="CloseValue"/>,
/// one item per period). When zoomed out, candles too narrow to see merge into wider ones.
/// </summary>
public sealed class CandlestickSeries<TItem> : SeriesBase<TItem>
{
    private Func<TItem, object?>? _time;
    private Func<TItem, object?>? _bucketed;
    private Func<IReadOnlyList<TItem>, double?>? _group;

    /// <summary>Raw readings: the period of each candle, e.g. <c>TimeBucket.Hour</c> (on the readings' wall-clock time).</summary>
    [Parameter] public TimeBucket? Bucket { get; set; }

    /// <summary>Precomputed candles: the period's first value.</summary>
    [Parameter] public Func<TItem, double?>? OpenValue { get; set; }

    /// <summary>Precomputed candles: the highest value.</summary>
    [Parameter] public Func<TItem, double?>? HighValue { get; set; }

    /// <summary>Precomputed candles: the lowest value.</summary>
    [Parameter] public Func<TItem, double?>? LowValue { get; set; }

    /// <summary>Precomputed candles: the period's last value (the same as <c>YValue</c>).</summary>
    [Parameter] public Func<TItem, double?>? CloseValue { get; set; }

    private bool Precomputed => OpenValue is not null && HighValue is not null && LowValue is not null;

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Candlestick;

    private protected override object? DataSettings => (Precomputed, Bucket);

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // XValue is wrapped to group readings per bucket; keep the caller's own selector.
        if (!ReferenceEquals(XValue, _bucketed))
        {
            _time = XValue;
        }

        if (Precomputed)
        {
            XValue = _time;
            YValue = CloseValue ?? YValue;
            YAggregate = null;
        }
        else if (Bucket is not null && _time is not null)
        {
            XValue = _bucketed ??= item => _time?.Invoke(item) is { } t && Bucket is { } b ? TimeBuckets.Floor(t, b) : _time?.Invoke(item);
            YAggregate = _group ??= _ => 0; // group per bucket; the candles replace the values in Complete
        }
        else
        {
            XValue = _time;
            YAggregate = null;
        }

        Aggregate = null;
        base.OnParametersSet();
    }

    private protected override SeriesData Complete(SeriesData data, IReadOnlyList<TItem> items, IReadOnlyList<int[]>? groups)
    {
        if (groups is not null && YValue is { } value && _time is { } time)
        {
            var candles = new CandleSummary[groups.Count];
            for (var g = 0; g < candles.Length; g++)
            {
                // In time order within the bucket, whatever the order of the items.
                var ordered = groups[g].OrderBy(i => time(items[i]) is { } t ? AxisValue.ToDouble(t, XKind.Time) : double.NaN).ThenBy(i => i);
                candles[g] = CandleSummary.FromValues(ordered.Select(i => value(items[i]) ?? double.NaN));
            }

            return data.WithCandles(candles);
        }

        if (groups is null && Precomputed)
        {
            double V(Func<TItem, double?>? f, TItem item) => f?.Invoke(item) ?? double.NaN;
            return data.WithCandles(items.Select(item => new CandleSummary(V(OpenValue, item), V(HighValue, item), V(LowValue, item), V(YValue, item))).ToArray());
        }

        return data;
    }
}

/// <summary>Bar series. Several bar series in one chart are grouped side by side.</summary>
public sealed class BarSeries<TItem> : SeriesBase<TItem>
{
    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Bar;
}

/// <summary>
/// Pie: one slice per category (the x value), sized by its share of the total. Several pie
/// series in one chart are drawn as nested rings, the first one outermost.
/// </summary>
public class PieSeries<TItem> : SeriesBase<TItem>
{
    /// <summary>
    /// Inner radius as a fraction of the outer radius; above 0 makes a donut. Default: 0 for one
    /// pie, 0.4 when several pie series are drawn as rings.
    /// </summary>
    [Parameter] public double? InnerRadius { get; set; }

    /// <summary>Start angle in degrees, clockwise from 12 o'clock. Default 0.</summary>
    [Parameter] public double? StartAngle { get; set; }

    /// <summary>End angle in degrees. Default: a full circle. 180 with StartAngle -90 makes a half donut.</summary>
    [Parameter] public double? EndAngle { get; set; }

    /// <summary>Group slices below this share of the total (e.g. 0.03) into one "Other" slice.</summary>
    [Parameter] public double? GroupSmallSlices { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Pie;

    /// <inheritdoc />
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
    /// <summary>Creates a donut series with an inner radius of 0.6.</summary>
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
    [Parameter] public double? StartAngle { get; set; }

    /// <summary>End angle in degrees. Default 270.</summary>
    [Parameter] public double? EndAngle { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.RadialBar;

    /// <inheritdoc />
    protected override SeriesSpec CreateSpec(string id) => base.CreateSpec(id) with
    {
        Max = Max,
        InnerRadius = InnerRadius,
        StartAngle = StartAngle,
        EndAngle = EndAngle,
    };
}
