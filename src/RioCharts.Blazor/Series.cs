using Microsoft.AspNetCore.Components;
using RioCharts.Core;
using RioCharts.Core.Data;

namespace RioCharts.Blazor;

/// <summary>
/// Base for series components. A series renders nothing itself: it turns its parameters into
/// a <see cref="SeriesOptions"/> and columnar <see cref="SeriesData"/> and registers with the chart.
/// Data is reprocessed only when <see cref="Items"/> or <see cref="Data"/> is a new reference or
/// <see cref="DataVersion"/> changes; changing a list in place does not trigger an update.
/// </summary>
public abstract class SeriesBase<TItem> : ComponentBase, ISeriesSource, IDisposable
{
    private object? _lastItems;
    private SeriesData? _lastData;
    private object? _lastVersion;
    private (Aggregate?, bool, TimeBucket?) _lastAggregate;
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

    /// <summary>
    /// A color per item, e.g. <c>o =&gt; o.Late ? "--rio-negative" : null</c>: a CSS color or a
    /// theme token (tokens follow light and dark themes); null keeps the series color. Bars,
    /// scatter markers, pie slices, radial bars and treemap items take the color; lines and areas
    /// mark the point with a dot in it. Aggregated points take their first item's color. Runs once
    /// per data change, like the other selectors.
    /// </summary>
    [Parameter] public Func<TItem, string?>? ColorValue { get; set; }

    private bool _hadColorValue;

    /// <summary>
    /// Colors by value, e.g. <c>[new() { Above = 120, Color = "--rio-negative" }]</c>: a line or
    /// area changes color where it crosses into a band, a bar or scatter point takes the color of
    /// the band its value is in. Later bands win where they overlap; <see cref="ColorValue"/> wins
    /// over bands.
    /// </summary>
    [Parameter] public IReadOnlyList<ColorBand>? ColorBands { get; set; }

    private IReadOnlyList<ColorBand>? _bands;

    // The same bands as last time keep their list, so a list written inline (new each render) does
    // not change the definition and lay the chart out again. A few small records, not data.
    private IReadOnlyList<ColorBand>? StableBands()
    {
        if (ColorBands is null || _bands is null || !ColorBands.SequenceEqual(_bands))
        {
            _bands = ColorBands is null ? null : [.. ColorBands];
        }

        return _bands;
    }

    /// <summary>Draw order override (default: areas, then bars, then lines).</summary>
    [Parameter] public int? ZIndex { get; set; }

    /// <summary>Id of the vertical axis to use (see <c>RioChart.YAxes</c>). Default: the primary axis.</summary>
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

    /// <summary>
    /// Builds the data from Items in a way of the series type's own (e.g. a histogram's bins), with
    /// each point's items in <paramref name="groups"/>; null builds it from <c>XValue</c> and <c>YValue</c> as usual.
    /// </summary>
    private protected virtual SeriesData? BuildData(IReadOnlyList<TItem> items, out IReadOnlyList<int[]>? groups)
    {
        groups = null;
        return null;
    }

    /// <summary>Data a series type makes without items (e.g. a gauge's single <c>Value</c>), used instead of Items when not null.</summary>
    private protected virtual SeriesData? OwnData => null;

    /// <summary>
    /// The time bucket items are grouped into (one point per bucket), or null to group only items
    /// with the same x. Series types with a <c>Bucket</c> parameter return it.
    /// </summary>
    private protected virtual TimeBucket? XBucket => null;

    /// <summary>Settings of a series type that change how its data is built (e.g. a candle bucket): a change rebuilds it.</summary>
    private protected virtual object? DataSettings => null;

    private object? _lastDataSettings;

    bool ISeriesSource.HasTooltipValue => TooltipValue is not null;

    string? ISeriesSource.FormatTooltipValue(TooltipPointInfo point) =>
        TooltipValue?.Invoke(TooltipBuilder.Point<TItem>(point, this));

