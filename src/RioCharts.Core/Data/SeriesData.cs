using System.Globalization;

namespace RioCharts.Core.Data;

/// <summary>What the x values of a series are.</summary>
public enum XKind
{
    /// <summary>Plain numbers.</summary>
    Numeric,

    /// <summary>Times, stored as milliseconds (see <see cref="TimeValue"/>).</summary>
    Time,

    /// <summary>Categories, stored as indexes into <see cref="SeriesData.Categories"/>.</summary>
    Category,
}

/// <summary>
/// The single data representation inside Core: contiguous columns (structure of arrays).
/// <see cref="Y"/> uses NaN for missing values, which become gaps in lines.
/// Numeric and time data is sorted by x on creation; <see cref="SourceIndex"/> maps back to the input.
/// </summary>
public sealed class SeriesData
{
    private SeriesData(double[] x, double[] y, XKind kind, IReadOnlyList<string>? categories, int[]? sourceIndex)
    {
        X = x;
        Y = y;
        Kind = kind;
        Categories = categories ?? [];
        SourceIndex = sourceIndex;
    }

    /// <summary>The x values, sorted ascending: numbers, time in milliseconds (see <see cref="TimeValue"/>) or category indexes. Treat as read-only; the arrays are exposed for speed.</summary>
    public double[] X { get; }
    /// <summary>The y values, one per x; NaN is a gap. Treat as read-only.</summary>
    public double[] Y { get; }
    /// <summary>What the x values are: numbers, times or categories.</summary>
    public XKind Kind { get; }
    /// <summary>The number of points.</summary>
    public int Count => X.Length;

    /// <summary>Category names when <see cref="Kind"/> is Category; X holds indexes into this list.</summary>
    public IReadOnlyList<string> Categories { get; }

    /// <summary>For each point, its index in the original input; null when the input order was kept.</summary>
    public int[]? SourceIndex { get; }

    /// <summary>
    /// Range series: the low value of each point (<see cref="Y"/> is the high value), NaN where
    /// missing; null for other series. Set with <see cref="WithLow"/>.
    /// </summary>
    public double[]? Low { get; private set; }

    /// <summary>
    /// Each point's own color (a CSS color or theme token such as "--rio-negative"), null where the
    /// point keeps its series color; null when no point has one. Set with <see cref="WithColors"/>.
    /// </summary>
    public string?[]? Colors { get; private set; }

    /// <summary>
    /// The same points with a color each (bars, scatter markers, slices, treemap items; a dot on
    /// lines and areas). <paramref name="colors"/> is in input order like the y values: index i is
    /// the color of input item (or group) i; null keeps the series color.
    /// </summary>
    public SeriesData WithColors(IReadOnlyList<string?> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var values = new string?[Count];
        var any = false;
        for (var i = 0; i < values.Length; i++)
        {
            var source = ToSourceIndex(i);
            values[i] = source < colors.Count && !string.IsNullOrWhiteSpace(colors[source]) ? colors[source] : null;
            any |= values[i] is not null;
        }

        return new SeriesData(X, Y, Kind, Categories, SourceIndex)
        {
            TimeOffsets = TimeOffsets, Low = Low, Box = Box, Candles = Candles, Totals = Totals, Sizes = Sizes, Colors = any ? values : null,
            Rows = Rows, EndOffsets = EndOffsets, Targets = Targets, ErrorLow = ErrorLow, ErrorHigh = ErrorHigh,
        };
    }

    /// <summary>The color of a point: its own, or <paramref name="fallback"/> (the series color).</summary>
    public string ColorAt(int point, string fallback) => Colors is { } colors && point >= 0 && point < colors.Length && colors[point] is { } own ? own : fallback;

    /// <summary>
    /// The same points with a low value each, for range series (<see cref="Y"/> is the high value).
    /// <paramref name="low"/> is in input order, like the y values given when creating the data:
    /// index i is the low of input item (or column entry) i.
    /// </summary>
    public SeriesData WithLow(ReadOnlySpan<double> low)
    {
        var values = new double[Count];
        for (var i = 0; i < values.Length; i++)
        {
            var source = ToSourceIndex(i);
            values[i] = source < low.Length ? low[source] : double.NaN;
        }

        return new SeriesData(X, Y, Kind, Categories, SourceIndex) { TimeOffsets = TimeOffsets, Low = values, Totals = Totals, Sizes = Sizes, Colors = Colors, Targets = Targets, ErrorLow = ErrorLow, ErrorHigh = ErrorHigh };
    }

