namespace RioCharts.Core.Data;

/// <summary>
/// One box of a box plot: the quartiles, the whisker ends, the extremes and the outliers. Missing
/// values are NaN; a box without values is <see cref="Empty"/> (a gap).
/// </summary>
/// <param name="Min">The lowest value, outliers included.</param>
/// <param name="Low">The lower whisker end: the lowest value that is not an outlier.</param>
/// <param name="Q1">The lower quartile (25th percentile).</param>
/// <param name="Median">The median.</param>
/// <param name="Q3">The upper quartile (75th percentile).</param>
/// <param name="High">The upper whisker end: the highest value that is not an outlier.</param>
/// <param name="Max">The highest value, outliers included.</param>
/// <param name="Count">How many values the box summarizes (0 when given as five numbers without a count).</param>
/// <param name="Outliers">Values beyond the whiskers, ascending.</param>
public readonly record struct BoxSummary(double Min, double Low, double Q1, double Median, double Q3, double High, double Max, int Count, double[] Outliers)
{
    /// <summary>A box without values: drawn as a gap.</summary>
    public static BoxSummary Empty { get; } = new(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, 0, []);

    /// <summary>True when the box has a median and quartiles to draw.</summary>
    public bool HasBox => double.IsFinite(Median) && double.IsFinite(Q1) && double.IsFinite(Q3);

    /// <summary>
    /// Summarizes raw values: quartiles by linear interpolation between the sorted values (as
    /// Excel's QUARTILE.INC and NumPy's default), whiskers at the furthest values within 1.5 times
    /// the interquartile range from the box (Tukey), and the values beyond them as outliers.
    /// NaN and infinite values are skipped.
    /// </summary>
    public static BoxSummary FromValues(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = values.Where(double.IsFinite).ToArray();
        if (sorted.Length == 0)
        {
            return Empty;
        }

        Array.Sort(sorted);
        var q1 = Quantile(sorted, 0.25);
        var median = Quantile(sorted, 0.5);
        var q3 = Quantile(sorted, 0.75);
        var reach = 1.5 * (q3 - q1);
        var (lowFence, highFence) = (q1 - reach, q3 + reach);
        var low = sorted.First(v => v >= lowFence);
        var high = sorted.Last(v => v <= highFence);
        var outliers = sorted.Where(v => v < lowFence || v > highFence).ToArray();
        return new BoxSummary(sorted[0], low, q1, median, q3, high, sorted[^1], sorted.Length, outliers);
    }

    /// <summary>
    /// A box from numbers computed elsewhere (e.g. by the database). The whiskers end at
    /// <paramref name="low"/> and <paramref name="high"/>; outliers, if given, may lie beyond them.
    /// Values out of order are sorted, so the box always reads low to high.
    /// </summary>
    public static BoxSummary FromFiveNumbers(double low, double q1, double median, double q3, double high, IEnumerable<double>? outliers = null, int count = 0)
    {
        double[] five = [low, q1, median, q3, high];
        if (five.All(double.IsFinite))
        {
            Array.Sort(five);
        }

        var extra = outliers?.Where(double.IsFinite).Order().ToArray() ?? [];
        var min = extra.Length > 0 && double.IsFinite(five[0]) ? Math.Min(five[0], extra[0]) : five[0];
        var max = extra.Length > 0 && double.IsFinite(five[4]) ? Math.Max(five[4], extra[^1]) : five[4];
        return new BoxSummary(min, five[0], five[1], five[2], five[3], five[4], max, count, extra);
    }

    /// <summary>The q-th quantile of sorted values, linearly interpolated (type 7).</summary>
    private static double Quantile(double[] sorted, double q)
    {
        var position = (sorted.Length - 1) * q;
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(sorted.Length - 1, lower + 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }
}

/// <summary>
/// The box plot columns of a <see cref="SeriesData"/>, one entry per point (in point order; the
/// point's <see cref="SeriesData.Y"/> is its median). Treat as read-only.
/// </summary>
public sealed class BoxColumns
{
    internal BoxColumns(int count)
    {
        Min = new double[count];
        Low = new double[count];
        Q1 = new double[count];
        Q3 = new double[count];
        High = new double[count];
        Max = new double[count];
        Count = new int[count];
    }

    /// <summary>The lowest value per point, outliers included.</summary>
    public double[] Min { get; }

    /// <summary>The lower whisker end per point.</summary>
    public double[] Low { get; }

    /// <summary>The lower quartile per point.</summary>
    public double[] Q1 { get; }

    /// <summary>The upper quartile per point.</summary>
    public double[] Q3 { get; }

    /// <summary>The upper whisker end per point.</summary>
    public double[] High { get; }

    /// <summary>The highest value per point, outliers included.</summary>
    public double[] Max { get; }

    /// <summary>How many values each point summarizes (0: not known).</summary>
    public int[] Count { get; }

    /// <summary>All outliers, grouped by point in point order.</summary>
    public double[] OutlierValues { get; internal set; } = [];

    /// <summary>The point of each outlier in <see cref="OutlierValues"/>.</summary>
    public int[] OutlierPoints { get; internal set; } = [];

    /// <summary>The outliers of one point.</summary>
    public ReadOnlySpan<double> OutliersOf(int point)
    {
        var start = Array.BinarySearch(OutlierPoints, point);
        if (start < 0)
        {
            return [];
        }

        while (start > 0 && OutlierPoints[start - 1] == point)
        {
            start--;
        }

        var end = start;
        while (end < OutlierPoints.Length && OutlierPoints[end] == point)
        {
            end++;
        }

        return OutlierValues.AsSpan(start, end - start);
    }

    /// <summary>The summary of one point (its median is the series' y value).</summary>
    public BoxSummary At(int point, double median) =>
        new(Min[point], Low[point], Q1[point], median, Q3[point], High[point], Max[point], Count[point], OutliersOf(point).ToArray());

    /// <summary>Columns from summaries in point order.</summary>
    internal static BoxColumns From(IReadOnlyList<BoxSummary> boxes)
    {
        var columns = new BoxColumns(boxes.Count);
        var values = new List<double>();
        var points = new List<int>();
        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            (columns.Min[i], columns.Low[i], columns.Q1[i], columns.Q3[i], columns.High[i], columns.Max[i], columns.Count[i]) =
                (b.Min, b.Low, b.Q1, b.Q3, b.High, b.Max, b.Count);
            foreach (var v in b.Outliers ?? [])
            {
                values.Add(v);
                points.Add(i);
            }
        }

        columns.OutlierValues = [.. values];
        columns.OutlierPoints = [.. points];
        return columns;
    }
}