    /// <summary>The series' options from the current parameters; derived series add their own settings.</summary>
    /// <param name="id">The id the chart assigned to the series.</param>
    protected virtual SeriesOptions CreateOptions(string id) => new()
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
        ColorBands = StableBands(),
    };

    object? ISeriesSource.ItemAt(int inputIndex) =>
        inputIndex >= 0 && inputIndex < ItemList.Count ? ItemList[inputIndex] : null;

    IReadOnlyList<int> ISeriesSource.InputIndexes(int sourceIndex) =>
        _groups is null ? [sourceIndex] : sourceIndex >= 0 && sourceIndex < _groups.Count ? _groups[sourceIndex] : [];

    internal SeriesOptions Spec { get; private set; } = new() { Id = "" };

    SeriesOptions ISeriesSource.Spec => Spec;

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
    /// chart redraws at most every <c>RioChart.RefreshInterval</c> milliseconds.
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
        var aggregateKey = (Aggregate, YAggregate is not null, XBucket);
        if (_buffer is null && (!ReferenceEquals(items, _lastItems) || aggregateKey != _lastAggregate || !Equals(DataSettings, _lastDataSettings)
                || (ColorValue is not null) != _hadColorValue)
            || !ReferenceEquals(Data, _lastData) || !Equals(DataVersion, _lastVersion))
        {
            _lastDataSettings = DataSettings;
            _hadColorValue = ColorValue is not null;
            _lastItems = items;
            _lastData = Data;
            _lastVersion = DataVersion;
            _lastAggregate = aggregateKey;
            _groups = null;
            var bucket = XBucket;
            // Per bucket without an aggregate: the average, as in JSON definitions.
            var aggregate = YAggregate ?? (Aggregate is { } a ? Aggregation.For(a, YValue)
                : bucket is not null && YValue is not null ? Aggregation.For(Core.Aggregate.Average, YValue) : null);
            if (Data is not null)
            {
                _data = Data;
                ItemList = [];
            }
            else if (OwnData is { } own0)
            {
                _data = own0;
                ItemList = [];
            }
            else if (items is not null && BuildData(ItemList = items as IReadOnlyList<TItem> ?? items.ToList(), out var own) is { } built)
            {
                _data = built;
                _groups = own;
            }
            else if (items is not null && XValue is not null && aggregate is not null)
            {
                ItemList = items as IReadOnlyList<TItem> ?? items.ToList();
                _data = bucket is { } perBucket
                    ? SeriesData.FromTimeBuckets(ItemList, XValue, perBucket, aggregate, out var groups)
                    : SeriesData.FromGroups(ItemList, XValue, aggregate, out groups);
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

            // Each point's color from its item (a group's first item), in input order like the y values.
            if (ColorValue is { } colorOf && Data is null && ItemList.Count > 0)
            {
                _data = _data.WithColors(_groups is { } grouped
                    ? [.. grouped.Select(g => g.Length > 0 ? colorOf(ItemList[g[0]]) : null)]
                    : [.. ItemList.Select(colorOf)]);
            }

            changed = true;
        }

        var spec = CreateOptions(_id);
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

/// <summary>
/// A series that can show error bars: <see cref="LineSeries{TItem}"/>, <see cref="BarSeries{TItem}"/>
/// and <see cref="ScatterSeries{TItem}"/>. Give a plus-or-minus amount (<see cref="Error"/> for every
/// point, or <see cref="ErrorValue"/> per item), or each error bar's ends
/// (<see cref="ErrorLowValue"/> and <see cref="ErrorHighValue"/>).
/// </summary>
public abstract class ErrorBarSeriesBase<TItem> : SeriesBase<TItem>
{
    /// <summary>Error bars: the same plus-or-minus amount for every point, e.g. 2.5.</summary>
    [Parameter] public double? Error { get; set; }

    /// <summary>
    /// Error bars: each item's plus-or-minus amount, e.g. <c>m =&gt; m.StdDev</c>; null for none.
    /// Items combined into one point take the average.
    /// </summary>
    [Parameter] public Func<TItem, double?>? ErrorValue { get; set; }

    /// <summary>
    /// Error bars: the low end of each item's error bar (a value, e.g. a confidence interval's lower
    /// bound); use with <see cref="ErrorHighValue"/>. Items combined into one point take the lowest.
    /// </summary>
    [Parameter] public Func<TItem, double?>? ErrorLowValue { get; set; }

    /// <summary>Error bars: the high end of each item's error bar; use with <see cref="ErrorLowValue"/>. Items combined into one point take the highest.</summary>
    [Parameter] public Func<TItem, double?>? ErrorHighValue { get; set; }

    /// <summary>Settings of the derived series that change how its data is built.</summary>
    private protected virtual object? OwnDataSettings => null;

    private protected sealed override object? DataSettings => (ErrorValue is not null, ErrorLowValue is not null, ErrorHighValue is not null, OwnDataSettings);

    /// <summary>What the derived series adds to its data before the error bars.</summary>
    private protected virtual SeriesData CompleteOwn(SeriesData data, IReadOnlyList<TItem> items, IReadOnlyList<int[]>? groups) => data;

    private protected sealed override SeriesData Complete(SeriesData data, IReadOnlyList<TItem> items, IReadOnlyList<int[]>? groups)
    {
        data = CompleteOwn(data, items, groups);
        if (ErrorValue is null && (ErrorLowValue is null || ErrorHighValue is null))
        {
            return data;
        }

        // Each point's y in input order (index = input item or group), for the plus-or-minus amounts.
        var count = groups?.Count ?? items.Count;
        var y = new double[count];
        Array.Fill(y, double.NaN);
        for (var i = 0; i < data.Count; i++)
        {
            var source = data.ToSourceIndex(i);
            if (source >= 0 && source < count)
            {
                y[source] = data.Y[i];
            }
        }

        double Combine(Func<TItem, double?> value, int point, Core.Aggregate aggregate) => groups is null
            ? value(items[point]) ?? double.NaN
            : Aggregation.Apply(aggregate, groups[point].Select(i => value(items[i])).ToList()) ?? double.NaN;

        var low = new double[count];
        var high = new double[count];
        for (var p = 0; p < count; p++)
        {
            if (ErrorValue is { } amount)
            {
                var e = Math.Abs(Combine(amount, p, Core.Aggregate.Average));
                (low[p], high[p]) = (y[p] - e, y[p] + e);
            }
            else
            {
                (low[p], high[p]) = (Combine(ErrorLowValue!, p, Core.Aggregate.Min), Combine(ErrorHighValue!, p, Core.Aggregate.Max));
            }
        }

        return data.WithErrors(low, high);
    }

    /// <inheritdoc />
    protected override SeriesOptions CreateOptions(string id) => base.CreateOptions(id) with { Error = Error };
}

/// <summary>Line series.</summary>
public sealed class LineSeries<TItem> : ErrorBarSeriesBase<TItem>
{
    /// <summary>
    /// Time x: one point per bucket (e.g. <c>TimeBucket.Day</c> or <c>TimeBucket.Month</c>, on the
    /// items' wall-clock time) at the bucket's start, combining its items with <c>Aggregate</c>
    /// (average when not set) or <c>YAggregate</c>. <c>TimeBucket.Auto</c> picks the bucket from the
    /// data's time span (about 50 buckets).
    /// </summary>
    [Parameter] public TimeBucket? Bucket { get; set; }

    /// <summary>Line shape: Linear, Monotone (smooth, no overshoot) or Step.</summary>
    [Parameter] public CurveType Curve { get; set; } = CurveType.Linear;

    /// <summary>Line width in pixels.</summary>
    [Parameter] public double StrokeWidth { get; set; } = 2;

    /// <summary>Points to mark with a dot, e.g. <c>PointHighlight.Last | PointHighlight.Max</c>. Default none.</summary>
    [Parameter] public PointHighlight Highlight { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Line;

    private protected override TimeBucket? XBucket => Bucket;

    /// <inheritdoc />
    protected override SeriesOptions CreateOptions(string id) => base.CreateOptions(id) with { Curve = Curve, StrokeWidth = StrokeWidth, Highlight = Highlight };
}

/// <summary>Area series, filled down to zero.</summary>
public sealed class AreaSeries<TItem> : SeriesBase<TItem>
{
    /// <summary>
    /// Time x: one point per bucket (e.g. <c>TimeBucket.Day</c> or <c>TimeBucket.Month</c>, on the
    /// items' wall-clock time) at the bucket's start, combining its items with <c>Aggregate</c>
    /// (average when not set) or <c>YAggregate</c>. <c>TimeBucket.Auto</c> picks the bucket from the
    /// data's time span (about 50 buckets).
    /// </summary>
    [Parameter] public TimeBucket? Bucket { get; set; }

    /// <summary>Shape of the top edge: Linear, Monotone (smooth, no overshoot) or Step.</summary>
    [Parameter] public CurveType Curve { get; set; } = CurveType.Linear;

    /// <summary>Width of the top line in pixels. Default 2.</summary>
    [Parameter] public double StrokeWidth { get; set; } = 2;

    /// <summary>Points to mark with a dot, e.g. <c>PointHighlight.Last</c>. Default none.</summary>
    [Parameter] public PointHighlight Highlight { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Area;

    private protected override TimeBucket? XBucket => Bucket;

    /// <inheritdoc />
    protected override SeriesOptions CreateOptions(string id) => base.CreateOptions(id) with { Curve = Curve, StrokeWidth = StrokeWidth, Highlight = Highlight };
}

/// <summary>
/// Scatter series: a marker per point, for how two measures relate, e.g. delivery distance
/// against delivery time. With <see cref="SizeValue"/> it is a bubble chart: a third measure sets
/// each marker's size. The tooltip shows the point nearest the pointer.
/// </summary>
public sealed class ScatterSeries<TItem> : ErrorBarSeriesBase<TItem>
{
    /// <summary>
    /// Marker diameter in pixels. Default 5. Bubbles: the diameter of the largest bubble; default
    /// from the chart size.
    /// </summary>
    [Parameter] public double? MarkerSize { get; set; }

    /// <summary>Draw a dashed least-squares trend line through the points.</summary>
    [Parameter] public bool TrendLine { get; set; }

    /// <summary>
    /// Each item's size, e.g. <c>c =&gt; c.Population</c>, which makes a bubble chart: a bubble's
    /// area is proportional to its size, and all bubble series in a chart share one size scale.
    /// Sizes of zero or less, or null, are not drawn. Aggregated points take the sum of their items' sizes.
    /// </summary>
    [Parameter] public Func<TItem, double?>? SizeValue { get; set; }

    /// <summary>Bubbles: what the size is, shown with it in the tooltip, e.g. "Population". Default: "Size" in the chart's language.</summary>
    [Parameter] public string? SizeName { get; set; }

    /// <summary>Bubbles: .NET format string for the size in tooltips, e.g. "0 M".</summary>
    [Parameter] public string? SizeFormat { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Scatter;

    private protected override object? OwnDataSettings => SizeValue is not null;

    private protected override SeriesData CompleteOwn(SeriesData data, IReadOnlyList<TItem> items, IReadOnlyList<int[]>? groups)
    {
        if (SizeValue is not { } size)
        {
            return data;
        }

        return data.WithSizes(groups is null
            ? [.. items.Select(item => size(item) ?? double.NaN)]
            : [.. groups.Select(g => g.Select(i => size(items[i])).Where(v => v is not null).Sum() ?? double.NaN)]);
    }

    /// <inheritdoc />
    protected override SeriesOptions CreateOptions(string id) => base.CreateOptions(id) with
    {
        MarkerSize = MarkerSize, TrendLine = TrendLine, SizeName = SizeName, SizeFormat = SizeFormat,
    };
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
    protected override SeriesOptions CreateOptions(string id) => base.CreateOptions(id) with { Max = Max };
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
    protected override SeriesOptions CreateOptions(string id) => base.CreateOptions(id) with { Curve = Curve };

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
/// to close, lightly filled when it rose and solid when it fell, in the series color (or green and red with
/// <see cref="DirectionColors"/>). Give raw readings (<c>XValue</c> and <c>YValue</c>, one item per
/// reading) with a <see cref="Bucket"/> such as an hour or a day, or candles computed elsewhere
/// (<see cref="OpenValue"/>, <see cref="HighValue"/>, <see cref="LowValue"/> and <see cref="CloseValue"/>,
/// one item per period). When zoomed out, candles too narrow to see merge into wider ones.
/// </summary>
public sealed class CandlestickSeries<TItem> : SeriesBase<TItem>
{
    private Func<IReadOnlyList<TItem>, double?>? _group;

    /// <summary>
    /// Raw readings: the period of each candle, e.g. <c>TimeBucket.Hour</c> (on the readings'
    /// wall-clock time); <c>TimeBucket.Auto</c> picks it from the data's time span (about 50 candles).
    /// </summary>
    [Parameter] public TimeBucket? Bucket { get; set; }

    /// <summary>
    /// Color candles by direction, rising in <c>--rio-positive</c> and falling in <c>--rio-negative</c> (green and red),
    /// with the change in percent in the tooltip, as for prices. Off (default): the series color, with a light fill for
    /// rising and a solid fill for falling candles.
    /// </summary>
    [Parameter] public bool DirectionColors { get; set; }

    /// <inheritdoc />
    protected override SeriesOptions CreateOptions(string id) => base.CreateOptions(id) with { DirectionColors = DirectionColors };

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

    private protected override object? DataSettings => Precomputed;

    private protected override TimeBucket? XBucket => Precomputed ? null : Bucket;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (Precomputed)
        {
            YValue = CloseValue ?? YValue;
            YAggregate = null;
        }
        else
        {
            // Group per bucket; the candles replace the values in Complete.
            YAggregate = Bucket is null ? null : _group ??= _ => 0;
        }

        Aggregate = null;
        base.OnParametersSet();
    }

    private protected override SeriesData Complete(SeriesData data, IReadOnlyList<TItem> items, IReadOnlyList<int[]>? groups)
    {
        if (groups is not null && YValue is { } value && XValue is { } time)
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
public sealed class BarSeries<TItem> : ErrorBarSeriesBase<TItem>
{
    /// <summary>
    /// Time x: one point per bucket (e.g. <c>TimeBucket.Day</c> or <c>TimeBucket.Month</c>, on the
    /// items' wall-clock time) at the bucket's start, combining its items with <c>Aggregate</c>
    /// (average when not set) or <c>YAggregate</c>. <c>TimeBucket.Auto</c> picks the bucket from the
    /// data's time span (about 50 buckets).
    /// </summary>
    [Parameter] public TimeBucket? Bucket { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Bar;

    private protected override TimeBucket? XBucket => Bucket;
}

/// <summary>
/// Histogram: how values are distributed, e.g. response times. Give one item per value:
/// <c>XValue</c> is the value to count (a number). The values are counted in equal-width bins with
/// round edges (<see cref="BinWidth"/> or <see cref="BinCount"/>, picked from the data when not set),
/// drawn as bars that touch. The tooltip shows the bin's range and count; a click gives its items.
/// Several histograms on one chart overlay, see-through.
/// </summary>
public sealed class HistogramSeries<TItem> : SeriesBase<TItem>
{
    private Bins _bins;

    /// <summary>The width of each bin, in x units (e.g. 5 for bins of 5 ms). Default: a nice width picked from the data.</summary>
    [Parameter] public double? BinWidth { get; set; }

    /// <summary>About how many bins, when <see cref="BinWidth"/> is not set (the width is rounded to a nice number). Default: picked from the data.</summary>
    [Parameter] public int? BinCount { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Histogram;

    private protected override object? DataSettings => (BinWidth, BinCount);

    /// <inheritdoc />
    protected override SeriesOptions CreateOptions(string id) =>
        base.CreateOptions(id) with { BinWidth = _bins.Count > 0 ? _bins.Width : BinWidth, BinCount = BinCount };

    private protected override SeriesData? BuildData(IReadOnlyList<TItem> items, out IReadOnlyList<int[]>? groups)
    {
        groups = null;
        if (XValue is not { } value)
        {
            return null;
        }

        var data = SeriesData.FromBins(items, item => AxisValue.ToDouble(value(item), XKind.Numeric) is var v && double.IsFinite(v) ? v : null,
            BinWidth, BinCount, out var bins, out _bins);
        groups = bins;
        return data;
    }
}

/// <summary>
/// Range bars (a timeline): a bar from each item's <see cref="StartValue"/> to its
/// <see cref="EndValue"/> (times, or numbers) in the row <see cref="RowValue"/> gives, e.g. a
/// project schedule (one row per task) or states over time (one row per machine). Rows are listed
/// top to bottom in the order they first appear. Several range bar series share the rows and the
/// time axis, so a state timeline is one series per state (running, idle, stopped), each with its
/// color and legend entry. The tooltip shows the row, start, end and duration. Cannot be mixed with
/// other series types; x lines and bands (<c>Annotations</c>) mark times such as "now".
/// </summary>
public sealed class RangeBarSeries<TItem> : SeriesBase<TItem>
{
    /// <summary>The row of each item, e.g. <c>t =&gt; t.Machine</c>. Default: every bar in one row, named after the series.</summary>
    [Parameter] public Func<TItem, string?>? RowValue { get; set; }

    /// <summary>Where each bar starts: a DateTime, DateTimeOffset, DateOnly or number. Items without a start are left out.</summary>
    [Parameter] public Func<TItem, object?>? StartValue { get; set; }

    /// <summary>Where each bar ends, of the same kind as <see cref="StartValue"/>. A missing end, or one before the start, draws nothing.</summary>
    [Parameter] public Func<TItem, object?>? EndValue { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.RangeBar;

    private protected override object? DataSettings => RowValue is not null;

    private protected override SeriesData? BuildData(IReadOnlyList<TItem> items, out IReadOnlyList<int[]>? groups)
    {
        groups = null;
        return StartValue is { } start && EndValue is { } end ? SeriesData.FromRanges(items, RowValue, start, end) : SeriesData.Empty;
    }
}

/// <summary>
/// Waterfall: how a start value becomes an end value, e.g. a month's result from revenue through
/// costs. Each item is a change (<c>YValue</c>), drawn as a bar from the running total before it to
/// the one after: in <c>--rio-positive</c> (green) when it rises and <c>--rio-negative</c> (red) when
/// it falls. Items marked with <see cref="IsTotal"/> are totals: a bar from zero to the running total,
/// in the series color. Thin lines carry each bar's end over to the next. The tooltip shows the
/// change and the running total after it.
/// </summary>
public sealed class WaterfallSeries<TItem> : SeriesBase<TItem>
{
    /// <summary>
    /// Marks the totals, e.g. <c>s =&gt; s.IsTotal</c>: a total shows the running total so far, so its
    /// own <c>YValue</c> is ignored (it can return null). Totals in between (a subtotal) keep the running
    /// total going. Aggregated points are totals when their first item is.
    /// </summary>
    [Parameter] public Func<TItem, bool>? IsTotal { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Waterfall;

    private protected override object? DataSettings => IsTotal is not null;

    private protected override SeriesData Complete(SeriesData data, IReadOnlyList<TItem> items, IReadOnlyList<int[]>? groups)
    {
        if (IsTotal is not { } isTotal)
        {
            return data;
        }

        return data.WithTotals(groups is null
            ? [.. items.Select(isTotal)]
            : [.. groups.Select(g => g.Length > 0 && isTotal(items[g[0]]))]);
    }
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
    protected override SeriesOptions CreateOptions(string id) => base.CreateOptions(id) with
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

/// <summary>
/// Radar: one spoke per category (x), clockwise from 12 o'clock, and a closed shape per series
/// through its values. The radius is a linear scale from zero shared by all spokes; set the
/// chart's <c>YAxis</c> Min and Max to fix it (e.g. 0 to 100 for percentages). Several radar
/// series compare, e.g. two products' test scores against their class average. Cannot be mixed with other
/// series types.
/// </summary>
public sealed class RadarSeries<TItem> : SeriesBase<TItem>
{
    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Radar;
}

/// <summary>
/// Treemap: rectangles sized by value, nested by group, for how a total splits into many parts.
/// <c>XValue</c> is each item's path from the outermost group, e.g. <c>"Europe/Sweden/Stockholm"</c>
/// (see <see cref="PathSeparator"/>), or its name with <see cref="ParentValue"/>. A group's value is
/// the sum of its items. Colors and legend items follow the top-level groups. Zero and negative
/// values are left out. Cannot be mixed with other series types.
/// </summary>
public sealed class TreemapSeries<TItem> : SeriesBase<TItem>
{
    /// <summary>
    /// The name of each item's group, matching another item's <c>XValue</c> (null or unknown: a
    /// top-level item). Builds the paths from a parent-child list, e.g. cities with their countries
    /// and countries with their regions; names must then be unique.
    /// </summary>
    [Parameter] public Func<TItem, object?>? ParentValue { get; set; }

    /// <summary>What separates the groups in an <c>XValue</c> path. Default "/".</summary>
    [Parameter] public string? PathSeparator { get; set; }

    /// <summary>Group items smaller than this share of their group (e.g. 0.03) into one "Other" item per group.</summary>
    [Parameter] public double? GroupSmallItems { get; set; }

    /// <summary>
    /// How many levels to draw at once below the group shown, e.g. 2 for continents and their
    /// countries; deeper groups show as one cell with their total and open on a click (drill-down).
    /// Null: all levels. The data keeps every level either way.
    /// </summary>
    [Parameter] public int? VisibleLevels { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Treemap;

    private protected override object? DataSettings => (ParentValue is not null, PathSeparator);

    /// <inheritdoc />
    protected override SeriesOptions CreateOptions(string id) => base.CreateOptions(id) with
    {
        PathSeparator = PathSeparator,
        GroupSmallSlices = GroupSmallItems,
        VisibleLevels = VisibleLevels,
    };

    private protected override SeriesData Complete(SeriesData data, IReadOnlyList<TItem> items, IReadOnlyList<int[]>? groups)
    {
        if (ParentValue is not { } parentOf || XValue is not { } nameOf || data.Kind != XKind.Category)
        {
            return data;
        }

        static string Text(object? value) => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var parents = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            parents.TryAdd(Text(nameOf(item)), parentOf(item) is { } parent ? Text(parent) : null);
        }

        var separator = string.IsNullOrEmpty(PathSeparator) ? "/" : PathSeparator;
        var paths = new string[data.Count];
        for (var i = 0; i < paths.Length; i++)
        {
            var item = items[groups is null ? data.ToSourceIndex(i) : groups[i][0]];
            var name = Text(nameOf(item));
            var path = new List<string> { name };
            var seen = new HashSet<string>(StringComparer.Ordinal) { name };
            for (var p = parentOf(item) is { } first ? Text(first) : null; !string.IsNullOrEmpty(p) && seen.Add(p); p = parents.GetValueOrDefault(p))
            {
                path.Insert(0, p); // stops at a top-level item, an unknown name or a cycle
            }

            paths[i] = string.Join(separator, path);
        }

        return SeriesData.FromCategories(paths, data.Y);
    }
}

/// <summary>
/// A gauge: a value on a scale from <see cref="Min"/> to <see cref="Max"/>, drawn as an arc with the
/// value in the middle. Give one <see cref="Value"/>, or Items with <c>YValue</c> (and <c>XValue</c>
/// for each gauge's label) for a row of gauges. <c>ColorBands</c> color the scale's ranges (and the
/// value, when it is in one); <see cref="Target"/> marks a target.
/// </summary>
public sealed class GaugeSeries<TItem> : SeriesBase<TItem>
{
    /// <summary>
    /// The value to show; used instead of Items when set. Its label is <c>Name</c>. A chart without
    /// items still needs a <c>TItem</c>, e.g. <c>&lt;RioChart TItem="object"&gt;</c>.
    /// </summary>
    [Parameter] public double? Value { get; set; }

    /// <summary>The value at the start of the scale. Default 0 (or a nice value below a negative value).</summary>
    [Parameter] public double? Min { get; set; }

    /// <summary>The value at the end of the scale, e.g. 100 for percentages. Default: a nice value above the largest.</summary>
    [Parameter] public double? Max { get; set; }

    /// <summary>A target value, marked across the scale and given in the tooltip.</summary>
    [Parameter] public double? Target { get; set; }

    /// <summary>Inner radius as a fraction of the outer radius. Default 0.8.</summary>
    [Parameter] public double? InnerRadius { get; set; }

    /// <summary>Start angle in degrees, clockwise from 12 o'clock. Default -120 (with <see cref="EndAngle"/> 120); -90 and 90 make a half circle.</summary>
    [Parameter] public double? StartAngle { get; set; }

    /// <summary>End angle in degrees. Default 120.</summary>
    [Parameter] public double? EndAngle { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Gauge;

    private protected override object? DataSettings => Value;

    private protected override SeriesData? OwnData => Value is { } value ? SeriesData.FromCategories([""], [value]) : null;

    // Items without XValue: one unlabeled gauge per item.
    private protected override SeriesData? BuildData(IReadOnlyList<TItem> items, out IReadOnlyList<int[]>? groups)
    {
        groups = null;
        if (XValue is not null || YValue is not { } y)
        {
            return null;
        }

        var values = new double[items.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = y(items[i]) ?? double.NaN;
        }

        return SeriesData.FromCategories([.. items.Select(_ => "")], values);
    }

    /// <inheritdoc />
    protected override SeriesOptions CreateOptions(string id) => base.CreateOptions(id) with
    {
        Min = Min,
        Max = Max,
        Target = Target,
        InnerRadius = InnerRadius,
        StartAngle = StartAngle,
        EndAngle = EndAngle,
    };
}

/// <summary>
/// Bullets: each value as a bar on a horizontal scale from <see cref="Min"/> to <see cref="Max"/>, in
/// a row of its own, over the scale's ranges (<c>ColorBands</c>, tinted) with a target mark. Give one
/// <see cref="Value"/>, or Items with <c>XValue</c> (each row's label) and <c>YValue</c>. Each series
/// has its own scale, so measures in different units (revenue, margin, satisfaction) can be one
/// series each in the same chart.
/// </summary>
public sealed class BulletSeries<TItem> : SeriesBase<TItem>
{
    /// <summary>
    /// The value to show; used instead of Items when set. Its label is <c>Name</c>. A chart without
    /// items still needs a <c>TItem</c>, e.g. <c>&lt;RioChart TItem="object"&gt;</c>.
    /// </summary>
    [Parameter] public double? Value { get; set; }

    /// <summary>The value at the start of the scale. Default 0 (or a nice value below a negative value).</summary>
    [Parameter] public double? Min { get; set; }

    /// <summary>The value at the end of the scale. Default: a nice value above the largest value and target.</summary>
    [Parameter] public double? Max { get; set; }

    /// <summary>A target for every row, marked across it and given in the tooltip. <see cref="TargetValue"/> gives one per item.</summary>
    [Parameter] public double? Target { get; set; }

    /// <summary>
    /// Each item's target, e.g. <c>k =&gt; k.Goal</c>; null for none (then <see cref="Target"/>).
    /// Items combined into one row (<c>Aggregate</c>) get the same aggregate of their targets, or
    /// the average without one.
    /// </summary>
    [Parameter] public Func<TItem, double?>? TargetValue { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Bullet;

    private protected override object? DataSettings => (Value, TargetValue is not null);

    private protected override SeriesData? OwnData => Value is { } value ? SeriesData.FromCategories([""], [value]) : null;

    // Items without XValue: one unlabeled row per item.
    private protected override SeriesData? BuildData(IReadOnlyList<TItem> items, out IReadOnlyList<int[]>? groups)
    {
        groups = null;
        if (XValue is not null || YValue is not { } y)
        {
            return null;
        }

        var values = new double[items.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = y(items[i]) ?? double.NaN;
        }

        return Complete(SeriesData.FromCategories([.. items.Select(_ => "")], values), items, null);
    }

    private protected override SeriesData Complete(SeriesData data, IReadOnlyList<TItem> items, IReadOnlyList<int[]>? groups)
    {
        if (TargetValue is not { } target)
        {
            return data;
        }

        if (groups is null)
        {
            return data.WithTargets([.. items.Select(item => target(item) ?? double.NaN)]);
        }

        var aggregate = Aggregate is { } a && a != Core.Aggregate.Count ? a : Core.Aggregate.Average;
        return data.WithTargets([.. groups.Select(g => Aggregation.Apply(aggregate, g.Select(i => target(items[i])).ToList()) ?? double.NaN)]);
    }

    /// <inheritdoc />
    protected override SeriesOptions CreateOptions(string id) => base.CreateOptions(id) with
    {
        Min = Min,
        Max = Max,
        Target = Target,
    };
}

/// <summary>
/// Dumbbells: two values per category joined by a line, e.g. before and after, or plan and actual.
/// <see cref="FromValue"/> is the first value (a hollow dot), <c>YValue</c> the second (a filled dot).
/// <c>XValue</c> is the category; with <c>RioChart.Horizontal</c> the categories are rows, the classic
/// layout. The tooltip shows "58 → 66 (+8)".
/// </summary>
public sealed class DumbbellSeries<TItem> : SeriesBase<TItem>
{
    /// <summary>
    /// Each item's first value (before, plan), e.g. <c>c =&gt; c.LastYear</c>; null for none. Items
    /// combined into one point (<c>Aggregate</c>) get the same aggregate of their first values, or the
    /// average without one.
    /// </summary>
    [Parameter] public Func<TItem, double?>? FromValue { get; set; }

    /// <inheritdoc />
    protected override SeriesType Type => SeriesType.Dumbbell;

    private protected override object? DataSettings => FromValue is not null;

    private protected override SeriesData Complete(SeriesData data, IReadOnlyList<TItem> items, IReadOnlyList<int[]>? groups)
    {
        if (FromValue is not { } from)
        {
            return data;
        }

        if (groups is null)
        {
            return data.WithLow([.. items.Select(item => from(item) ?? double.NaN)]);
        }

        var aggregate = Aggregate is { } a && a != Core.Aggregate.Count ? a : Core.Aggregate.Average;
        return data.WithLow([.. groups.Select(g => Aggregation.Apply(aggregate, g.Select(i => from(items[i])).ToList()) ?? double.NaN)]);
    }
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
    protected override SeriesOptions CreateOptions(string id) => base.CreateOptions(id) with
    {
        Max = Max,
        InnerRadius = InnerRadius,
        StartAngle = StartAngle,
        EndAngle = EndAngle,
    };
}
