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

    public double[] X { get; }
    public double[] Y { get; }
    public XKind Kind { get; }
    public int Count => X.Length;

    /// <summary>Category names when <see cref="Kind"/> is Category; X holds indexes into this list.</summary>
    public IReadOnlyList<string> Categories { get; }

    /// <summary>For each point, its index in the original input; null when the input order was kept.</summary>
    public int[]? SourceIndex { get; }

    public static SeriesData Empty { get; } = new([], [], XKind.Numeric, null, null);

    /// <summary>Index of the point in the original input (items list or columns).</summary>
    public int ToSourceIndex(int index) => SourceIndex is null ? index : SourceIndex[index];

    // ---------- Columnar fast path ----------

    public static SeriesData From(ReadOnlySpan<double> x, ReadOnlySpan<double> y) =>
        Create(x.ToArray(), CopyY(y, x.Length), XKind.Numeric);

    public static SeriesData From(ReadOnlyMemory<double> x, ReadOnlyMemory<double> y) => From(x.Span, y.Span);

    public static SeriesData From(ReadOnlySpan<DateTime> x, ReadOnlySpan<double> y)
    {
        var xs = new double[x.Length];
        for (var i = 0; i < xs.Length; i++)
        {
            xs[i] = TimeValue.FromDateTime(x[i]);
        }

        return Create(xs, CopyY(y, x.Length), XKind.Time);
    }

    public static SeriesData From(ReadOnlyMemory<DateTime> x, ReadOnlyMemory<double> y) => From(x.Span, y.Span);

    public static SeriesData From(ReadOnlySpan<DateTimeOffset> x, ReadOnlySpan<double> y)
    {
        var xs = new double[x.Length];
        for (var i = 0; i < xs.Length; i++)
        {
            xs[i] = TimeValue.FromDateTimeOffset(x[i]);
        }

        return Create(xs, CopyY(y, x.Length), XKind.Time);
    }

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

        return Create(xs, ys, kind.Value);
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
