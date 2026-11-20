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

    public static double FromDateTime(DateTime value) => (value.Ticks - EpochTicks) / (double)TimeSpan.TicksPerMillisecond;

    public static double FromDateTimeOffset(DateTimeOffset value) => FromDateTime(value.DateTime);

    public static double FromDateOnly(DateOnly value) => FromDateTime(value.ToDateTime(TimeOnly.MinValue));

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
