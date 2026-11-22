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

    /// <summary>
    /// A domain for a logarithmic axis: [min, max] rounded out to powers of ten (at least one decade). Expects
    /// 0 &lt; min ≤ max; anything else gives [1, 10].
    /// </summary>
    public static (double Min, double Max) LogDomain(double min, double max)
    {
        if (!(min > 0) || !(max >= min) || !double.IsFinite(min) || !double.IsFinite(max))
        {
            return (1, 10);
        }

        var lo = (int)Math.Floor(Math.Log10(min) + 1e-9);
        var hi = (int)Math.Ceiling(Math.Log10(max) - 1e-9);
        if (hi <= lo)
        {
            hi = lo + 1;
        }

        return (Pow10(1, lo), Pow10(1, hi));
    }

    /// <summary>
    /// Ticks for a logarithmic axis within [min, max]: 1, 2 and 5 times each power of ten when there are few decades,
    /// every power of ten when that fits <paramref name="targetCount"/>, else every k-th power of ten.
    /// </summary>
    public static List<double> LogTicks(double min, double max, int targetCount)
    {
        var ticks = new List<double>();
        if (!(min > 0) || !(max >= min) || !double.IsFinite(min) || !double.IsFinite(max))
        {
            return ticks;
        }

        var target = Math.Max(2, targetCount);
        var lo = (int)Math.Floor(Math.Log10(min) + 1e-9);
        var hi = (int)Math.Ceiling(Math.Log10(max) - 1e-9);
        var decades = Math.Max(1, hi - lo);
        int[] mantissas = decades * 3 <= target + 1 ? [1, 2, 5] : [1];
        var every = mantissas.Length > 1 ? 1 : (int)Math.Ceiling(decades / (double)target);
        for (var e = lo; e <= hi; e++)
        {
            if ((e - lo) % every != 0)
            {
                continue;
            }

            foreach (var m in mantissas)
            {
                var value = Pow10(m, e);
                if (value >= min * (1 - 1e-9) && value <= max * (1 + 1e-9))
                {
                    ticks.Add(value);
                }
            }
        }

        return ticks;
    }

    /// <summary>m × 10^e without floating point noise (parsed, so 5e-3 is the double closest to 0.005).</summary>
    private static double Pow10(int m, int e) => double.Parse($"{m}e{e}", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Removes floating point noise and negative zero.</summary>
    public static double Clean(double value)
    {
        var rounded = Math.Round(value, 12);
        return rounded == 0 ? 0 : rounded;
    }
}
