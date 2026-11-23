using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;

namespace RioCharts.Core.Layout;

public static partial class ChartLayout
{
    /// <summary>The display zone conversion, or null when times are shown as given (the default).</summary>
    private static TimeConversion? ResolveTime(ChartDefinition spec, IReadOnlyDictionary<string, SeriesData> data, LayoutOptions options, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(spec.TimeZone))
        {
            return null;
        }

        var zone = TimeConversion.FindZone(spec.TimeZone, options.ViewerTimeZone);
        if (zone is null)
        {
            warnings.Add($"timeZone: '{spec.TimeZone}' is not a known time zone (use \"UTC\", \"local\" or an IANA id such as \"Europe/Stockholm\"); times are shown as given.");
            return null;
        }

        if (spec.UnspecifiedTimes is null && data.Values.Any(d => d.Kind == XKind.Time && d.Count > 0 && (d.TimeOffsets is null || d.TimeOffsets.Any(double.IsNaN))))
        {
            warnings.Add("unspecifiedTimes: some times have no zone (DateTime with Kind Unspecified); they are read as UTC. Set unspecifiedTimes to Utc or DisplayZone.");
        }

        return new TimeConversion(zone, spec.UnspecifiedTimes ?? UnspecifiedTimes.Utc);
    }

    /// <summary>
    /// Formats x values for tooltips and annotations. With a display zone, instants are shown as
    /// wall-clock time in that zone, followed by its offset when it differs from the viewer's.
    /// </summary>
    private static Func<double, string> TimeFormat(XKind kind, IEnumerable<double> values, CultureInfo culture, string? format, TimeConversion? time,
        string? viewerZone)
    {
        if (time is null || kind != XKind.Time)
        {
            return ValueFormatter.ForX(kind, values, culture, format);
        }

        var local = ValueFormatter.ForX(kind, values.Select(time.ToDisplay), culture, format);
        var viewer = viewerZone is null ? null : TimeConversion.FindZone(viewerZone, null);
        var showZone = viewer is null || !SameZone(viewer, time.Zone);
        return v => showZone ? $"{local(time.ToDisplay(v))} {time.OffsetLabel(v)}" : local(time.ToDisplay(v));
    }

    private static bool SameZone(TimeZoneInfo a, TimeZoneInfo b) =>
        a.Id == b.Id || (TimeZoneInfo.TryConvertIanaIdToWindowsId(a.Id, out var wa) && wa == b.Id)
        || (TimeZoneInfo.TryConvertIanaIdToWindowsId(b.Id, out var wb) && wb == a.Id)
        || (a.BaseUtcOffset == TimeSpan.Zero && b.BaseUtcOffset == TimeSpan.Zero && !a.SupportsDaylightSavingTime && !b.SupportsDaylightSavingTime);

    /// <summary>
    /// Calendar ticks in the display zone: generated on wall-clock time there (midnights, Mondays,
    /// the 1st of the month), then turned back into instants. A tick that does not exist on that
    /// day (spring) or repeats (autumn) is placed once.
    /// </summary>
    private static (List<double> Instants, List<double> Wall) ZonedTicks(double min, double max, TimeInterval interval, TimeConversion time)
    {
        var wall = TimeTicks.Generate(time.ToDisplay(min), time.ToDisplay(max), interval);
        var instants = new List<double>(wall.Count);
        var walls = new List<double>(wall.Count);
        foreach (var w in wall)
        {
            var instant = time.FromDisplay(w);
            if (instant >= min - 1 && instant <= max + 1 && (instants.Count == 0 || instant > instants[^1]))
            {
                instants.Add(instant);
                walls.Add(w);
            }
        }

        return (instants, walls);
    }
}
