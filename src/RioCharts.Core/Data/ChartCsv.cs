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
public static class ChartCsv
{
    /// <summary>The CSV text for the given series (name and data), in order.</summary>
    public static string Write(IReadOnlyList<(string Name, SeriesData Data)> series, CsvOptions? options = null)
    {
        options ??= new CsvOptions();
        var kind = series.Select(s => s.Data).FirstOrDefault(d => d.Count > 0)?.Kind ?? XKind.Numeric;

        // Series with another kind of x (rare) cannot share the x column: they are left out.
        var columns = series.Where(s => s.Data.Count == 0 || s.Data.Kind == kind).ToList();

        // Rows keyed by x: the category name, or the number (time: wall-clock milliseconds).
        var keys = new List<object>();
        var lookup = new Dictionary<object, int>();
        var cells = new List<List<double>[]>(); // per key: per column, the y values at that x in order
        var offsets = new Dictionary<object, double>(); // time keys: a UTC offset, when the input had one
        for (var c = 0; c < columns.Count; c++)
        {
            var data = columns[c].Data;
            for (var i = 0; i < data.Count; i++)
            {
                object key = kind == XKind.Category ? CategoryOf(data, i) : data.X[i];
                if (key is double d && !double.IsFinite(d))
                {
                    continue;
                }

                if (!lookup.TryGetValue(key, out var row))
                {
                    row = keys.Count;
                    lookup[key] = row;
                    keys.Add(key);
                    cells.Add(new List<double>[columns.Count]);
                }

                (cells[row][c] ??= []).Add(data.Y[i]);
                if (data.TimeOffsets is { } o && double.IsFinite(o[i]))
                {
                    offsets.TryAdd(key, o[i]);
                }
            }
        }

        var order = Enumerable.Range(0, keys.Count).ToList();
        if (kind != XKind.Category)
        {
            order.Sort((a, b) => ((double)keys[a]).CompareTo((double)keys[b]));
        }

        // Dates alone when every time is at midnight (daily data); else date and time on every row.
        var dateOnly = kind == XKind.Time && keys.All(k => TimeValue.ToDateTime((double)k).TimeOfDay == TimeSpan.Zero);
        var text = new StringBuilder();
        var delimiter = options.Delimiter;
        text.Append(Escape(options.XHeader, delimiter));
        foreach (var column in columns)
        {
            text.Append(delimiter).Append(Escape(column.Name, delimiter));
        }

        text.Append(options.NewLine);
        foreach (var row in order)
        {
            var key = keys[row];
            var x = Escape(FormatX(key, kind, dateOnly, offsets, options.Culture), delimiter);
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

    private static string FormatX(object key, XKind kind, bool dateOnly, Dictionary<object, double> offsets, CultureInfo culture)
    {
        if (key is string category)
        {
            return category;
        }

        var value = (double)key;
        if (kind != XKind.Time)
        {
            return value.ToString("R", culture);
        }

        // ISO 8601, as given: with the input's UTC offset when it had one.
        var time = TimeValue.ToDateTime(value);
        return offsets.TryGetValue(key, out var offset)
            ? new DateTimeOffset(time, TimeSpan.FromMilliseconds(offset)).ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture)
            : time.ToString(dateOnly ? "yyyy-MM-dd" : "yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
    }

    /// <summary>Quotes a cell that holds the delimiter, a quote or a line break (RFC 4180).</summary>
    private static string Escape(string value, char delimiter) =>
        value.IndexOfAny([delimiter, '"', '\r', '\n']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";
}
