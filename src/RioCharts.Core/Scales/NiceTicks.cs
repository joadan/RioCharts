namespace RioCharts.Core.Scales;

/// <summary>"Nice" numeric ticks: steps of 1, 2 or 5 times a power of ten.</summary>
public static class NiceTicks
{
    /// <summary>
    /// The smallest nice step (1, 2, 5 or 10 times a power of ten) that is at least
    /// <paramref name="span"/> / <paramref name="targetCount"/>. Returns 1 for an empty or invalid span.
    /// </summary>
    public static double Step(double span, int targetCount)
    {
        var raw = Math.Abs(span) / Math.Max(1, targetCount);
        if (!(raw > 0) || !double.IsFinite(raw))
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var fraction = raw / magnitude;
        var nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    /// <summary>Rounds a domain out to multiples of a nice step.</summary>
    public static (double Min, double Max, double Step) NiceDomain(double min, double max, int targetCount)
    {
        var step = Step(max - min, targetCount);
        var niceMin = Math.Floor(min / step + 1e-9) * step;
        var niceMax = Math.Ceiling(max / step - 1e-9) * step;
        if (!(niceMax > niceMin))
        {
            niceMax = niceMin + step;
        }

        // Rounding out can change the best step; recompute once.
        step = Step(niceMax - niceMin, targetCount);
        niceMin = Math.Floor(min / step + 1e-9) * step;
        niceMax = Math.Ceiling(max / step - 1e-9) * step;
        if (!(niceMax > niceMin))
        {
            niceMax = niceMin + step;
        }

        return (Clean(niceMin), Clean(niceMax), step);
    }

    /// <summary>
    /// A domain with exactly <paramref name="intervals"/> nice steps that covers [min, max], so a
    /// second axis can put its ticks on the first axis' grid lines. Null when none is found.
    /// </summary>
    public static (double Min, double Max, double Step)? AlignedDomain(double min, double max, int intervals)
    {
        if (intervals < 1 || !double.IsFinite(min) || !double.IsFinite(max))
        {
            return null;
        }

        if (max < min)
        {
            (min, max) = (max, min);
        }

        var step = Step(max - min, intervals);
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var lo = Math.Floor(min / step + 1e-9) * step;
            if (lo + intervals * step >= max - step * 1e-9)
            {
                return (Clean(lo), Clean(lo + intervals * step), step);
            }

            // Next nice step: 1, 2, 5, 10, ...
            var magnitude = Math.Pow(10, Math.Floor(Math.Log10(step) + 1e-9));
            var fraction = Math.Round(step / magnitude, 6);
            step = (fraction < 2 ? 2 : fraction < 5 ? 5 : 10) * magnitude;
        }

        return null;
    }

    /// <summary>Multiples of step within [min, max], rounded to avoid floating point noise.</summary>
    public static List<double> Ticks(double min, double max, double step)
    {
        var ticks = new List<double>();
        if (!(step > 0) || !(max >= min))
        {
            return ticks;
        }

        var epsilon = step * 1e-9;
        var decimals = Math.Clamp((int)-Math.Floor(Math.Log10(step)) + 2, 0, 15);
        var first = Math.Ceiling((min - epsilon) / step);
        for (var i = 0; i < 1000; i++)
        {
            var value = Math.Round((first + i) * step, decimals);
            if (value > max + epsilon)
            {
                break;
            }

            ticks.Add(value == 0 ? 0 : value);
        }

        return ticks;
    }

    /// <summary>Removes floating point noise and negative zero.</summary>
    public static double Clean(double value)
    {
        var rounded = Math.Round(value, 12);
        return rounded == 0 ? 0 : rounded;
    }
}
