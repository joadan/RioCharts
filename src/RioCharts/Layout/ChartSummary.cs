using System.Globalization;
using System.Text;

namespace RioCharts.Layout;

public enum Trend { None, Increasing, Decreasing, Stable }

/// <summary>Builds a text alternative describing the chart for screen readers.</summary>
public static class ChartSummary
{
    public static string Describe(ChartLayout layout, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var chart = layout.Chart;
        var sb = new StringBuilder();
        var visible = layout.Series.Where(s => s.Series.Visible).OrderBy(s => s.SeriesIndex).ToList();

        sb.Append(string.IsNullOrWhiteSpace(chart.Title) ? "Chart" : $"Chart \"{chart.Title}\"");
        sb.Append(visible.Count == 1 ? " with 1 series." : $" with {visible.Count} series.");

        if (!layout.HasData)
        {
            sb.Append(" No data available.");
            return sb.ToString();
        }

        foreach (var s in visible)
        {
            sb.Append(' ');
            switch (s.Series)
            {
                case XYSeries xy:
                    DescribeXY(sb, xy.Name, xy.Points.Select(p => (p.X, p.Y)).ToList(), s, culture);
                    break;
                case XYZSeries xyz:
                    DescribeXY(sb, xyz.Name, xyz.Points.Select(p => (p.X, p.Y)).ToList(), s, culture);
                    break;
                case ValueSeries value:
                    DescribeValue(sb, value, s);
                    break;
            }
        }

        return sb.ToString().Trim();
    }

    private static void DescribeXY(StringBuilder sb, string name, List<(double X, double Y)> points, SeriesLayout layout, CultureInfo culture)
    {
        var finite = points.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).ToList();
        sb.Append(CultureInfo.InvariantCulture, $"{Name(name)}: {finite.Count} data points");
        if (finite.Count == 0)
        {
            sb.Append('.');
            return;
        }

        var x = layout.LayoutXAxis;
        var y = layout.LayoutYAxis;
        var minX = finite.Min(p => p.X);
        var maxX = finite.Max(p => p.X);
        var minY = finite.Min(p => p.Y);
        var maxY = finite.Max(p => p.Y);
        sb.Append(", X from ").Append(Format(x, minX, culture)).Append(" to ").Append(Format(x, maxX, culture));
        sb.Append(", Y from ").Append(Format(y, minY, culture)).Append(" to ").Append(Format(y, maxY, culture));

        var trend = ComputeTrend(finite);
        if (trend != Trend.None)
        {
            sb.Append(", trend ").Append(trend.ToString().ToLowerInvariant());
        }

        sb.Append('.');
    }

    private static void DescribeValue(StringBuilder sb, ValueSeries series, SeriesLayout layout)
    {
        var slices = layout.Slices ?? [];
        sb.Append(CultureInfo.InvariantCulture, $"{Name(series.Name)}: {slices.Count} slices");
        if (slices.Count > 0)
        {
            var parts = slices.OrderByDescending(s => s.Fraction)
                .Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.Point.Label} {s.Fraction * 100:0.#}%"));
            sb.Append(": ").Append(string.Join(", ", parts));
        }

        sb.Append('.');
    }

    private static string Name(string? name) => string.IsNullOrWhiteSpace(name) ? "Unnamed series" : name;

    private static string Format(AxisLayout? axis, double value, CultureInfo culture) =>
        AxisLabelFormatter.Format(axis?.Axis, value, axis?.TickStep ?? 0, culture);

    /// <summary>
    /// Classifies the trend from a least-squares fit: the fitted change across the X range,
    /// relative to the Y range. More than ±10% (with correlation of at least 0.3) is
    /// increasing/decreasing, otherwise stable.
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

        if (rangeY == 0)
        {
            return Trend.Stable;
        }

        // Noisy data whose fit explains little of the variation counts as stable.
        var correlation = sxy / Math.Sqrt(sxx * syy);
        if (Math.Abs(correlation) < 0.3)
        {
            return Trend.Stable;
        }

        var relativeChange = sxy / sxx * rangeX / rangeY;
        return relativeChange switch
        {
            > 0.1 => Trend.Increasing,
            < -0.1 => Trend.Decreasing,
            _ => Trend.Stable,
        };
    }
}
