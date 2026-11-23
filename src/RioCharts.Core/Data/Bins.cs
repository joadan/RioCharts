using RioCharts.Core.Scales;

namespace RioCharts.Core.Data;

/// <summary>
/// Equal-width bins for a histogram: <see cref="Count"/> bins of <see cref="Width"/> from
/// <see cref="Start"/>. Each bin holds the values from its lower edge up to (not including) its upper
/// edge; the last bin also holds a value on its upper edge.
/// </summary>
/// <param name="Start">The lower edge of the first bin.</param>
/// <param name="Width">The width of every bin.</param>
/// <param name="Count">The number of bins (0 when there are no values).</param>
public readonly record struct Bins(double Start, double Width, int Count)
{
    /// <summary>At most this many bins; a narrower width is widened to fit.</summary>
    public const int MaxCount = 1000;

    /// <summary>The lower edge of bin <paramref name="index"/> (<see cref="Count"/> gives the last bin's upper edge).</summary>
    public double Edge(int index) => NiceTicks.Clean(Start + index * Width);

    /// <summary>The middle of bin <paramref name="index"/>.</summary>
    public double Center(int index) => NiceTicks.Clean(Start + (index + 0.5) * Width);

    /// <summary>The bin a value falls in, or -1 when it is outside the bins (or not a number).</summary>
    public int IndexOf(double value)
    {
        if (!double.IsFinite(value) || Count == 0 || value < Start || value > Edge(Count))
        {
            return -1;
        }

        // A little tolerance, so a value on an edge (0.3 with bins of 0.1) lands in the bin it starts.
        var index = (int)Math.Floor((value - Start) / Width + 1e-9);
        return Math.Clamp(index, 0, Count - 1);
    }

    /// <summary>
    /// Bins that cover <paramref name="values"/> (NaN is skipped), with edges on multiples of a nice
    /// width (1, 2 or 5 times a power of ten). <paramref name="width"/> sets the width (used as given);
    /// <paramref name="count"/> asks for about that many bins. Neither: the Freedman–Diaconis rule
    /// (twice the interquartile range over the cube root of the count), or Sturges' rule when the
    /// quartiles are equal.
    /// </summary>
    public static Bins For(ReadOnlySpan<double> values, double? width = null, int? count = null)
    {
        var finite = new List<double>(values.Length);
        foreach (var v in values)
        {
            if (double.IsFinite(v))
            {
                finite.Add(v);
            }
        }

        if (finite.Count == 0)
        {
            return new Bins(0, width is > 0 and var w0 && double.IsFinite(w0) ? w0 : 1, 0);
        }

        finite.Sort();
        var (min, max) = (finite[0], finite[^1]);
        var span = max - min;
        double binWidth;
        if (width is > 0 and var given && double.IsFinite(given))
        {
            binWidth = given;
        }
        else if (span == 0)
        {
            binWidth = NiceTicks.Step(min == 0 ? 1 : Math.Abs(min) * 0.1, 1); // one bin around the value
        }
        else
        {
            var target = count is > 0 and var c ? c : AutoCount(finite, span);
            binWidth = NiceTicks.Step(span, Math.Clamp(target, 1, MaxCount));
        }

        if (span / binWidth > MaxCount)
        {
            binWidth = NiceTicks.Step(span, MaxCount);
        }

        var start = NiceTicks.Clean(Math.Floor(min / binWidth + 1e-9) * binWidth);
        var bins = Math.Max(1, (int)Math.Ceiling(NiceTicks.Clean((max - start) / binWidth)));
        if (NiceTicks.Clean(start + bins * binWidth) < max)
        {
            bins++; // rounding: the last bin must reach the largest value
        }

        return new Bins(start, binWidth, bins);
    }

    private static int AutoCount(List<double> sorted, double span)
    {
        var n = sorted.Count;
        var iqr = Quantile(sorted, 0.75) - Quantile(sorted, 0.25);
        var sturges = (int)Math.Ceiling(Math.Log2(n)) + 1;
        if (!(iqr > 0))
        {
            return sturges;
        }

        var fd = (int)Math.Ceiling(span / (2 * iqr / Math.Cbrt(n)));
        return Math.Clamp(fd, Math.Min(sturges, 5), 100); // very skewed data: not hundreds of near-empty bins
    }

    private static double Quantile(List<double> sorted, double q)
    {
        var position = (sorted.Count - 1) * q;
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(sorted.Count - 1, lower + 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }
}
