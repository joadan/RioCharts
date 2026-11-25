using System.Globalization;
using System.Text;

namespace RioCharts.Core.Data;

/// <summary>Options for <see cref="ChartCsv.Write"/>.</summary>
public sealed record CsvOptions
{
    /// <summary>Column separator. Default ','.</summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>Culture for numbers. Default invariant (a '.' decimal point), which spreadsheets and code read alike.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    /// <summary>Header of the x column. Default "x".</summary>
    public string XHeader { get; init; } = "x";

    /// <summary>Line ending. Default "\r\n" (RFC 4180).</summary>
    public string NewLine { get; init; } = "\r\n";
}

/// <summary>
/// Writes chart data as CSV: one x column and one column per series, one row per x value (the
/// union of all series' x values, sorted; categories in order of first appearance). A series
/// without a value at an x gets an empty cell, as do missing values (NaN). When a series has the
/// same x more than once, that x gets as many rows as needed, so no point is lost.
/// </summary>
internal static class ChartCsv
{
    /// <summary>The CSV text for the given series (name and data), in order.</summary>
    public static string Write(IReadOnlyList<(string Name, SeriesData Data)> series, CsvOptions? options = null)
    {
        options ??= new CsvOptions();
        var kind = series.Select(s => s.Data).FirstOrDefault(d => d.Count > 0)?.Kind ?? XKind.Numeric;

        // Series with another kind of x (rare) cannot share the x column: they are left out.
        var columns = series.Where(s => s.Data.Count == 0 || s.Data.Kind == kind).ToList();

        // Rows keyed by x: the category name, or the number (time: wall-clock milliseconds, with the UTC offset when
        // the input had one, so the same wall-clock time at two offsets, as in the repeated hour in autumn, is two rows).
        var keys = new List<object>();
        var lookup = new Dictionary<object, int>();
        var cells = new List<List<double>[]>(); // per key: per column, the y values at that x in order
        for (var c = 0; c < columns.Count; c++)
        {
            var data = columns[c].Data;
            for (var i = 0; i < data.Count; i++)
            {
                if (kind != XKind.Category && !double.IsFinite(data.X[i]))
                {
                    continue;
                }

                object key = kind == XKind.Category ? CategoryOf(data, i)
                    : data.TimeOffsets is { } o && double.IsFinite(o[i]) ? new TimeKey(data.X[i], o[i])
                    : data.X[i];

                if (!lookup.TryGetValue(key, out var row))
                {
                    row = keys.Count;
                    lookup[key] = row;
                    keys.Add(key);
                    cells.Add(new List<double>[columns.Count]);
                }

                (cells[row][c] ??= []).Add(data.Y[i]);
            }
        }

        var order = Enumerable.Range(0, keys.Count).ToList();
        if (kind != XKind.Category)
        {
            // In time order when every time has an offset; else by wall-clock time (then instant).
            var instants = keys.All(k => k is TimeKey);
            order.Sort((a, b) =>
            {
                var (wallA, instantA) = SortOf(keys[a]);
                var (wallB, instantB) = SortOf(keys[b]);
                return instants || wallA == wallB ? instantA.CompareTo(instantB) : wallA.CompareTo(wallB);
            });
        }

        // Dates alone when every time is at midnight (daily data); else date and time on every row.
        var dateOnly = kind == XKind.Time && keys.All(k => TimeValue.ToDateTime(SortOf(k).Wall).TimeOfDay == TimeSpan.Zero);
        var text = new StringBuilder();
        var delimiter = options.Delimiter;
        text.Append(Escape(Text(options.XHeader), delimiter));
        foreach (var column in columns)
        {
            text.Append(delimiter).Append(Escape(Text(column.Name), delimiter));
        }

        text.Append(options.NewLine);
        foreach (var row in order)
        {
            var key = keys[row];
            var x = Escape(FormatX(key, kind, dateOnly, options.Culture), delimiter);
            var repeat = cells[row].Max(values => values?.Count ?? 0);
            for (var r = 0; r < repeat; r++)
            {
                text.Append(x);
                foreach (var values in cells[row])
                {
                    text.Append(delimiter);
                    if (values is not null && r < values.Count && double.IsFinite(values[r]))
                    {
                        text.Append(Escape(values[r].ToString("R", options.Culture), delimiter));
                    }
                }

                text.Append(options.NewLine);
            }
        }

        return text.ToString();
    }

    private static string CategoryOf(SeriesData data, int i)
    {
        var index = (int)data.X[i];
        return index >= 0 && index < data.Categories.Count ? data.Categories[index] : "";
    }

    private static string FormatX(object key, XKind kind, bool dateOnly, CultureInfo culture)
    {
        if (key is string category)
        {
            return Text(category);
        }

        // ISO 8601, as given: with the input's UTC offset when it had one.
        if (key is TimeKey t)
        {
            return new DateTimeOffset(TimeValue.ToDateTime(t.Wall), TimeSpan.FromMilliseconds(t.Offset))
                .ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);
        }

        var value = (double)key;
        return kind != XKind.Time
            ? value.ToString("R", culture)
            : TimeValue.ToDateTime(value).ToString(dateOnly ? "yyyy-MM-dd" : "yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
    }

    /// <summary>A row's x for sorting: its wall-clock time and its instant (the same when the offset is not known).</summary>
    private static (double Wall, double Instant) SortOf(object key) =>
        key is TimeKey t ? (t.Wall, t.Wall - t.Offset) : ((double)key, (double)key);

    /// <summary>A time row's key when the input had a UTC offset: wall-clock milliseconds and the offset.</summary>
    private readonly record struct TimeKey(double Wall, double Offset);

    /// <summary>
    /// A text cell (a header or category) that a spreadsheet would read as a formula ("=", "+", "-" or "@" first) gets
    /// a leading apostrophe, so opening the file never runs it (CSV injection). Numbers ("-5") are left as they are.
    /// </summary>
    private static string Text(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? "'" + value
            : value;

    /// <summary>Quotes a cell that holds the delimiter, a quote or a line break (RFC 4180).</summary>
    private static string Escape(string value, char delimiter) =>
        value.IndexOfAny([delimiter, '"', '\r', '\n']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";
}
