namespace RioCharts.Core.Data;

/// <summary>
/// One candle: how a value moved within a period. Missing values are NaN; a candle missing any of
/// the four is a gap.
/// </summary>
/// <param name="Open">The first value of the period.</param>
/// <param name="High">The highest value.</param>
/// <param name="Low">The lowest value.</param>
/// <param name="Close">The last value of the period.</param>
/// <param name="Count">How many values the candle summarizes (0: not known).</param>
public readonly record struct CandleSummary(double Open, double High, double Low, double Close, int Count = 0)
{
    /// <summary>A candle without values: drawn as a gap.</summary>
    public static CandleSummary Empty { get; } = new(double.NaN, double.NaN, double.NaN, double.NaN);

    /// <summary>True when all four values are there.</summary>
    public bool IsComplete => double.IsFinite(Open) && double.IsFinite(High) && double.IsFinite(Low) && double.IsFinite(Close);

    /// <summary>A candle from values in time order: the first, highest, lowest and last. NaN values are skipped.</summary>
    public static CandleSummary FromValues(IEnumerable<double> valuesInTimeOrder)
    {
        ArgumentNullException.ThrowIfNull(valuesInTimeOrder);
        double open = double.NaN, high = double.NegativeInfinity, low = double.PositiveInfinity, close = double.NaN;
        var count = 0;
        foreach (var v in valuesInTimeOrder)
        {
            if (!double.IsFinite(v))
            {
                continue;
            }

            if (count++ == 0)
            {
                open = v;
            }

            (high, low, close) = (Math.Max(high, v), Math.Min(low, v), v);
        }

        return count == 0 ? Empty : new CandleSummary(open, high, low, close, count);
    }

    /// <summary>
    /// Candles merged into one, e.g. when zoomed out: the first open, the highest high, the lowest
    /// low and the last close. Gaps are skipped.
    /// </summary>
    public static CandleSummary Merge(IEnumerable<CandleSummary> candlesInTimeOrder)
    {
        ArgumentNullException.ThrowIfNull(candlesInTimeOrder);
        var result = Empty;
        foreach (var c in candlesInTimeOrder.Where(c => c.IsComplete))
        {
            result = result.IsComplete
                ? new CandleSummary(result.Open, Math.Max(result.High, c.High), Math.Min(result.Low, c.Low), c.Close, result.Count + c.Count)
                : c;
        }

        return result;
    }
}

/// <summary>
/// The candlestick columns of a <see cref="SeriesData"/>, one entry per point in point order; the
/// point's <see cref="SeriesData.Y"/> is its close. Treat as read-only.
/// </summary>
public sealed class CandleColumns
{
    internal CandleColumns(int count)
    {
        Open = new double[count];
        High = new double[count];
        Low = new double[count];
        Count = new int[count];
    }

    /// <summary>The open per point.</summary>
    public double[] Open { get; }

    /// <summary>The high per point.</summary>
    public double[] High { get; }

    /// <summary>The low per point.</summary>
    public double[] Low { get; }

    /// <summary>How many values each candle summarizes (0: not known).</summary>
    public int[] Count { get; }

    /// <summary>The candle of one point (its close is the series' y value).</summary>
    public CandleSummary At(int point, double close) => new(Open[point], High[point], Low[point], close, Count[point]);

    internal static CandleColumns From(IReadOnlyList<CandleSummary> candles)
    {
        var columns = new CandleColumns(candles.Count);
        for (var i = 0; i < candles.Count; i++)
        {
            (columns.Open[i], columns.High[i], columns.Low[i], columns.Count[i]) = (candles[i].Open, candles[i].High, candles[i].Low, candles[i].Count);
        }

        return columns;
    }
}

/// <summary>Time buckets: the start of the bucket a time falls in, on its wall-clock time (weeks start on Monday).</summary>
public static class TimeBuckets
{
    private const double Day = 86_400_000;

    // Approximate lengths (ms) of the calendar buckets, finest first, for TimeBucket.Auto.
    private static readonly (TimeBucket Bucket, double Ms)[] Lengths =
    [
        (TimeBucket.Minute, 60_000), (TimeBucket.Hour, 3_600_000), (TimeBucket.Day, Day), (TimeBucket.Week, 7 * Day),
        (TimeBucket.Month, 30.44 * Day), (TimeBucket.Quarter, 91.31 * Day), (TimeBucket.Year, 365.25 * Day),
    ];

