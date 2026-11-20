namespace RioCharts.Docs;

/// <summary>Deterministic sample data for the examples (building climate and energy, temperatures in °C).</summary>
public static class SampleData
{
    public sealed record Reading(DateTime Time, double? Value);

    public sealed record Sales(string Quarter, double LastYear, double ThisYear);

    public sealed record Share(string Name, double Value);

    public sealed record Monthly(string Month, double Heating, double Ventilation, double Cooling);

    public static readonly DateTime Start = new(2026, 11, 16);

    private static double Cycle(DateTime t) => Math.Sin((t.Hour + t.Minute / 60.0 - 9) / 24 * 2 * Math.PI);

    private static List<Reading> Series(Func<int, DateTime, double?> value, int count = 2 * 96, int minutes = 15) =>
        Enumerable.Range(0, count).Select(i => Start.AddMinutes(minutes * i)).Select((t, i) => new Reading(t, value(i, t))).ToList();

    public static readonly List<Reading> Supply = Series((i, t) => Math.Round(19 + 1.5 * Cycle(t) + Noise(i, 1) * 0.4, 1));

    public static readonly List<Reading> Extract = Series((i, t) => Math.Round(22 + 0.8 * Cycle(t) + Noise(i, 2) * 0.3, 1));

    /// <summary>Outdoor temperature with a sensor gap.</summary>
    public static readonly List<Reading> Outdoor = Series((i, t) => i is >= 60 and < 75 ? null : Math.Round(4 + 5 * Cycle(t) + Noise(i, 3), 1));

    public static readonly List<Reading> Heating = Series((i, t) => Math.Round(Math.Max(0, 14 - 9 * Cycle(t) + Noise(i, 4) * 2), 1));

    public static readonly List<Reading> Ventilation = Series((i, t) => Math.Round(t.Hour is >= 7 and < 18 ? 6 + Noise(i, 5) : 2 + Noise(i, 6) * 0.5, 1));

    public static readonly List<Reading> Daily = Enumerable.Range(0, 35)
        .Select(d => Start.AddDays(d - 34))
        .Select((d, i) => new Reading(d, Math.Round(220 + 60 * Math.Sin(d.DayOfYear / 5.0) + Noise(i, 7) * 25, 0)))
        .ToList();

    public static readonly List<Reading> Setpoints =
    [
        new(Start, 21), new(Start.AddHours(6), 21), new(Start.AddHours(7), 22.5), new(Start.AddHours(17), 22.5),
        new(Start.AddHours(18), 20), new(Start.AddHours(30), 20), new(Start.AddHours(31), 22.5), new(Start.AddHours(41), 22.5),
        new(Start.AddHours(42), 20), new(Start.AddHours(48), 20),
    ];

    public static readonly List<Sales> Quarters =
    [
        new("Q1", 182_000, 214_000), new("Q2", 236_000, 251_000), new("Q3", 198_000, 243_000), new("Q4", 271_000, 305_000),
    ];

    public static readonly List<Monthly> Mix =
    [
        new("Jan", 42, 18, 0), new("Feb", 38, 17, 0), new("Mar", 29, 15, 1), new("Apr", 17, 11, 3), new("May", 8, 9, 8), new("Jun", 4, 8, 15),
    ];

    public static readonly List<Share> EnergyUse =
    [
        new("Heating", 412), new("Ventilation", 236), new("Cooling", 148), new("Lighting", 97), new("Pumps", 31), new("Elevators", 9), new("Controls", 6),
    ];

    public static readonly List<Share> Health = [new("Supply fan", 87), new("Extract fan", 74), new("Filter", 41), new("Heat recovery", 92)];

    /// <summary>Hourly UTC readings across the end of summer time in Europe (25 Oct 2026).</summary>
    public static readonly (DateTimeOffset Time, double Value)[] DstReadings = Enumerable.Range(0, 36)
        .Select(h => (new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero).AddHours(h), Math.Round(7 + 4 * Math.Sin((h - 3) / 24.0 * 2 * Math.PI), 1)))
        .ToArray();

    /// <summary>A random walk as plain arrays (the columnar fast path).</summary>
    public static (DateTime[] Times, double[] Values) Big(int count)
    {
        var times = new DateTime[count];
        var values = new double[count];
        var value = 20.0;
        for (var i = 0; i < count; i++)
        {
            times[i] = Start.AddDays(-30).AddSeconds(i * 13);
            value += Noise(i, 9) * 0.08;
            values[i] = value;
        }

        return (times, values);
    }

    /// <summary>Repeatable noise in [-0.5, 0.5).</summary>
    public static double Noise(int i, int seed)
    {
        var x = (uint)(i * 374761393 + seed * 668265263);
        x = (x ^ (x >> 13)) * 1274126177;
        return (x ^ (x >> 16)) / (double)uint.MaxValue - 0.5;
    }
}
