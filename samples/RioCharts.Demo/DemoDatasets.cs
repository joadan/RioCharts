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
        var shopRows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var channel in new[] { "Web", "App", "Marketplace" })
        {
            var scale = channel switch { "Web" => 1.0, "App" => 0.65, _ => 0.35 };
            for (var h = 0; h < 7 * 24; h++)
            {
                var t = start.AddHours(h);
                var cycle = Math.Sin((t.Hour - 14) / 24.0 * 2 * Math.PI);
                var sessions = Math.Round(Math.Max(10, scale * (560 + 440 * cycle + random.NextDouble() * 120)));
                var orders = Math.Round(sessions * (0.025 + random.NextDouble() * 0.006));
                shopRows.Add(new Dictionary<string, object?>
                {
                    ["timestamp"] = t,
                    ["channel"] = channel,
                    ["sessions"] = sessions,
                    ["orders"] = orders,
                    ["revenue"] = Math.Round(orders * (78 + random.NextDouble() * 20)),
                    ["responseMs"] = Math.Round(140 + sessions * 0.05 + random.NextDouble() * 30),
                });
            }
        }

        var shop = ChartDataset.FromRows("shop", shopRows,
            "Online store traffic per sales channel (Web, App, Marketplace), hourly for one week (14-20 Nov 2026)",
            new Dictionary<string, string>
            {
                ["channel"] = "Web, App, Marketplace",
                ["sessions"] = "visitor sessions per hour",
                ["orders"] = "orders per hour",
                ["revenue"] = "revenue in € per hour",
                ["responseMs"] = "average server response time in ms",
            });

        var months = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        var categories = new (string Name, double[] Profile)[]
        {
            ("Shoes", [42, 38, 29, 33, 36, 31, 28, 30, 34, 37, 48, 61]),
            ("Clothing", [18, 17, 21, 24, 26, 22, 20, 21, 25, 29, 38, 52]),
            ("Camping", [6, 7, 11, 19, 28, 35, 39, 33, 18, 9, 7, 12]),
            ("Accessories", [9, 8, 7, 8, 9, 10, 10, 9, 8, 9, 14, 24]),
        };
        var salesRows = months.SelectMany((month, m) => categories.Select(c => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["month"] = month,
            ["category"] = c.Name,
            ["revenue"] = c.Profile[m],
        })).ToList();
        var sales = ChartDataset.FromRows("sales", salesRows, "Revenue per product category and month, 2026",
            new Dictionary<string, string> { ["month"] = "Jan..Dec", ["category"] = "Shoes, Clothing, Camping, Accessories", ["revenue"] = "revenue in k€" });

        return [shop, sales];
    }
}
