using System.Globalization;
using System.Text;
using RioCharts.Core.Data;

namespace RioCharts.Core.Layout;

/// <summary>The overall direction of a series, from <see cref="ChartDescriber.ComputeTrend(IReadOnlyList{ValueTuple{double, double}})"/>.</summary>
public enum Trend
{
    /// <summary>No trend can be computed (fewer than two points, or no x range).</summary>
    None,

    /// <summary>Clearly rising.</summary>
    Increasing,

    /// <summary>Clearly falling.</summary>
    Decreasing,

    /// <summary>Flat, or no clear direction.</summary>
    Stable,
}

/// <summary>Text alternative for a chart: series, ranges and trends.</summary>
public static class ChartDescriber
{
    /// <summary>
    /// Describes the chart in a few sentences in the chart's language: title, series count and,
    /// per visible series, its value count, lowest and highest values with their x, and its trend
    /// (no trend for category axes).
    /// </summary>
    /// <param name="spec">The chart (for its title).</param>
    /// <param name="series">The series with their data; hidden ones are left out.</param>
    /// <param name="xKind">The kind of x axis.</param>
    /// <param name="culture">Culture for numbers and dates.</param>
    /// <param name="texts">The chart's own texts. Null: <see cref="ChartTexts.For"/> the culture.</param>
    public static string Describe(ChartSpec spec, IReadOnlyList<(SeriesSpec Spec, SeriesData Data, bool Visible)> series, XKind xKind, CultureInfo culture,
        ChartTexts? texts = null) =>
        Describe(spec, series, xKind, culture, texts ?? ChartTexts.For(culture), d => Facts(d, xKind));