    /// <summary>How many buckets <see cref="TimeBucket.Auto"/> aims for.</summary>
    public const int AutoTarget = 50;

    /// <summary>
    /// The bucket <see cref="TimeBucket.Auto"/> picks for data spanning <paramref name="span"/>: the
    /// one that gives closest to <see cref="AutoTarget"/> buckets (on a log scale, so 25 and 100 are
    /// equally close). An empty or zero span gives <see cref="TimeBucket.Day"/>.
    /// </summary>
    public static TimeBucket ForSpan(TimeSpan span)
    {
        var ms = span.TotalMilliseconds;
        if (!(ms > 0))
        {
            return TimeBucket.Day;
        }

        var best = TimeBucket.Day;
        var bestDistance = double.PositiveInfinity;
        foreach (var (bucket, length) in Lengths)
        {
            var distance = Math.Abs(Math.Log(ms / length / AutoTarget));
            if (distance < bestDistance)
            {
                (best, bestDistance) = (bucket, distance);
            }
        }

        return best;
    }

    /// <summary>
    /// <paramref name="bucket"/> itself, or for <see cref="TimeBucket.Auto"/> the bucket
    /// <see cref="ForSpan"/> picks for the span of the time values in <paramref name="values"/>
    /// (DateTime, DateTimeOffset or DateOnly; others are ignored).
    /// </summary>
    public static TimeBucket Resolve(TimeBucket bucket, IEnumerable<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (bucket != TimeBucket.Auto)
        {
            return bucket;
        }

        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (var value in values)
        {
            var t = value switch
            {
                DateTime d => TimeValue.FromDateTime(d),
                DateTimeOffset o => TimeValue.FromDateTimeOffset(o),
                DateOnly d => TimeValue.FromDateOnly(d),
                _ => double.NaN,
            };
            if (double.IsFinite(t))
            {
                (min, max) = (Math.Min(min, t), Math.Max(max, t));
            }
        }

        return ForSpan(max > min ? TimeSpan.FromMilliseconds(max - min) : TimeSpan.Zero);
    }

    /// <summary>
    /// The start of the bucket <paramref name="value"/> falls in: a DateTime or DateTimeOffset (with
    /// the same offset), or a DateOnly (minute and hour buckets keep the date); other values are
    /// returned as they are. <see cref="TimeBucket.Auto"/> needs all values: pick the bucket with
    /// <see cref="Resolve"/> first.
    /// </summary>
    public static object Floor(object value, TimeBucket bucket)
    {
        if (bucket == TimeBucket.Auto)
        {
            throw new ArgumentException("TimeBucket.Auto depends on all values; pick the bucket with TimeBuckets.Resolve first.", nameof(bucket));
        }

        if (value is DateOnly date)
        {
            return DateOnly.FromDateTime(FloorTime(date.ToDateTime(TimeOnly.MinValue), bucket));
        }

        if (value is not (DateTime or DateTimeOffset))
        {
            return value;
        }

        var t = value is DateTimeOffset o ? o.DateTime : (DateTime)value;
        var floored = FloorTime(t, bucket);
        // Each branch boxed on its own: a DateTime would otherwise widen to a DateTimeOffset with
        // the machine's local offset.
        return value is DateTimeOffset offset ? new DateTimeOffset(floored, offset.Offset) : (object)floored;
    }

    private static DateTime FloorTime(DateTime t, TimeBucket bucket) => bucket switch
    {
        TimeBucket.Minute => new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Kind),
        TimeBucket.Hour => new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Kind),
        TimeBucket.Day => t.Date,
        TimeBucket.Week => t.Date.AddDays(-(((int)t.DayOfWeek + 6) % 7)),
        TimeBucket.Month => new DateTime(t.Year, t.Month, 1, 0, 0, 0, t.Kind),
        TimeBucket.Quarter => new DateTime(t.Year, (t.Month - 1) / 3 * 3 + 1, 1, 0, 0, 0, t.Kind),
        _ => new DateTime(t.Year, 1, 1, 0, 0, 0, t.Kind),
    };
}