    /// <summary>
    /// Waterfalls: true for each total point, whose y is the running total of the changes before it;
    /// null for other series (and for a waterfall without totals). Set with <see cref="WithTotals"/>.
    /// </summary>
    public bool[]? Totals { get; private set; }

    /// <summary>
    /// The same points as a waterfall with totals: <paramref name="totals"/> is in input order, like
    /// the y values given when creating the data (index i is true when input item or group i is a
    /// total). A total point's y becomes the running total of the changes before it (its own y is
    /// ignored); the other points are changes, and gaps (NaN) change nothing.
    /// </summary>
    public SeriesData WithTotals(ReadOnlySpan<bool> totals)
    {
        var flags = new bool[Count];
        var y = (double[])Y.Clone();
        var running = 0.0;
        var any = false;
        for (var i = 0; i < flags.Length; i++)
        {
            var source = ToSourceIndex(i);
            flags[i] = source < totals.Length && totals[source];
            if (flags[i])
            {
                y[i] = running;
                any = true;
            }
            else if (double.IsFinite(y[i]))
            {
                running += y[i];
            }
        }

        return new SeriesData(X, any ? y : Y, Kind, Categories, SourceIndex)
        {
            TimeOffsets = TimeOffsets, Low = Low, Colors = Colors, Sizes = Sizes, Totals = any ? flags : null, ErrorLow = ErrorLow, ErrorHigh = ErrorHigh,
        };
    }

    /// <summary>
    /// Bubbles (scatter with a size): each point's size, NaN where missing; null for other series.
    /// A bubble's area is proportional to its size. Set with <see cref="WithSizes"/>.
    /// </summary>
    public double[]? Sizes { get; private set; }

    /// <summary>
    /// The same points with a size each, which makes a scatter series a bubble chart.
    /// <paramref name="sizes"/> is in input order, like the y values given when creating the data:
    /// index i is the size of input item (or group) i.
    /// </summary>
    public SeriesData WithSizes(ReadOnlySpan<double> sizes)
    {
        var values = new double[Count];
        for (var i = 0; i < values.Length; i++)
        {
            var source = ToSourceIndex(i);
            values[i] = source < sizes.Length ? sizes[source] : double.NaN;
        }

        return new SeriesData(X, Y, Kind, Categories, SourceIndex)
        {
            TimeOffsets = TimeOffsets, Low = Low, Box = Box, Candles = Candles, Totals = Totals, Colors = Colors, Sizes = values, Targets = Targets, ErrorLow = ErrorLow, ErrorHigh = ErrorHigh,
        };
    }

    /// <summary>
    /// Error bars: the low end of each point's error bar (a value, not a distance), NaN where it has
    /// none; null when the series has no error bars. Set with <see cref="WithErrors"/>.
    /// </summary>
    public double[]? ErrorLow { get; private set; }

    /// <summary>Error bars: the high end of each point's error bar, NaN where it has none; null without error bars.</summary>
    public double[]? ErrorHigh { get; private set; }

    /// <summary>
    /// The same points with an error bar each, from <paramref name="low"/> to <paramref name="high"/>
    /// (values on the y axis, e.g. y - sd and y + sd). Both are in input order, like the y values
    /// given when creating the data: index i belongs to input item (or group) i; NaN for none.
    /// </summary>
    public SeriesData WithErrors(ReadOnlySpan<double> low, ReadOnlySpan<double> high)
    {
        var lows = new double[Count];
        var highs = new double[Count];
        for (var i = 0; i < lows.Length; i++)
        {
            var source = ToSourceIndex(i);
            lows[i] = source < low.Length ? low[source] : double.NaN;
            highs[i] = source < high.Length ? high[source] : double.NaN;
        }

        return new SeriesData(X, Y, Kind, Categories, SourceIndex)
        {
            TimeOffsets = TimeOffsets, Low = Low, Box = Box, Candles = Candles, Totals = Totals, Colors = Colors, Sizes = Sizes, Rows = Rows, Targets = Targets,
            ErrorLow = lows, ErrorHigh = highs,
        };
    }

