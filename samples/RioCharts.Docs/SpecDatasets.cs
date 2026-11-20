using RioCharts.Core.Specs;

namespace RioCharts.Docs;

/// <summary>Datasets the JSON spec playground can chart.</summary>
public static class SpecDatasets
{
    public static IReadOnlyDictionary<string, ChartDataset> ByName { get; } = Create().ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

    private static List<ChartDataset> Create()
    {
        var start = new DateTime(2026, 11, 14);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var i = 0;
        foreach (var unit in new[] { "AHU 1", "AHU 2", "AHU 3" })
        {
            var offset = unit[^1] - '1';
            for (var h = 0; h < 7 * 24; h++, i++)
            {
                var t = start.AddHours(h);
                var cycle = Math.Sin((t.Hour - 9) / 24.0 * 2 * Math.PI);
                var outdoor = Math.Round(3 + 5 * cycle + SampleData.Noise(i, 21) * 2, 1);
                rows.Add(new Dictionary<string, object?>
                {
                    ["timestamp"] = t,
                    ["unit"] = unit,
                    ["supplyTemp"] = Math.Round(18.5 + offset * 0.6 + 1.4 * cycle + SampleData.Noise(i, 22) * 0.4, 1),
                    ["extractTemp"] = Math.Round(21.5 + offset * 0.3 + 0.8 * cycle + SampleData.Noise(i, 23) * 0.4, 1),
                    ["outdoorTemp"] = outdoor,
                    ["energy"] = Math.Round(Math.Max(0, 12 + offset * 3 - 1.6 * outdoor + SampleData.Noise(i, 24) * 2), 1),
                });
            }
        }

        var months = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        var systems = new (string Name, double[] Profile)[]
        {
            ("Heating", [42, 38, 29, 17, 8, 4, 3, 3, 9, 19, 30, 39]),
            ("Ventilation", [21, 20, 21, 20, 19, 18, 15, 16, 19, 20, 21, 21]),
            ("Cooling", [0, 0, 1, 3, 8, 15, 21, 19, 9, 2, 0, 0]),
            ("Lighting", [9, 8, 7, 6, 5, 4, 4, 4, 6, 7, 8, 9]),
        };
        var energy = months.SelectMany((month, m) => systems.Select(s =>
            (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["month"] = month, ["system"] = s.Name, ["mwh"] = s.Profile[m] }));

        return
        [
            ChartDataset.FromRows("ahu", rows, "Air handling units AHU 1-3, hourly for one week"),
            ChartDataset.FromRows("energy", energy, "Energy use per system and month, MWh"),
        ];
    }
}
