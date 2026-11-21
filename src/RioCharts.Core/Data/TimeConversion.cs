using System.Globalization;

namespace RioCharts.Core.Data;

/// <summary>How times without a zone (DateTime with Kind Unspecified, DateOnly, plain strings) are read when a display zone is set.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<UnspecifiedTimes>))]
public enum UnspecifiedTimes
{
    /// <summary>They are UTC.</summary>
    Utc,

    /// <summary>They are wall-clock times in the display zone.</summary>
    DisplayZone,
}

/// <summary>
/// Time handling for a display zone: data is plotted on instants (UTC milliseconds), while ticks,
/// labels and tooltips use wall-clock time in <see cref="Zone"/>. Lines never double back across
/// a daylight saving change, and a day with a repeated hour is 25 hours wide.
/// </summary>
public sealed class TimeConversion
{
    /// <summary>Creates a conversion for a display zone.</summary>
    /// <param name="zone">The zone ticks, labels and tooltips are shown in.</param>
    /// <param name="unspecified">How times without a zone are read.</param>
    public TimeConversion(TimeZoneInfo zone, UnspecifiedTimes unspecified)
    {
        Zone = zone ?? throw new ArgumentNullException(nameof(zone));
        Unspecified = unspecified;
    }

    /// <summary>The display zone.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>How times without a zone are read.</summary>
    public UnspecifiedTimes Unspecified { get; }

    /// <summary>Wall-clock milliseconds in the display zone for an instant.</summary>
    public double ToDisplay(double instant)
    {
        if (!double.IsFinite(instant))
        {
            return instant;
        }

        var utc = DateTime.SpecifyKind(TimeValue.ToDateTime(instant), DateTimeKind.Utc);
        return instant + Zone.GetUtcOffset(utc).TotalMilliseconds;
    }

    /// <summary>
    /// The instant of a wall-clock time in the display zone. A repeated time (autumn) maps to its
    /// first occurrence; a skipped time (spring) uses the offset from before the change.
    /// </summary>
    public double FromDisplay(double wall)
    {
        if (!double.IsFinite(wall))
        {
            return wall;
        }

        var local = DateTime.SpecifyKind(TimeValue.ToDateTime(wall), DateTimeKind.Unspecified);
        var offset = Zone.IsAmbiguousTime(local)
            ? Zone.GetAmbiguousTimeOffsets(local).Max()
            : Zone.IsInvalidTime(local) ? Zone.GetUtcOffset(local.AddHours(-3)) : Zone.GetUtcOffset(local);
        return wall - offset.TotalMilliseconds;
    }

    /// <summary>The instant of a time value as given: its offset when known, else per <see cref="Unspecified"/>.</summary>
    public double ToInstant(double wall, double offset) =>
        double.IsFinite(offset) ? wall - offset : Unspecified == UnspecifiedTimes.Utc ? wall : FromDisplay(wall);

    /// <summary>The instant of a value from an annotation or range: DateTime, DateTimeOffset, DateOnly or an ISO 8601 string.</summary>
    public double ToInstant(object? value) => value switch
    {
        DateTimeOffset o => TimeValue.FromDateTime(o.UtcDateTime),
        DateTime d => ToInstant(TimeValue.FromDateTime(d), TimeValue.OffsetOf(d)),
        DateOnly d => ToInstant(TimeValue.FromDateOnly(d), double.NaN),
        string s => FromString(s),
        System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } j => FromString(j.GetString() ?? ""),
        _ => AxisValue.ToDouble(value, XKind.Numeric), // milliseconds as a number
    };

    /// <summary>The instant as a DateTimeOffset in the display zone.</summary>
    public DateTimeOffset ToDateTimeOffset(double instant)
    {
        var utc = DateTime.SpecifyKind(TimeValue.ToDateTime(instant), DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(utc), Zone);
    }

    /// <summary>"UTC" or "UTC+01:00": the zone's offset at an instant, for tooltips.</summary>
    public string OffsetLabel(double instant)
    {
        var offset = TimeSpan.FromMilliseconds(ToDisplay(instant) - instant);
        return offset == TimeSpan.Zero ? "UTC" : "UTC" + (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Time data converted to instants (sorted again if the conversion changed the order).</summary>
    public SeriesData Convert(SeriesData data)
    {
        if (data.Kind != XKind.Time)
        {
            return data;
        }

        var x = new double[data.Count];
        for (var i = 0; i < x.Length; i++)
        {
            x[i] = ToInstant(data.X[i], data.TimeOffsets?[i] ?? double.NaN);
        }

        return data.WithX(x);
    }

    private double FromString(string s)
    {
        // With an explicit offset or "Z" the string is an instant; without, it is unspecified.
        var hasZone = s.EndsWith('Z') || s.EndsWith('z') || System.Text.RegularExpressions.Regex.IsMatch(s, @"T.*[+-]\d{2}:?\d{2}$");
        if (hasZone && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var o))
        {
            return TimeValue.FromDateTime(o.UtcDateTime);
        }

        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? ToInstant(TimeValue.FromDateTime(d), double.NaN)
            : double.NaN;
    }

    /// <summary>
    /// Resolves a zone name: "UTC", "local" (the viewer's zone, or UTC when unknown) or an IANA
    /// or Windows id. Null when the name is unknown.
    /// </summary>
    public static TimeZoneInfo? FindZone(string name, string? viewerZone)
    {
        if (string.Equals(name, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }

        if (string.Equals(name, "local", StringComparison.OrdinalIgnoreCase))
        {
            return viewerZone is null ? TimeZoneInfo.Utc : FindZone(viewerZone, null);
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(name);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }
}