    /// <summary>
    /// Bullets: each point's target value, NaN where it has none; null for other series. Set with
    /// <see cref="WithTargets"/>.
    /// </summary>
    public double[]? Targets { get; private set; }

    /// <summary>
    /// The same points with a target each (bullet charts). <paramref name="targets"/> is in input
    /// order, like the y values given when creating the data: index i is the target of input item
    /// (or group) i.
    /// </summary>
    public SeriesData WithTargets(ReadOnlySpan<double> targets)
    {
        var values = new double[Count];
        for (var i = 0; i < values.Length; i++)
        {
            var source = ToSourceIndex(i);
            values[i] = source < targets.Length ? targets[source] : double.NaN;
        }

        return new SeriesData(X, Y, Kind, Categories, SourceIndex)
        {
            TimeOffsets = TimeOffsets, Low = Low, Box = Box, Candles = Candles, Totals = Totals, Colors = Colors, Sizes = Sizes, Rows = Rows, Targets = values, ErrorLow = ErrorLow, ErrorHigh = ErrorHigh,
        };
    }

    /// <summary>
    /// Candlesticks: the open, high and low of each point (<see cref="Y"/> is the close); null for
    /// other series. Set with <see cref="WithCandles"/>.
    /// </summary>
    public CandleColumns? Candles { get; private set; }

    /// <summary>
    /// The same points as candles: <paramref name="candles"/> is in input order (index i is the
    /// candle of input item or group i). Each point's y becomes its candle's close; an incomplete
    /// candle is a gap.
    /// </summary>
    public SeriesData WithCandles(ReadOnlySpan<CandleSummary> candles)
    {
        var ordered = new CandleSummary[Count];
        var y = new double[Count];
        for (var i = 0; i < ordered.Length; i++)
        {
            var source = ToSourceIndex(i);
            ordered[i] = source < candles.Length && candles[source].IsComplete ? candles[source] : CandleSummary.Empty;
            y[i] = ordered[i].Close;
        }

        return new SeriesData(X, y, Kind, Categories, SourceIndex) { TimeOffsets = TimeOffsets, Candles = CandleColumns.From(ordered), Sizes = Sizes, Colors = Colors };
    }

    /// <summary>
    /// Candles from raw readings: the items are grouped per time bucket (e.g. per hour or day, on
    /// their wall-clock time) and each bucket gives the first, highest, lowest and last value in
    /// time order. Null values are skipped.
    /// </summary>
    /// <param name="groups">Each candle's item indexes; a point's source index is its group's index.</param>
    public static SeriesData FromCandleBuckets<TItem>(IEnumerable<TItem> items, Func<TItem, object?> time, Func<TItem, double?> value,
        TimeBucket bucket, out IReadOnlyList<int[]> groups)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(value);
        var list = items as IReadOnlyList<TItem> ?? items?.ToList() ?? throw new ArgumentNullException(nameof(items));
        var times = new object?[list.Count];
        for (var i = 0; i < times.Length; i++)
        {
            times[i] = time(list[i]);
        }

        var data = FromTimeBuckets(times, bucket, _ => 0, out groups);
        var candles = new CandleSummary[groups.Count];
        for (var g = 0; g < candles.Length; g++)
        {
            // In time order within the bucket, whatever the input order.
            var ordered = groups[g].OrderBy(i => ToX(times[i])).ThenBy(i => i);
            candles[g] = CandleSummary.FromValues(ordered.Select(i => value(list[i]) ?? double.NaN));
        }

