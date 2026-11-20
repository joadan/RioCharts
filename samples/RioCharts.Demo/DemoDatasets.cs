using RioCharts.Core.Specs;

namespace RioCharts.Demo;

/// <summary>The datasets that JSON specs (and AI agents through MCP) can chart in the demo.</summary>
public static class DemoDatasets
{
    public static IReadOnlyList<ChartDataset> All { get; } = Create();

    public static IReadOnlyDictionary<string, ChartDataset> ByName { get; } = All.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

    private static List<ChartDataset> Create()
    {
        var random = new Random(11);
        var start = new DateTime(2026, 11, 14);
        var ahuRows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var unit in new[] { "AHU 1", "AHU 2", "AHU 3" })
        {
            var offset = unit[^1] - '1';
            for (var h = 0; h < 7 * 24; h++)
            {
                var t = start.AddHours(h);
                var cycle = Math.Sin((t.Hour - 9) / 24.0 * 2 * Math.PI);
                var outdoor = Math.Round(3 + 5 * cycle + random.NextDouble(), 1);
                ahuRows.Add(new Dictionary<string, object?>
                {
                    ["timestamp"] = t,
                    ["unit"] = unit,
                    ["supplyTemp"] = Math.Round(18.5 + offset * 0.6 + 1.4 * cycle + random.NextDouble() * 0.3, 1),
                    ["extractTemp"] = Math.Round(21.5 + offset * 0.3 + 0.8 * cycle + random.NextDouble() * 0.3, 1),
                    ["outdoorTemp"] = outdoor,
                    ["energy"] = Math.Round(Math.Max(0, 12 + offset * 3 - 1.6 * outdoor + random.NextDouble() * 2), 1),
                    ["fanSpeed"] = Math.Round(t.Hour is >= 7 and < 18 ? 70 + random.NextDouble() * 20 : 30 + random.NextDouble() * 10, 0),
                });
            }
        }

        var ahu = ChartDataset.FromRows("ahu", ahuRows, "Air handling units AHU 1-3, hourly readings for one week (14-20 Nov 2026)",
            new Dictionary<string, string>
            {
                ["supplyTemp"] = "supply air °C",
                ["extractTemp"] = "extract air °C",
                ["outdoorTemp"] = "outdoor °C",
                ["energy"] = "heating energy kWh per hour",
                ["fanSpeed"] = "supply fan speed %",
            });

        var months = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        var systems = new (string Name, double[] Profile)[]
        {
            ("Heating", [42, 38, 29, 17, 8, 4, 3, 3, 9, 19, 30, 39]),
            ("Ventilation", [21, 20, 21, 20, 19, 18, 15, 16, 19, 20, 21, 21]),
            ("Cooling", [0, 0, 1, 3, 8, 15, 21, 19, 9, 2, 0, 0]),
            ("Lighting", [9, 8, 7, 6, 5, 4, 4, 4, 6, 7, 8, 9]),
        };
        var energyRows = months.SelectMany((month, m) => systems.Select(s => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["month"] = month,
            ["system"] = s.Name,
            ["mwh"] = s.Profile[m],
        })).ToList();
        var energy = ChartDataset.FromRows("energy", energyRows, "Building energy use per system and month, 2026",
            new Dictionary<string, string> { ["month"] = "Jan..Dec", ["system"] = "Heating, Ventilation, Cooling, Lighting", ["mwh"] = "energy in MWh" });

        return [ahu, energy];
    }
}
