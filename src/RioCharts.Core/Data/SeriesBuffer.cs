namespace RioCharts.Core.Data;

/// <summary>
/// A growing window of points for live data: append from any thread, and charts that use it
/// redraw at a capped rate. The oldest points drop out when <see cref="Capacity"/> is reached or,
/// for time data, when they are older than <see cref="TimeWindow"/> behind the newest point.
/// </summary>
/// <example>
/// <code>
/// var buffer = new SeriesBuffer(timeWindow: TimeSpan.FromMinutes(10));
/// buffer.Append(DateTime.Now, 21.4);
/// // &lt;LineSeries Buffer="buffer" Name="Checkout" /&gt;
/// </code>
/// </example>
public sealed class SeriesBuffer
{
    private readonly Lock _lock = new();
    private double[] _x;
    private double[] _y;
    private int _start;
    private int _count;
    private bool _sorted = true;
    private long _version;

    /// <param name="capacity">Most points kept; older ones are dropped.</param>
    /// <param name="timeWindow">For time data: keep only points this close to the newest one.</param>
    /// <param name="kind">Time (default) or Numeric x values.</param>
    public SeriesBuffer(int capacity = 100_000, TimeSpan? timeWindow = null, XKind kind = XKind.Time)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        if (kind == XKind.Category)
        {
            throw new ArgumentException("A buffer holds numeric or time data.", nameof(kind));
        }

        Capacity = capacity;
        TimeWindow = timeWindow;
        Kind = kind;
        var initial = Math.Min(capacity, 1024);
        _x = new double[initial];
        _y = new double[initial];
    }

    /// <summary>The most points kept; the oldest drop out beyond it.</summary>
    public int Capacity { get; }

    /// <summary>For time data: points further than this behind the newest one drop out. Null keeps them until <see cref="Capacity"/>.</summary>
    public TimeSpan? TimeWindow { get; }

    /// <summary>Time or Numeric x values.</summary>
    public XKind Kind { get; }

    /// <summary>Raised after points are appended or cleared (on the appending thread).</summary>
    public event Action? Changed;

    /// <summary>Increases with every change; charts compare it to know when to redraw.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>The number of points in the buffer now.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    /// <summary>Appends a point at a time (used as given, see <see cref="TimeValue"/>). Thread-safe.</summary>
    public void Append(DateTime x, double y) => Append(TimeValue.FromDateTime(x), y);

    /// <summary>Appends a point at a time (its own wall-clock time). Thread-safe.</summary>
    public void Append(DateTimeOffset x, double y) => Append(TimeValue.FromDateTimeOffset(x), y);

    /// <summary>Appends a point; y may be NaN for a gap. On time buffers x is in milliseconds (<see cref="TimeValue"/>).</summary>
    public void Append(double x, double y)
    {
        lock (_lock)
        {
            Add(x, y);
            Trim();
        }

        Changed?.Invoke();
    }

    /// <summary>Appends many points with one change notification.</summary>
    public void AppendRange(ReadOnlySpan<DateTime> x, ReadOnlySpan<double> y)
    {
        lock (_lock)
        {
            for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
            {
                Add(TimeValue.FromDateTime(x[i]), y[i]);
            }

            Trim();
        }

        Changed?.Invoke();
    }

    /// <summary>Appends many points with one change notification (x in data units).</summary>
    public void AppendRange(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
    {
        lock (_lock)
        {
            for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
            {
                Add(x[i], y[i]);
            }

            Trim();
        }

        Changed?.Invoke();
    }

    /// <summary>Removes all points. Thread-safe.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _start = 0;
            _count = 0;
            _sorted = true;
            _version++;
        }

        Changed?.Invoke();
    }

    /// <summary>A copy of the current points as chart data (sorted by x).</summary>
    public SeriesData ToSeriesData()
    {
        lock (_lock)
        {
            var x = new double[_count];
            var y = new double[_count];
            for (var i = 0; i < _count; i++)
            {
                var j = (_start + i) % _x.Length;
                x[i] = _x[j];
                y[i] = _y[j];
            }

            return SeriesData.FromColumns(x, y, Kind);
        }
    }

    private void Add(double x, double y)
    {
        if (!double.IsFinite(x))
        {
            return;
        }

        if (_count == _x.Length && _x.Length < Capacity)
        {
            Grow();
        }

        if (_count > 0 && x < _x[(_start + _count - 1) % _x.Length])
        {
            _sorted = false;
        }

        if (_count == _x.Length)
        {
            // Full: overwrite the oldest point.
            _x[_start] = x;
            _y[_start] = y;
            _start = (_start + 1) % _x.Length;
        }
        else
        {
            var j = (_start + _count) % _x.Length;
            _x[j] = x;
            _y[j] = y;
            _count++;
        }

        _version++;
    }

    private void Grow()
    {
        var size = (int)Math.Min(Capacity, _x.Length * 2L);
        var x = new double[size];
        var y = new double[size];
        for (var i = 0; i < _count; i++)
        {
            var j = (_start + i) % _x.Length;
            x[i] = _x[j];
            y[i] = _y[j];
        }

        (_x, _y, _start) = (x, y, 0);
    }

    private void Trim()
    {
        if (TimeWindow is not { } window || _count == 0 || !_sorted)
        {
            return;
        }

        var newest = _x[(_start + _count - 1) % _x.Length];
        var oldestAllowed = newest - window.TotalMilliseconds;
        while (_count > 0 && _x[_start] < oldestAllowed)
        {
            _start = (_start + 1) % _x.Length;
            _count--;
        }
    }
}

/// <summary>Helpers that feed a <see cref="SeriesBuffer"/>.</summary>
public static class SeriesBufferExtensions
{
    /// <summary>
    /// Appends every item of an async stream until it ends or is cancelled. Charts coalesce the
    /// changes and redraw at their own rate, so items can arrive as fast as they like.
    /// </summary>
    public static async Task ConsumeAsync<T>(this SeriesBuffer buffer, IAsyncEnumerable<T> source, Func<T, DateTime> x, Func<T, double?> y,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(source);
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            buffer.Append(x(item), y(item) ?? double.NaN);
        }
    }

    /// <inheritdoc cref="ConsumeAsync{T}(SeriesBuffer, IAsyncEnumerable{T}, Func{T, DateTime}, Func{T, double?}, CancellationToken)"/>
    public static async Task ConsumeAsync<T>(this SeriesBuffer buffer, IAsyncEnumerable<T> source, Func<T, double> x, Func<T, double?> y,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(source);
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            buffer.Append(x(item), y(item) ?? double.NaN);
        }
    }
}
