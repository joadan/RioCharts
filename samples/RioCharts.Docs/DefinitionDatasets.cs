using RioCharts.Core.Definitions;

namespace RioCharts.Docs;

/// <summary>Datasets the JSON definition playground can chart.</summary>
public static class DefinitionDatasets
{
    public static IReadOnlyDictionary<string, ChartDataset> ByName { get; } = Create().ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

    private static List<ChartDataset> Create()
    {
        var start = new DateTime(2026, 11, 14);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var i = 0;
        foreach (var channel in new[] { "Web", "App", "Marketplace" })
        {
            var scale = channel switch { "Web" => 1.0, "App" => 0.65, _ => 0.35 };
            for (var h = 0; h < 7 * 24; h++, i++)
            {
                var t = start.AddHours(h);
                var cycle = Math.Sin((t.Hour - 14) / 24.0 * 2 * Math.PI);
                var sessions = Math.Round(Math.Max(10, scale * (560 + 440 * cycle + SampleData.Noise(i, 21) * 120)));
                var orders = Math.Round(sessions * (0.025 + SampleData.Noise(i, 22) * 0.006));
                rows.Add(new Dictionary<string, object?>
                {
                    ["timestamp"] = t,
                    ["channel"] = channel,
                    ["sessions"] = sessions,
                    ["orders"] = orders,
                    ["revenue"] = Math.Round(orders * (78 + SampleData.Noise(i, 23) * 20)),
                    ["responseMs"] = Math.Round(140 + sessions * 0.05 + SampleData.Noise(i, 24) * 30),
                });
            }
        }

        var months = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        var categories = new (string Name, double[] Profile)[]
        {
            ("Shoes", [42, 38, 29, 33, 36, 31, 28, 30, 34, 37, 48, 61]),
            ("Clothing", [18, 17, 21, 24, 26, 22, 20, 21, 25, 29, 38, 52]),
            ("Camping", [6, 7, 11, 19, 28, 35, 39, 33, 18, 9, 7, 12]),
            ("Accessories", [9, 8, 7, 8, 9, 10, 10, 9, 8, 9, 14, 24]),
        };
        var sales = months.SelectMany((month, m) => categories.Select(c =>
            (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["month"] = month, ["category"] = c.Name, ["revenue"] = c.Profile[m] }));

        return
        [
            ChartDataset.FromRows("shop", rows, "Online store traffic per sales channel (Web, App, Marketplace), hourly for one week"),
            ChartDataset.FromRows("sales", sales, "Revenue per product category and month, k€"),
        ];
    }
}
