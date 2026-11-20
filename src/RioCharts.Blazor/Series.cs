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
    private bool _registered;
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

    /// <summary>Show the value next to each point or bar.</summary>
    [Parameter] public bool DataLabels { get; set; }

    /// <summary>.NET format string for values in tooltips and data labels, e.g. "0.0".</summary>
    [Parameter] public string? Format { get; set; }

    protected abstract SeriesType Type { get; }

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

    object? ISeriesSource.ItemAt(int sourceIndex) =>
        sourceIndex >= 0 && sourceIndex < ItemList.Count ? ItemList[sourceIndex] : null;

    public SeriesSpec Spec { get; private set; } = new() { Id = "" };

    SeriesData ISeriesSource.Data => _data;

    private SeriesData _data = SeriesData.Empty;

    /// <summary>The items behind the plotted points (index = the point's source index).</summary>
    public IReadOnlyList<TItem> ItemList { get; private set; } = [];

    protected override void OnParametersSet()
    {
        if (Chart is null)
        {
            throw new InvalidOperationException($"{GetType().Name} must be placed inside a <Chart>.");
        }

        if (!_registered)
        {
            _id = Chart.Register(this, Id ?? Name);
            _registered = true;
        }

        var changed = false;
        var items = Items ?? Chart.DefaultItems as IEnumerable<TItem>;
        if (!ReferenceEquals(items, _lastItems) || !ReferenceEquals(Data, _lastData) || !Equals(DataVersion, _lastVersion))
        {
            _lastItems = items;
            _lastData = Data;
            _lastVersion = DataVersion;
            if (Data is not null)
            {
                _data = Data;
                ItemList = [];
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

        if (changed)
        {
            Chart.SeriesChanged(this);
        }
    }

    // Series draw nothing themselves; the chart renders the scene.
    protected override bool ShouldRender() => false;

    public void Dispose()
    {
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
