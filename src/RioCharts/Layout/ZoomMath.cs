namespace RioCharts.Layout;

/// <summary>Zoom and pan calculations on a single axis, kept inside the full data range.</summary>
public static class ZoomMath
{
    /// <summary>Smallest visible span as a fraction of the full range.</summary>
    public const double MinSpanFraction = 1e-6;

    /// <summary>
    /// Zooms around <paramref name="anchor"/> (a data value that stays at the same screen position).
    /// A factor above 1 zooms in, below 1 zooms out.
    /// </summary>
    public static AxisRange Zoom(AxisRange current, AxisRange full, double factor, double anchor)
    {
        if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor))
        {
            return Clamp(current, full);
        }

        anchor = Math.Clamp(anchor, current.Minimum, current.Maximum);
        var min = anchor - (anchor - current.Minimum) / factor;
        var max = anchor + (current.Maximum - anchor) / factor;
        return Clamp(new AxisRange(min, max), full);
    }

    /// <summary>Moves the range by <paramref name="delta"/> data units without changing its span.</summary>
    public static AxisRange Pan(AxisRange current, AxisRange full, double delta) =>
        Clamp(new AxisRange(current.Minimum + delta, current.Maximum + delta), full);

    /// <summary>
    /// Keeps a range inside the full range: the span is limited to the full span (and a tiny
    /// minimum), and a range that runs past an end is shifted back inside.
    /// </summary>
    public static AxisRange Clamp(AxisRange range, AxisRange full)
    {
        var fullSpan = full.Maximum - full.Minimum;
        if (fullSpan <= 0)
        {
            return full;
        }

        var min = Math.Min(range.Minimum, range.Maximum);
        var max = Math.Max(range.Minimum, range.Maximum);
        var span = Math.Clamp(max - min, fullSpan * MinSpanFraction, fullSpan);
        var center = (min + max) / 2;
        min = center - span / 2;
        max = center + span / 2;

        if (min < full.Minimum)
        {
            max += full.Minimum - min;
            min = full.Minimum;
        }

        if (max > full.Maximum)
        {
            min -= max - full.Maximum;
            max = full.Maximum;
        }

        return new AxisRange(Math.Max(min, full.Minimum), Math.Min(max, full.Maximum));
    }

    /// <summary>True when the range covers (practically) the whole full range.</summary>
    public static bool IsFull(AxisRange range, AxisRange full)
    {
        var tolerance = (full.Maximum - full.Minimum) * 1e-9;
        return range.Minimum <= full.Minimum + tolerance && range.Maximum >= full.Maximum - tolerance;
    }
}
