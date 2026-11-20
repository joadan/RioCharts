using System.Globalization;
using System.Text;
using RioCharts.Core.Data;

namespace RioCharts.Core.Layout;

public enum Trend { None, Increasing, Decreasing, Stable }

/// <summary>Text alternative for a chart: series, ranges and trends.</summary>
public static class ChartDescriber
{
    public static string Describe(ChartSpec spec, IReadOnlyList<(SeriesSpec Spec, SeriesData Data, bool Visible)> series, XKind xKind, CultureInfo culture)
    {
        var sb = new StringBuilder();
        var visible = series.Where(s => s.Visible).ToList();
        sb.Append(string.IsNullOrWhiteSpace(spec.Title) ? "Chart" : $"Chart \"{spec.Title}\"");
        sb.Append(visible.Count == 1 ? " with 1 series." : $" with {visible.Count} series.");

        if (!visible.Any(s => s.Data.Y.Any(double.IsFinite)))
        {
            sb.Append(" No data available.");
            return sb.ToString();
        }

        foreach (var (s, d, _) in visible)
        {
            var points = Enumerable.Range(0, d.Count).Where(i => double.IsFinite(d.Y[i])).Select(i => (X: d.X[i], Y: d.Y[i])).ToList();
            sb.Append(' ').Append(s.DisplayName).Append(": ").Append(points.Count.ToString(CultureInfo.InvariantCulture)).Append(" values");
            if (points.Count == 0)
            {
                sb.Append('.');
                continue;
            }

            var min = points.MinBy(p => p.Y);
            var max = points.MaxBy(p => p.Y);
            sb.Append(", lowest ").Append(min.Y.ToString("G4", culture)).Append(" at ").Append(FormatX(min.X, d, xKind, culture));
            sb.Append(", highest ").Append(max.Y.ToString("G4", culture)).Append(" at ").Append(FormatX(max.X, d, xKind, culture));

            var trend = xKind == XKind.Category ? Trend.None : ComputeTrend(points);
            if (trend != Trend.None)
            {
                sb.Append(", trend ").Append(trend.ToString().ToLowerInvariant());
            }

            sb.Append('.');
        }

        return sb.ToString();
    }

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
