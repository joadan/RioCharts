namespace RioCharts.Layout;

/// <summary>Computes "nice" axis ranges and tick values.</summary>
public static class NiceScale
{
    /// <summary>Returns a nice step for spanning <paramref name="span"/> with about <paramref name="targetTickCount"/> ticks.</summary>
    public static double Step(double span, int targetTickCount)
    {
        var intervals = Math.Max(1, targetTickCount - 1);
        var raw = Math.Abs(span) / intervals;
        if (raw <= 0 || double.IsNaN(raw) || double.IsInfinity(raw))
        {
            return 1;
        }

        var exponent = Math.Floor(Math.Log10(raw));
        var magnitude = Math.Pow(10, exponent);
        var fraction = raw / magnitude;
        var nice = fraction switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 2.5 => 2.5,
            <= 5 => 5,
            _ => 10,
        };
        return nice * magnitude;
    }

    private static readonly double[] DateSteps =
    [
        1.0 / 1440, 5.0 / 1440, 15.0 / 1440, 30.0 / 1440, // minutes
        1.0 / 24, 3.0 / 24, 6.0 / 24, 12.0 / 24,          // hours
        1, 2, 7, 14, 30, 91, 182, 365,                    // days
    ];

    /// <summary>Step for date/time axes, in days, picked from calendar-friendly sizes.</summary>
    public static double DateStep(double spanDays, int targetTickCount)
    {
        var raw = Math.Abs(spanDays) / Math.Max(1, targetTickCount - 1);
        foreach (var step in DateSteps)
        {
            if (step >= raw)
            {
                return step;
            }
        }

        // Beyond a year: whole multiples of years.
        return Math.Ceiling(raw / 365) * 365;
    }

    /// <summary>
    /// Ticks for date axes (values are OLE Automation dates). Steps of about a month or more
    /// land on the first of the month (or January 1 for yearly steps); shorter steps use
    /// multiples of the step, which line up with midnight, whole hours and minutes.
    /// </summary>
    public static IReadOnlyList<double> DateTicks(double min, double max, double stepDays)
    {
        if (stepDays < 28)
        {
            return Ticks(min, max, stepDays);
        }

        var months = stepDays >= 365 ? 12 * (int)Math.Max(1, Math.Round(stepDays / 365)) : (int)Math.Max(1, Math.Round(stepDays / 30.4));
        var ticks = new List<double>();
        DateTime start, end;
        try
        {
            start = DateTime.FromOADate(min);
            end = DateTime.FromOADate(max);
        }
        catch (ArgumentException)
        {
            return Ticks(min, max, stepDays);
        }

        var date = new DateTime(start.Year, start.Month, 1);
        if (date < start)
        {
            date = date.AddMonths(1);
        }

        for (var i = 0; i < 1000 && date <= end; i++)
        {
            var monthIndex = date.Year * 12 + date.Month - 1;
            if (monthIndex % months == 0)
            {
                ticks.Add(date.ToOADate());
            }

            date = date.AddMonths(1);
        }

        return ticks;
    }

    /// <summary>Tick values between min and max (inclusive) at multiples of step.</summary>
    public static IReadOnlyList<double> Ticks(double min, double max, double step)
    {
        var ticks = new List<double>();
        if (step <= 0 || max < min || double.IsNaN(step))
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

            ticks.Add(value == 0 ? 0 : value); // avoid -0
        }

        return ticks;
    }
}
