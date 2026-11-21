using RioCharts.Core;
using RioCharts.Core.Data;

namespace RioCharts.Benchmarks;

/// <summary>Deterministic test data: hourly supply air temperatures in °C and category values.</summary>
internal static class Data
{
    public static readonly DateTime Start = new(2026, 1, 1);

    public static double Supply(int i) => 19 + 1.5 * Math.Sin(i / 24.0 * 2 * Math.PI) + 0.4 * Math.Sin(i * 1.7);

    public static double Extract(int i) => 22 + 0.8 * Math.Sin((i - 3) / 24.0 * 2 * Math.PI) + 0.2 * Math.Cos(i * 1.3);

    public static DateTime[] Hours(int n) => Enumerable.Range(0, n).Select(i => Start.AddHours(i)).ToArray();

    public static SeriesData Hourly(int n, Func<int, double> value) =>
        SeriesData.From(Hours(n), Enumerable.Range(0, n).Select(value).ToArray());

    public static ChartSpec TwoLines() => new()
    {
        Title = "Air handling unit",
        Y = new AxisSpec { Title = "°C", Format = "0.0" },
        Series = [new SeriesSpec { Id = "s", Name = "Supply" }, new SeriesSpec { Id = "e", Name = "Extract" }],
    };

    public static Dictionary<string, SeriesData> TwoLinesData(int n) =>
        new() { ["s"] = Hourly(n, Supply), ["e"] = Hourly(n, Extract) };
}
