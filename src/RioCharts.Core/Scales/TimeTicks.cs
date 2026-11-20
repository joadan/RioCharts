using RioCharts.Core.Data;

namespace RioCharts.Core.Scales;

public enum TimeUnit { Second, Minute, Hour, Day, Week, Month, Year }

/// <summary>A calendar tick interval such as "every 6 hours" or "every 3 months".</summary>
public readonly record struct TimeInterval(TimeUnit Unit, int Step)
{
    /// <summary>Approximate length in milliseconds, for choosing an interval.</summary>
    public double ApproxMilliseconds => Step * Unit switch
    {
        TimeUnit.Second => 1_000d,
        TimeUnit.Minute => 60_000d,
        TimeUnit.Hour => 3_600_000d,
        TimeUnit.Day => 86_400_000d,
        TimeUnit.Week => 604_800_000d,
        TimeUnit.Month => 2_629_746_000d,
        _ => 31_556_952_000d,
    };
}

/// <summary>
/// Calendar-aware ticks for time axes: whole minutes, hours, midnights, Mondays, first of
/// the month, January 1. Calendar logic runs on the wall-clock time as given.
/// </summary>
public static class TimeTicks
{
    public static IReadOnlyList<TimeInterval> Intervals { get; } =
    [
        new(TimeUnit.Second, 1), new(TimeUnit.Second, 5), new(TimeUnit.Second, 15), new(TimeUnit.Second, 30),
        new(TimeUnit.Minute, 1), new(TimeUnit.Minute, 5), new(TimeUnit.Minute, 15), new(TimeUnit.Minute, 30),
        new(TimeUnit.Hour, 1), new(TimeUnit.Hour, 3), new(TimeUnit.Hour, 6), new(TimeUnit.Hour, 12),
        new(TimeUnit.Day, 1), new(TimeUnit.Day, 2), new(TimeUnit.Week, 1),
        new(TimeUnit.Month, 1), new(TimeUnit.Month, 3), new(TimeUnit.Month, 6),
        new(TimeUnit.Year, 1), new(TimeUnit.Year, 2), new(TimeUnit.Year, 5), new(TimeUnit.Year, 10),
        new(TimeUnit.Year, 25), new(TimeUnit.Year, 50), new(TimeUnit.Year, 100), new(TimeUnit.Year, 250),
        new(TimeUnit.Year, 500), new(TimeUnit.Year, 1000),
    ];

    /// <summary>The smallest interval that gives at most <paramref name="maxCount"/> ticks.</summary>
    public static TimeInterval Choose(double minMs, double maxMs, int maxCount)
    {
        var span = Math.Max(0, maxMs - minMs);
        foreach (var interval in Intervals)
        {
            if (span / interval.ApproxMilliseconds <= Math.Max(1, maxCount))
            {
                return interval;
            }
        }

        return Intervals[^1];
    }

    /// <summary>Intervals from <paramref name="start"/> upward, for trying until labels fit.</summary>
    public static IEnumerable<TimeInterval> FromSmallest(double minMs, double maxMs, int maxCount)
    {
        var first = Choose(minMs, maxMs, maxCount);
        var started = false;
        foreach (var interval in Intervals)
        {
            started |= interval == first;
            if (started)
            {
                yield return interval;
            }
        }
    }

    /// <summary>Tick times (as milliseconds) within [minMs, maxMs].</summary>
    public static List<double> Generate(double minMs, double maxMs, TimeInterval interval)
    {
        var ticks = new List<double>();
        if (!(maxMs >= minMs))
        {
            return ticks;
        }

        var min = TimeValue.ToDateTime(minMs);
        var max = TimeValue.ToDateTime(maxMs);
        var t = Floor(min, interval);
        for (var guard = 0; guard < 2000 && t <= max; guard++)
        {
            if (t >= min && IsAligned(t, interval))
            {
                ticks.Add(TimeValue.FromDateTime(t));
            }

            var next = Advance(t, interval);
            if (next <= t)
            {
                break;
            }

            t = next;
        }

        return ticks;
    }

    private static DateTime Floor(DateTime t, TimeInterval i)
    {
        try
        {
            return i.Unit switch
            {
                TimeUnit.Second => new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second - t.Second % i.Step),
                TimeUnit.Minute => new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute - t.Minute % i.Step, 0),
                TimeUnit.Hour => new DateTime(t.Year, t.Month, t.Day, t.Hour - t.Hour % i.Step, 0, 0),
                TimeUnit.Day => t.Date,
                TimeUnit.Week => t.Date.AddDays(-(((int)t.DayOfWeek + 6) % 7)), // back to Monday
                TimeUnit.Month => new DateTime(t.Year, t.Month - (t.Month - 1) % i.Step, 1),
                _ => new DateTime(Math.Max(1, t.Year - t.Year % i.Step), 1, 1),
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return t;
        }
    }

    // Multi-day steps align to the day of month (1, 3, 5, ...), so they advance one day at a time.
    private static bool IsAligned(DateTime t, TimeInterval i) =>
        i.Unit != TimeUnit.Day || i.Step == 1 || (t.Day - 1) % i.Step == 0;

    private static DateTime Advance(DateTime t, TimeInterval i)
    {
        try
        {
            return i.Unit switch
            {
                TimeUnit.Second => t.AddSeconds(i.Step),
                TimeUnit.Minute => t.AddMinutes(i.Step),
                TimeUnit.Hour => t.AddHours(i.Step),
                TimeUnit.Day => t.AddDays(1),
                TimeUnit.Week => t.AddDays(7 * i.Step),
                TimeUnit.Month => t.AddMonths(i.Step),
                _ => t.AddYears(i.Step),
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return t;
        }
    }
}