        return data.WithCandles(candles);
    }

    /// <summary>
    /// Groups items per time bucket (e.g. per day or month, on their wall-clock time) and makes one
    /// point per bucket at the bucket's start, with y from <paramref name="aggregate"/> (null is a
    /// gap). <see cref="TimeBucket.Auto"/> picks the bucket from the span of the times. Items with a
    /// null x are left out; x values that are not times group by value. Selectors and the
    /// aggregate run once here, never per render.
    /// </summary>
    /// <param name="groups">Each bucket's item indexes; a point's source index is its group's index.</param>
    public static SeriesData FromTimeBuckets<TItem>(IEnumerable<TItem> items, Func<TItem, object?> time, TimeBucket bucket,
        Func<IReadOnlyList<TItem>, double?> aggregate, out IReadOnlyList<int[]> groups)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(aggregate);
        var list = items as IReadOnlyList<TItem> ?? items?.ToList() ?? throw new ArgumentNullException(nameof(items));
        var times = new object?[list.Count];
        for (var i = 0; i < times.Length; i++)
        {
            times[i] = time(list[i]);
        }

        return FromTimeBuckets(times, bucket, members => aggregate(Array.ConvertAll(members, i => list[i])), out groups);
    }

    // Groups item indexes by the bucket of their time; aggregate gets each group's item indexes.
    private static SeriesData FromTimeBuckets(object?[] times, TimeBucket bucket, Func<int[], double?> aggregate, out IReadOnlyList<int[]> groups)
    {
        var resolved = TimeBuckets.Resolve(bucket, times);
        return FromGroups(Enumerable.Range(0, times.Length).ToArray(),
            i => times[i] is { } t ? TimeBuckets.Floor(t, resolved) : null,
            members => aggregate([.. members]), out groups);
    }

    /// <summary>
    /// Box plots: the quartiles, whiskers, extremes and outliers of each point (<see cref="Y"/> is
    /// the median); null for other series. Set with <see cref="WithBox"/>.
    /// </summary>
    public BoxColumns? Box { get; private set; }

    /// <summary>
    /// The same points as boxes: <paramref name="boxes"/> is in input order, like the y values given
    /// when creating the data (index i is the box of input item or group i). Each point's y becomes
    /// its box's median.
    /// </summary>
    public SeriesData WithBox(ReadOnlySpan<BoxSummary> boxes)
    {
        var ordered = new BoxSummary[Count];
        var y = new double[Count];
        for (var i = 0; i < ordered.Length; i++)
        {
            var source = ToSourceIndex(i);
            ordered[i] = source < boxes.Length ? boxes[source] : BoxSummary.Empty;
            y[i] = ordered[i].HasBox ? ordered[i].Median : double.NaN;
        }

        return new SeriesData(X, y, Kind, Categories, SourceIndex) { TimeOffsets = TimeOffsets, Low = Low, Box = BoxColumns.From(ordered), Sizes = Sizes, Colors = Colors };
    }

    /// <summary>
    /// Box plot data from raw values: items with the same x form one box, summarized with
    /// <see cref="BoxSummary.FromValues"/>. Categories keep the order of first appearance; numbers
    /// and times are sorted. Null values are skipped.
    /// </summary>
    /// <param name="groups">Each box's item indexes; a point's source index is its group's index.</param>
    public static SeriesData FromBoxGroups<TItem>(IEnumerable<TItem> items, Func<TItem, object?> xValue, Func<TItem, double?> value,
        out IReadOnlyList<int[]> groups)
    {
        ArgumentNullException.ThrowIfNull(value);
        var list = items as IReadOnlyList<TItem> ?? items?.ToList() ?? throw new ArgumentNullException(nameof(items));
        var data = FromGroups(list, xValue, _ => 0, out groups);
        var boxes = new BoxSummary[groups.Count];
        for (var g = 0; g < boxes.Length; g++)
        {
            boxes[g] = BoxSummary.FromValues(groups[g].Select(i => value(list[i]) ?? double.NaN));
        }

        return data.WithBox(boxes);
    }

    /// <summary>
    /// Time data: the UTC offset (milliseconds) of each point when the input had one (DateTimeOffset,
    /// or DateTime with Kind Utc or Local), NaN otherwise; null when no point had one. Used only
    /// when the chart has a display time zone; by default times are shown as given.
    /// </summary>
    public double[]? TimeOffsets { get; private set; }

    /// <summary>Keeps the offsets of the input points (in input order) that have one.</summary>
    private SeriesData WithOffsets(double[] rawOffsets)
    {
        if (!rawOffsets.Any(double.IsFinite))
        {
            return this;
        }

        var offsets = new double[Count];
        for (var i = 0; i < offsets.Length; i++)
        {
            offsets[i] = rawOffsets[ToSourceIndex(i)];
        }

        TimeOffsets = offsets;
        return this;
    }

    /// <summary>The same points with new x values (e.g. converted to instants), sorted again when needed.</summary>
    internal SeriesData WithX(double[] x)
    {
        if (IsSorted(x))
        {
            return new SeriesData(x, Y, Kind, Categories, SourceIndex)
            {
                Low = Low, Box = Box, Candles = Candles, Totals = Totals, Sizes = Sizes, Colors = Colors, Rows = Rows, Targets = Targets, ErrorLow = ErrorLow, ErrorHigh = ErrorHigh,
            };
        }

        var order = Enumerable.Range(0, x.Length).ToArray();
        var keys = (double[])x.Clone();
        Array.Sort(keys, order);
        var y = new double[order.Length];
        var low = Low is null ? null : new double[order.Length];
        var source = new int[order.Length];
        for (var i = 0; i < order.Length; i++)
        {
            y[i] = Y[order[i]];
            if (low is not null)
            {
                low[i] = Low![order[i]];
            }

            source[i] = ToSourceIndex(order[i]);
        }

        var result = new SeriesData(keys, y, Kind, Categories, source) { Low = low };
        if (Box is { } box)
        {
            // The boxes follow their points.
            result.Box = BoxColumns.From([.. order.Select(i => box.At(i, Y[i]))]);
        }

        if (Candles is { } candles)
        {
            result.Candles = CandleColumns.From([.. order.Select(i => candles.At(i, Y[i]))]);
        }

        if (Colors is { } colors)
        {
            result.Colors = [.. order.Select(i => colors[i])];
        }

        if (Totals is { } totals)
        {
            result.Totals = [.. order.Select(i => totals[i])];
        }

        if (Sizes is { } sizes)
        {
            result.Sizes = [.. order.Select(i => sizes[i])];
        }

        if (Rows is { } rows)
        {
            result.Rows = [.. order.Select(i => rows[i])];
        }

        if (Targets is { } targets)
        {
            result.Targets = [.. order.Select(i => targets[i])];
        }

        if (ErrorLow is { } errorLow && ErrorHigh is { } errorHigh)
        {
            result.ErrorLow = [.. order.Select(i => errorLow[i])];
            result.ErrorHigh = [.. order.Select(i => errorHigh[i])];
        }

        return result;
    }

    /// <summary>
    /// Range bars: the row of each point, e.g. the machine or task it belongs to (<see cref="X"/> is
    /// the start, <see cref="Y"/> the end, in the same units); null for other series. Set with
    /// <see cref="WithRows"/> or made by <see cref="FromRanges"/>.
    /// </summary>
    public string[]? Rows { get; private set; }

    /// <summary>Range bars from times: the UTC offset of each end (like <see cref="TimeOffsets"/> for the starts); null otherwise.</summary>
    internal double[]? EndOffsets { get; private set; }

    /// <summary>
    /// The same points as range bars, each in a row: <paramref name="rows"/> is in input order, like
    /// the y values given when creating the data (index i is the row of input item i); a null or
    /// missing row is "". <see cref="X"/> is each bar's start and <see cref="Y"/> its end.
    /// </summary>
    public SeriesData WithRows(IReadOnlyList<string?> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var values = new string[Count];
        for (var i = 0; i < values.Length; i++)
        {
            var source = ToSourceIndex(i);
            values[i] = source < rows.Count ? rows[source] ?? "" : "";
        }

        return new SeriesData(X, Y, Kind, Categories, SourceIndex) { TimeOffsets = TimeOffsets, EndOffsets = EndOffsets, Colors = Colors, Rows = values };
    }

    /// <summary>Range bars: the same bars with new ends (e.g. converted to instants).</summary>
    internal SeriesData WithEnds(double[] end) =>
        new(X, end, Kind, Categories, SourceIndex) { TimeOffsets = TimeOffsets, Colors = Colors, Rows = Rows };

    /// <summary>
    /// Range bars from items: a bar from <paramref name="start"/> to <paramref name="end"/> (both
    /// numbers, or both DateTime, DateTimeOffset or DateOnly; the first non-null start decides) in
    /// the row <paramref name="row"/> gives (null: every bar in one row, named after the series).
    /// Items without a start are left out; a missing end, or one before the start, draws nothing.
    /// Points are sorted by start; selectors run once here, never per render.
    /// </summary>
    public static SeriesData FromRanges<TItem>(IEnumerable<TItem> items, Func<TItem, string?>? row, Func<TItem, object?> start, Func<TItem, object?> end)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        var list = items as IReadOnlyList<TItem> ?? items.ToList();
        var rawStart = new object?[list.Count];
        var rawEnd = new object?[list.Count];
        XKind? kind = null;
        for (var i = 0; i < list.Count; i++)
        {
            rawStart[i] = start(list[i]);
            rawEnd[i] = end(list[i]);
            kind ??= KindOf(rawStart[i]);
        }

        // Text is not a range: such values count as missing.
        var time = kind == XKind.Time;
        double Value(object? v) => v is string ? double.NaN : time != (KindOf(v) == XKind.Time) ? double.NaN : ToX(v);
        var data = Create(Array.ConvertAll(rawStart, Value), Array.ConvertAll(rawEnd, Value), time ? XKind.Time : XKind.Numeric);
        if (time)
        {
            data.WithOffsets(Array.ConvertAll(rawStart, TimeValue.OffsetOf));
            var endOffsets = Array.ConvertAll(rawEnd, TimeValue.OffsetOf);
            if (endOffsets.Any(double.IsFinite))
            {
                data.EndOffsets = [.. Enumerable.Range(0, data.Count).Select(i => endOffsets[data.ToSourceIndex(i)])];
            }
        }

        var rows = row is null ? new string?[list.Count] : list.Select(row).ToArray();
        return data.WithRows(rows);
    }

    /// <summary>A series with no points.</summary>
    public static SeriesData Empty { get; } = new([], [], XKind.Numeric, null, null);

    /// <summary>Index of the point in the original input (items list or columns).</summary>
    public int ToSourceIndex(int index) => SourceIndex is null ? index : SourceIndex[index];

    // ---------- Columnar fast path ----------

    /// <summary>Numeric x and y columns (copied). Points without a finite x are dropped, unsorted x is sorted, and missing y values are NaN (gaps).</summary>
    public static SeriesData From(ReadOnlySpan<double> x, ReadOnlySpan<double> y) =>
        Create(x.ToArray(), CopyY(y, x.Length), XKind.Numeric);

    /// <summary>Numeric x and y columns (copied).</summary>
    public static SeriesData From(ReadOnlyMemory<double> x, ReadOnlyMemory<double> y) => From(x.Span, y.Span);

    /// <summary>Time x and y columns (copied); times are used as given, see <see cref="TimeValue"/>.</summary>
    public static SeriesData From(ReadOnlySpan<DateTime> x, ReadOnlySpan<double> y)
    {
        var xs = new double[x.Length];
        for (var i = 0; i < xs.Length; i++)
        {
            xs[i] = TimeValue.FromDateTime(x[i]);
        }

        var offsets = new double[x.Length];
        for (var i = 0; i < offsets.Length; i++)
        {
            offsets[i] = TimeValue.OffsetOf(x[i]);
        }

        return Create(xs, CopyY(y, x.Length), XKind.Time).WithOffsets(offsets);
    }

    /// <summary>Time x and y columns (copied).</summary>
    public static SeriesData From(ReadOnlyMemory<DateTime> x, ReadOnlyMemory<double> y) => From(x.Span, y.Span);

    /// <summary>Time x and y columns (copied); each value contributes its own wall-clock time.</summary>
    public static SeriesData From(ReadOnlySpan<DateTimeOffset> x, ReadOnlySpan<double> y)
    {
        var xs = new double[x.Length];
        for (var i = 0; i < xs.Length; i++)
        {
            xs[i] = TimeValue.FromDateTimeOffset(x[i]);
        }

        var offsets = new double[x.Length];
        for (var i = 0; i < offsets.Length; i++)
        {
            offsets[i] = x[i].Offset.TotalMilliseconds;
        }

        return Create(xs, CopyY(y, x.Length), XKind.Time).WithOffsets(offsets);
    }

    /// <summary>Time x and y columns (copied).</summary>
    public static SeriesData From(ReadOnlyMemory<DateTimeOffset> x, ReadOnlyMemory<double> y) => From(x.Span, y.Span);

    /// <summary>Category data; order of first appearance is kept.</summary>
    public static SeriesData FromCategories(IReadOnlyList<string> categories, ReadOnlySpan<double> y)
    {
        var lookup = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = new List<string>();
        var xs = new double[categories.Count];
        for (var i = 0; i < xs.Length; i++)
        {
            var name = categories[i] ?? "";
            if (!lookup.TryGetValue(name, out var index))
            {
                index = names.Count;
                lookup[name] = index;
                names.Add(name);
            }

            xs[i] = index;
        }

        return new SeriesData(xs, CopyY(y, xs.Length), XKind.Category, names, null);
    }

    // ---------- Items with selectors ----------

    /// <summary>
    /// Builds columns from items. X may be a number, DateTime, DateTimeOffset, DateOnly or string
    /// (category); the first non-null value decides the kind. A null Y is a gap.
    /// Selectors run once here, never per render.
    /// </summary>
    public static SeriesData FromItems<TItem>(IEnumerable<TItem> items, Func<TItem, object?> xValue, Func<TItem, double?> yValue)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(xValue);
        ArgumentNullException.ThrowIfNull(yValue);

        var list = items as IReadOnlyList<TItem> ?? items.ToList();
        var rawX = new object?[list.Count];
        var ys = new double[list.Count];
        XKind? kind = null;
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            rawX[i] = xValue(item);
            ys[i] = yValue(item) ?? double.NaN;
            kind ??= KindOf(rawX[i]);
        }

        kind ??= XKind.Numeric;
        if (kind == XKind.Category)
        {
            var names = rawX.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").ToList();
            return FromCategories(names, ys);
        }

        var xs = new double[list.Count];
        for (var i = 0; i < xs.Length; i++)
        {
            xs[i] = ToX(rawX[i]);
        }

        var data = Create(xs, ys, kind.Value);
        return kind == XKind.Time ? data.WithOffsets(Array.ConvertAll(rawX, TimeValue.OffsetOf)) : data;
    }

    /// <summary>
    /// A histogram: the items' values (<paramref name="value"/>; null and NaN are skipped) counted in
    /// equal-width bins (<see cref="Bins.For"/>). One point per bin, empty bins included: x is the bin's
    /// middle, y the number of values in it. Selectors run once here, never per render.
    /// </summary>
    /// <param name="width">The bin width; null picks a nice width from the data.</param>
    /// <param name="count">About this many bins when <paramref name="width"/> is not set; null picks from the data.</param>
    /// <param name="groups">Each bin's item indexes (into <paramref name="items"/>); a point's source index is its bin.</param>
    /// <param name="bins">The bins used.</param>
    public static SeriesData FromBins<TItem>(IEnumerable<TItem> items, Func<TItem, double?> value, double? width, int? count,
        out IReadOnlyList<int[]> groups, out Bins bins)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(value);
        var list = items as IReadOnlyList<TItem> ?? items.ToList();
        var values = new double[list.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = value(list[i]) ?? double.NaN;
        }

        bins = Bins.For(values, width, count);
        var members = new List<int>[bins.Count];
        for (var b = 0; b < members.Length; b++)
        {
            members[b] = [];
        }

        for (var i = 0; i < values.Length; i++)
        {
            if (bins.IndexOf(values[i]) is >= 0 and var b)
            {
                members[b].Add(i);
            }
        }

        groups = [.. members.Select(m => m.ToArray())];
        var x = new double[bins.Count];
        var y = new double[bins.Count];
        for (var b = 0; b < x.Length; b++)
        {
            (x[b], y[b]) = (bins.Center(b), members[b].Count);
        }

        return Create(x, y, XKind.Numeric);
    }

    /// <inheritdoc cref="FromGroups{TItem}(IEnumerable{TItem}, Func{TItem, object?}, Func{IReadOnlyList{TItem}, double?}, out IReadOnlyList{int[]})"/>
    public static SeriesData FromGroups<TItem>(IEnumerable<TItem> items, Func<TItem, object?> xValue, Func<IReadOnlyList<TItem>, double?> aggregate) =>
        FromGroups(items, xValue, aggregate, out _);

    /// <summary>
    /// Groups items with the same x and makes one point per group, with y from
    /// <paramref name="aggregate"/> (null is a gap). Categories keep the order of first appearance;
    /// numbers and times are sorted as usual. Items with a null x are left out. Selectors and the
    /// aggregate run once here, never per render.
    /// </summary>
    /// <param name="groups">
    /// Each group's item indexes (into <paramref name="items"/>). A point's source index
    /// (<see cref="ToSourceIndex"/>) is the index of its group in this list.
    /// </param>
    public static SeriesData FromGroups<TItem>(IEnumerable<TItem> items, Func<TItem, object?> xValue,
        Func<IReadOnlyList<TItem>, double?> aggregate, out IReadOnlyList<int[]> groups)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(xValue);
        ArgumentNullException.ThrowIfNull(aggregate);

        var list = items as IReadOnlyList<TItem> ?? items.ToList();
        var lookup = new Dictionary<object, int>();
        var keys = new List<object>();
        var members = new List<List<int>>();
        for (var i = 0; i < list.Count; i++)
        {
            // Numbers group by value, so 1 (int) and 1.0 (double) are the same x.
            var x = xValue(list[i]);
            var key = x is null or string or DateTime or DateTimeOffset or DateOnly ? x : ToX(x);
            if (key is null)
            {
                continue;
            }

            if (!lookup.TryGetValue(key, out var g))
            {
                g = keys.Count;
                lookup[key] = g;
                keys.Add(key);
                members.Add([]);
            }

            members[g].Add(i);
        }

        var result = new int[members.Count][];
        var ys = new double?[members.Count];
        for (var g = 0; g < result.Length; g++)
        {
            result[g] = [.. members[g]];
            ys[g] = aggregate(Array.ConvertAll(result[g], i => list[i]));
        }

        groups = result;
        return FromItems(Enumerable.Range(0, keys.Count).ToArray(), g => keys[g], g => ys[g]);
    }

    private static XKind? KindOf(object? value) => value switch
    {
        null => null,
        DateTime or DateTimeOffset or DateOnly => XKind.Time,
        string => XKind.Category,
        _ => XKind.Numeric,
    };

    private static double ToX(object? value)
    {
        try
        {
            return value switch
            {
                null => double.NaN,
                DateTime d => TimeValue.FromDateTime(d),
                DateTimeOffset o => TimeValue.FromDateTimeOffset(o),
                DateOnly d => TimeValue.FromDateOnly(d),
                double d => d,
                float f => f,
                IConvertible c => c.ToDouble(CultureInfo.InvariantCulture),
                _ => double.NaN,
            };
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
        {
            return double.NaN;
        }
    }

    private static double[] CopyY(ReadOnlySpan<double> y, int length)
    {
        var ys = new double[length];
        var n = Math.Min(length, y.Length);
        y[..n].CopyTo(ys);
        ys.AsSpan(n).Fill(double.NaN); // fewer y values than x: the rest are gaps
        return ys;
    }

    /// <summary>Takes ownership of the arrays (x in data units: numbers, or milliseconds for time).</summary>
    internal static SeriesData FromColumns(double[] x, double[] y, XKind kind) => Create(x, y, kind);

    /// <summary>Drops points without a usable x, then sorts by x if needed (checked once).</summary>
    private static SeriesData Create(double[] x, double[] y, XKind kind)
    {
        var valid = 0;
        for (var i = 0; i < x.Length; i++)
        {
            if (double.IsFinite(x[i]))
            {
                valid++;
            }
        }

        int[]? source = null;
        if (valid < x.Length)
        {
            var nx = new double[valid];
            var ny = new double[valid];
            source = new int[valid];
            for (int i = 0, j = 0; i < x.Length; i++)
            {
                if (double.IsFinite(x[i]))
                {
                    nx[j] = x[i];
                    ny[j] = y[i];
                    source[j++] = i;
                }
            }

            (x, y) = (nx, ny);
        }

        if (!IsSorted(x))
        {
            var order = Enumerable.Range(0, x.Length).ToArray();
            var keys = (double[])x.Clone();
            Array.Sort(keys, order); // not stable, but ties are equal x anyway
            var ny = new double[y.Length];
            var ns = new int[order.Length];
            for (var i = 0; i < order.Length; i++)
            {
                ny[i] = y[order[i]];
                ns[i] = source is null ? order[i] : source[order[i]];
            }

            (x, y, source) = (keys, ny, ns);
        }

        return new SeriesData(x, y, kind, null, source);
    }

    private static bool IsSorted(double[] x)
    {
        for (var i = 1; i < x.Length; i++)
        {
            if (x[i] < x[i - 1])
            {
                return false;
            }
        }

        return true;
    }
}
