using System.Globalization;
using System.Text;
using RioCharts.Core.Data;

namespace RioCharts.Core.Layout;

public enum Trend { None, Increasing, Decreasing, Stable }

/// <summary>Text alternative for a chart: series, ranges and trends.</summary>
public static class ChartDescriber
{
    public static string Describe(ChartSpec spec, IReadOnlyList<(SeriesSpec Spec, SeriesData Data, bool Visible)> series, XKind xKind, CultureInfo culture,
        ChartTexts? texts = null)
    {
        texts ??= ChartTexts.For(culture);
        var sb = new StringBuilder();
        var visible = series.Where(s => s.Visible).ToList();
        sb.Append(string.IsNullOrWhiteSpace(spec.Title)
            ? texts.FillCount(nameof(ChartTexts.Summary), visible.Count, culture, visible.Count)
            : texts.FillCount(nameof(ChartTexts.SummaryTitled), visible.Count, culture, visible.Count, spec.Title));

        if (!visible.Any(s => s.Data.Y.Any(double.IsFinite)))
        {
            sb.Append(' ').Append(texts.NoData).Append('.');
            return sb.ToString();
        }

        foreach (var (s, d, _) in visible)
        {
            var points = Enumerable.Range(0, d.Count).Where(i => double.IsFinite(d.Y[i])).Select(i => (X: d.X[i], Y: d.Y[i])).ToList();
            sb.Append(' ').Append(texts.FillCount(nameof(ChartTexts.SeriesValues), points.Count, culture, s.DisplayName, points.Count));
            if (points.Count == 0)
            {
                sb.Append('.');
                continue;
            }

            var min = points.MinBy(p => p.Y);
            var max = points.MaxBy(p => p.Y);
            sb.Append(", ").Append(texts.Fill(nameof(ChartTexts.LowestHighest), culture,
                min.Y.ToString("G4", culture), FormatX(min.X, d, xKind, culture), max.Y.ToString("G4", culture), FormatX(max.X, d, xKind, culture)));

            var trend = xKind == XKind.Category ? Trend.None : ComputeTrend(points);
            if (trend != Trend.None)
            {
                sb.Append(", ").Append(texts.Fill(nameof(ChartTexts.Trend), culture, TrendText(trend, texts)));
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
        ChartTexts? texts = null)
    {
        texts ??= ChartTexts.For(culture);
        var sb = new StringBuilder();
        var visible = series.Where(s => s.Visible).ToList();
        if (!string.IsNullOrWhiteSpace(spec.Title))
        {
            sb.Append(spec.Title!.Trim().TrimEnd('.')).Append('.');
        }

        if (!visible.Any(s => s.Data.Y.Any(double.IsFinite)))
        {
            sb.Append(sb.Length > 0 ? " " : "").Append(texts.NoDataShort);
            return sb.ToString();
        }

        foreach (var (s, d, _) in visible)
        {
            if (DescribeSeries(spec, s, d, xKind, culture, texts) is { } text)
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
    public static string? DescribeSeries(ChartSpec spec, SeriesSpec s, SeriesData d, XKind xKind, CultureInfo culture, ChartTexts? texts = null)
    {
        texts ??= ChartTexts.For(culture);
        var points = Enumerable.Range(0, d.Count).Where(i => double.IsFinite(d.Y[i])).Select(i => (X: d.X[i], Y: d.Y[i])).ToList();
        if (points.Count == 0)
        {
            return null;
        }

        var axis = s.YAxisId is { } id ? spec.YAxes.FirstOrDefault(a => a.Id == id) ?? spec.Y : spec.Y;
        var format = s.Format ?? axis.Format;
        string F(double v) => Formatting.ValueFormatter.Format(v, culture, format);
        var (min, max) = (points.Min(p => p.Y), points.Max(p => p.Y));
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
            sb.Append(texts.FillCount(nameof(ChartTexts.SeriesPoints), points.Count, culture, s.DisplayName, points.Count)).Append(", ");
            sb.Append(min == max ? F(min) : texts.Fill(nameof(ChartTexts.ValueRange), culture, F(min), F(max)));
            if (xKind != XKind.Category && ComputeTrend(points) is var t && t != Trend.None)
            {
                sb.Append(", ").Append(TrendText(t, texts));
            }

            return sb.ToString();
        }

        sb.Append(s.DisplayName).Append(": ");
        sb.Append(min == max ? F(min) : texts.Fill(nameof(ChartTexts.ValueRange), culture, F(min), F(max)));
        if (xKind != XKind.Category && points.Count > 1)
        {
            sb.Append(", ").Append(texts.Fill(nameof(ChartTexts.LastValue), culture, F(points[^1].Y)));
            var trend = ComputeTrend(points);
            if (trend != Trend.None)
            {
                sb.Append(", ").Append(TrendText(trend, texts));
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
        if (points.Count < 2)
        {
            return Trend.None;
        }

        double meanX = points.Average(p => p.X), meanY = points.Average(p => p.Y);
        double sxy = 0, sxx = 0, syy = 0;
        foreach (var (x, y) in points)
        {
            sxy += (x - meanX) * (y - meanY);
            sxx += (x - meanX) * (x - meanX);
            syy += (y - meanY) * (y - meanY);
        }

        var rangeX = points.Max(p => p.X) - points.Min(p => p.X);
        var rangeY = points.Max(p => p.Y) - points.Min(p => p.Y);
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
}
