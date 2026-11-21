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

        return new SeriesData(X, Y, Kind, Categories, SourceIndex) { TimeOffsets = TimeOffsets, Low = values };
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
            return new SeriesData(x, Y, Kind, Categories, SourceIndex) { Low = Low };
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

        return new SeriesData(keys, y, Kind, Categories, source) { Low = low };
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
