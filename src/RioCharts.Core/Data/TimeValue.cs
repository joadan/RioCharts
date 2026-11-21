namespace RioCharts.Core.Data;

/// <summary>
/// Converts times to and from the numeric x values used inside Core: milliseconds since
/// 1970-01-01 of the wall-clock time <em>as given</em>. No time zone conversion happens:
/// a <see cref="DateTime"/> is used as is whatever its Kind, and a <see cref="DateTimeOffset"/>
/// contributes its own wall-clock <see cref="DateTimeOffset.DateTime"/>.
/// </summary>
public static class TimeValue
{
    private const long EpochTicks = 621355968000000000; // DateTime.UnixEpoch.Ticks

    /// <summary>Milliseconds since 1970-01-01 of the time as given, whatever its Kind.</summary>
    public static double FromDateTime(DateTime value) => (value.Ticks - EpochTicks) / (double)TimeSpan.TicksPerMillisecond;

    /// <summary>Milliseconds since 1970-01-01 of the value's own wall-clock time (the offset is ignored).</summary>
    public static double FromDateTimeOffset(DateTimeOffset value) => FromDateTime(value.DateTime);

    /// <summary>Milliseconds since 1970-01-01 of the date at midnight.</summary>
    public static double FromDateOnly(DateOnly value) => FromDateTime(value.ToDateTime(TimeOnly.MinValue));

    /// <summary>
    /// The UTC offset of a time in milliseconds when it is known: 0 for Kind Utc, the machine's
    /// offset for Kind Local; NaN for Kind Unspecified. Used only when a display zone is set.
    /// </summary>
    public static double OffsetOf(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => 0,
        DateTimeKind.Local => TimeZoneInfo.Local.GetUtcOffset(value).TotalMilliseconds,
        _ => double.NaN,
    };

    /// <summary>The offset of a value that may carry one (DateTime, DateTimeOffset); NaN otherwise.</summary>
    public static double OffsetOf(object? value) => value switch
    {
        DateTimeOffset o => o.Offset.TotalMilliseconds,
        DateTime d => OffsetOf(d),
        _ => double.NaN,
    };

    /// <summary>Back to a wall-clock <see cref="DateTime"/> (Kind Unspecified). Out-of-range values are clamped.</summary>
    public static DateTime ToDateTime(double milliseconds)
    {
        if (double.IsNaN(milliseconds))
        {
            return DateTime.MinValue;
        }

        var ticks = milliseconds * TimeSpan.TicksPerMillisecond + EpochTicks;
        if (ticks <= DateTime.MinValue.Ticks)
        {
            return DateTime.MinValue;
        }

        if (ticks >= DateTime.MaxValue.Ticks)
        {
            return DateTime.MaxValue;
        }

        return new DateTime((long)Math.Round(ticks), DateTimeKind.Unspecified);
    }
}