    /// <summary><see cref="Describe(ChartSpec, IReadOnlyList{ValueTuple{SeriesSpec, SeriesData, bool}}, XKind, CultureInfo, ChartTexts?)"/> with each series' <see cref="SeriesFacts"/> computed by the caller (once per layout).</summary>
    internal static string Describe(ChartSpec spec, IReadOnlyList<(SeriesSpec Spec, SeriesData Data, bool Visible)> series, XKind xKind, CultureInfo culture,
        ChartTexts texts, Func<SeriesData, SeriesFacts> facts)
    {
        var sb = new StringBuilder();
        var visible = series.Where(s => s.Visible).ToList();
        sb.Append(string.IsNullOrWhiteSpace(spec.Title)
            ? texts.FillCount(nameof(ChartTexts.Summary), visible.Count, culture, visible.Count)
            : texts.FillCount(nameof(ChartTexts.SummaryTitled), visible.Count, culture, visible.Count, spec.Title));

        if (!visible.Any(s => facts(s.Data).Count > 0))
        {
            sb.Append(' ').Append(texts.NoData).Append('.');
            return sb.ToString();
        }

        foreach (var (s, d, _) in visible)
        {
            var stats = facts(d);
            sb.Append(' ').Append(texts.FillCount(nameof(ChartTexts.SeriesValues), stats.Count, culture, s.DisplayName, stats.Count));
            if (stats.Count == 0)
            {
                sb.Append('.');
                continue;
            }

            var (min, max) = (stats.Min, stats.Max);
            sb.Append(", ").Append(texts.Fill(nameof(ChartTexts.LowestHighest), culture,
                d.Y[min].ToString("G4", culture), FormatX(d.X[min], d, xKind, culture), d.Y[max].ToString("G4", culture), FormatX(d.X[max], d, xKind, culture)));

            if (stats.Trend != Trend.None)
            {
                sb.Append(", ").Append(texts.Fill(nameof(ChartTexts.Trend), culture, TrendText(stats.Trend, texts)));
            }

            sb.Append('.');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Short text for a compact chart (sparkline), read instead of the full description: per series
    /// its range, last value and trend, formatted like the tooltips, e.g.
    /// "Checkout: 182 ms to 208 ms, last 194 ms, rising."
    /// </summary>
    public static string DescribeCompact(ChartSpec spec, IReadOnlyList<(SeriesSpec Spec, SeriesData Data, bool Visible)> series, XKind xKind, CultureInfo culture,
        ChartTexts? texts = null) =>
        DescribeCompact(spec, series, xKind, culture, texts ?? ChartTexts.For(culture), d => Facts(d, xKind));

    /// <summary><see cref="DescribeCompact(ChartSpec, IReadOnlyList{ValueTuple{SeriesSpec, SeriesData, bool}}, XKind, CultureInfo, ChartTexts?)"/> with facts computed by the caller.</summary>
    internal static string DescribeCompact(ChartSpec spec, IReadOnlyList<(SeriesSpec Spec, SeriesData Data, bool Visible)> series, XKind xKind, CultureInfo culture,
        ChartTexts texts, Func<SeriesData, SeriesFacts> facts)
    {
        var sb = new StringBuilder();
        var visible = series.Where(s => s.Visible).ToList();
        if (!string.IsNullOrWhiteSpace(spec.Title))
        {
            sb.Append(spec.Title!.Trim().TrimEnd('.')).Append('.');
        }

        if (!visible.Any(s => facts(s.Data).Count > 0))
        {
            sb.Append(sb.Length > 0 ? " " : "").Append(texts.NoDataShort);
            return sb.ToString();
        }

        foreach (var (s, d, _) in visible)
        {
            if (DescribeSeries(spec, s, d, xKind, culture, texts, facts(d)) is { } text)
            {
                sb.Append(sb.Length > 0 ? " " : "").Append(text).Append('.');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// One series: its range, last value and trend, formatted like the tooltips, e.g. "Checkout: 182 ms
    /// to 208 ms, last 194 ms, rising" (no last value or trend on category axes). Null without data.
    /// Used by compact charts and the legend's tooltips.
    /// </summary>
    public static string? DescribeSeries(ChartSpec spec, SeriesSpec s, SeriesData d, XKind xKind, CultureInfo culture, ChartTexts? texts = null) =>
        DescribeSeries(spec, s, d, xKind, culture, texts ?? ChartTexts.For(culture), Facts(d, xKind));

    /// <summary><see cref="DescribeSeries(ChartSpec, SeriesSpec, SeriesData, XKind, CultureInfo, ChartTexts?)"/> with the series' facts computed by the caller.</summary>
    internal static string? DescribeSeries(ChartSpec spec, SeriesSpec s, SeriesData d, XKind xKind, CultureInfo culture, ChartTexts texts, SeriesFacts stats)
    {
        if (stats.Count == 0)
        {
            return null;
        }

        var axis = s.YAxisId is { } id ? spec.YAxes.FirstOrDefault(a => a.Id == id) ?? spec.Y : spec.Y;
        var format = s.Format ?? axis.Format;
        string F(double v) => Formatting.ValueFormatter.Format(v, culture, format);
        var (min, max) = (d.Y[stats.Min], d.Y[stats.Max]);
        var sb = new StringBuilder();
        if (s.Type == SeriesType.RangeArea && d.Low is { } lows)
        {
            // From the lowest low to the highest high, and the last point's range.
            var low = lows.Where(double.IsFinite).DefaultIfEmpty(min).Min();
            sb.Append(s.DisplayName).Append(": ").Append(texts.Fill(nameof(ChartTexts.ValueRange), culture, F(low), F(max)));
            var last = Enumerable.Range(0, d.Count).LastOrDefault(i => double.IsFinite(d.Y[i]) && double.IsFinite(lows[i]), -1);
            if (xKind != XKind.Category && last >= 0)
            {
                sb.Append(", ").Append(texts.Fill(nameof(ChartTexts.LastValue), culture, $"{F(lows[last])} – {F(d.Y[last])}"));
            }

            return sb.ToString();
        }

        if (s.Type == SeriesType.Scatter)
        {
            // Points in no order: their number, the y range and whether y rises with x.
            sb.Append(texts.FillCount(nameof(ChartTexts.SeriesPoints), stats.Count, culture, s.DisplayName, stats.Count)).Append(", ");
            sb.Append(min == max ? F(min) : texts.Fill(nameof(ChartTexts.ValueRange), culture, F(min), F(max)));
            if (stats.Trend != Trend.None)
            {
                sb.Append(", ").Append(TrendText(stats.Trend, texts));
            }

            return sb.ToString();
        }

        sb.Append(s.DisplayName).Append(": ");
        sb.Append(min == max ? F(min) : texts.Fill(nameof(ChartTexts.ValueRange), culture, F(min), F(max)));
        if (xKind != XKind.Category && stats.Count > 1)
        {
            sb.Append(", ").Append(texts.Fill(nameof(ChartTexts.LastValue), culture, F(d.Y[stats.Last])));
            if (stats.Trend != Trend.None)
            {
                sb.Append(", ").Append(TrendText(stats.Trend, texts));
            }
        }

        return sb.ToString();
    }

    private static string TrendText(Trend trend, ChartTexts texts) =>
        trend switch { Trend.Increasing => texts.Rising, Trend.Decreasing => texts.Falling, _ => texts.Stable };

    private static string FormatX(double x, SeriesData data, XKind kind, CultureInfo culture) => kind switch
    {
        XKind.Time => TimeValue.ToDateTime(x).ToString("g", culture),
        XKind.Category => (int)x >= 0 && (int)x < data.Categories.Count ? data.Categories[(int)x] : "",
        _ => x.ToString("G4", culture),
    };

    /// <summary>
    /// Least-squares trend: the fitted change across the x range relative to the y range.
    /// Over ±10% with a correlation of at least 0.3 counts as increasing/decreasing.
    /// </summary>
    public static Trend ComputeTrend(IReadOnlyList<(double X, double Y)> points)
    {
        var x = new double[points.Count];
        var y = new double[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            (x[i], y[i]) = points[i];
        }

        return ComputeTrend(x, y);
    }

    /// <summary>The trend of the points with a finite y (and x), without copying them.</summary>
    internal static Trend ComputeTrend(ReadOnlySpan<double> xs, ReadOnlySpan<double> ys)
    {
        var n = 0;
        double sumX = 0, sumY = 0;
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        for (var i = 0; i < ys.Length; i++)
        {
            var (x, y) = (xs[i], ys[i]);
            if (!double.IsFinite(y) || !double.IsFinite(x))
            {
                continue;
            }

            n++;
            sumX += x;
            sumY += y;
            // Plain comparisons: the values are finite, and Math.Min/Max on doubles are slower (NaN, -0).
            if (x < minX)
            {
                minX = x;
            }

            if (x > maxX)
            {
                maxX = x;
            }

            if (y < minY)
            {
                minY = y;
            }

            if (y > maxY)
            {
                maxY = y;
            }
        }

        if (n < 2)
        {
            return Trend.None;
        }

        double meanX = sumX / n, meanY = sumY / n;
        double sxy = 0, sxx = 0, syy = 0;
        for (var i = 0; i < ys.Length; i++)
        {
            var (x, y) = (xs[i], ys[i]);
            if (!double.IsFinite(y) || !double.IsFinite(x))
            {
                continue;
            }

            sxy += (x - meanX) * (y - meanY);
            sxx += (x - meanX) * (x - meanX);
            syy += (y - meanY) * (y - meanY);
        }

        var rangeX = maxX - minX;
        var rangeY = maxY - minY;
        if (sxx == 0 || rangeX == 0)
        {
            return Trend.None;
        }

        if (rangeY == 0 || syy == 0)
        {
            return Trend.Stable;
        }

        if (Math.Abs(sxy / Math.Sqrt(sxx * syy)) < 0.3)
        {
            return Trend.Stable;
        }

        var relative = sxy / sxx * rangeX / rangeY;
        return relative > 0.1 ? Trend.Increasing : relative < -0.1 ? Trend.Decreasing : Trend.Stable;
    }

    /// <summary>
    /// What the descriptions say about a series, from two passes over its data. Chart layout computes
    /// it once per series and layout; the series' description is used twice (legend and chart summary).
    /// </summary>
    internal static SeriesFacts Facts(SeriesData d, XKind xKind)
    {
        var (count, min, max, last) = Scan(d);
        var trend = xKind == XKind.Category || count < 2 ? Trend.None : ComputeTrend(d.X, d.Y);
        return new SeriesFacts(count, min, max, last, trend);
    }

    /// <summary>One pass over a series: the number of finite y values and the indexes of the lowest, highest and last.</summary>
    private static (int Count, int Min, int Max, int Last) Scan(SeriesData d)
    {
        int count = 0, min = -1, max = -1, last = -1;
        for (var i = 0; i < d.Count; i++)
        {
            var y = d.Y[i];
            if (!double.IsFinite(y))
            {
                continue;
            }

            count++;
            last = i;
            if (min < 0 || y < d.Y[min])
            {
                min = i;
            }

            if (max < 0 || y > d.Y[max])
            {
                max = i;
            }
        }

        return (count, min, max, last);
    }
}

/// <summary>A series' finite values (count, indexes of the lowest, highest and last) and trend (None on category axes).</summary>
internal readonly record struct SeriesFacts(int Count, int Min, int Max, int Last, Trend Trend);
