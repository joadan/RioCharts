using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Skia;

namespace RioCharts.Tests;

/// <summary>
/// Visual snapshots: reference charts laid out with the deterministic approximate text measurer and
/// written as SVG, compared with <c>Snapshots/*.svg</c>. A change writes <c>name.received.svg</c>
/// next to the snapshot and fails; open both to compare. Accept intended changes with
/// <c>RIOCHARTS_ACCEPT_SNAPSHOTS=1 dotnet test</c> (or copy the received file over), then review the
/// diff before committing. A new chart writes its snapshot and fails once, so it gets looked at.
/// </summary>
public class SnapshotTests
{
    private static readonly DateTime Start = new(2026, 11, 16);

    /// <summary>
    /// The Snapshots folder next to the test project, found from the test assembly (bin/…): CI builds
    /// map source paths to "/_/", so [CallerFilePath] does not point at the checkout there.
    /// </summary>
    private static string SnapshotDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RioCharts.Tests.csproj")))
            {
                return Path.Combine(dir.FullName, "Snapshots");
            }
        }

        throw new InvalidOperationException($"RioCharts.Tests.csproj not found above {AppContext.BaseDirectory}.");
    }

    private static SeriesData Hourly(int hours, Func<int, double> value, int step = 1) =>
        SeriesData.From(Enumerable.Range(0, hours / step).Select(i => Start.AddHours(i * step)).ToArray(),
            Enumerable.Range(0, hours / step).Select(i => value(i * step)).ToArray());

    private static double London(int i) => Math.Round(19 + 1.5 * Math.Sin(i / 24.0 * 2 * Math.PI) + 0.4 * Math.Sin(i * 1.7), 2);

    private static double Toronto(int i) => Math.Round(22 + 0.8 * Math.Sin((i - 3) / 24.0 * 2 * Math.PI) + 0.2 * Math.Cos(i * 1.3), 2);

    private static SeriesData Categories(string[] names, params double[] values) => SeriesData.FromCategories(names, values);

    private static readonly string[] Quarters = ["Q1", "Q2", "Q3", "Q4"];

    // Periods on 2 March 2026, from and to in hours.
    private static SeriesData Ranges(params (string Row, double From, double To)[] periods) =>
        SeriesData.FromRanges(periods, p => p.Row, p => new DateTime(2026, 3, 2).AddHours(p.From), p => new DateTime(2026, 3, 2).AddHours(p.To));

    private sealed record Case(ChartDefinition Spec, Dictionary<string, SeriesData> Data, double Width = 640, double Height = 320, string Culture = "", (double, double)? XRange = null);

    private static readonly Dictionary<string, Case> Cases = new()
    {
        ["line-time"] = new(new ChartDefinition
        {
            Title = "Weather stations",
            X = new AxisOptions { Title = "Time" },
            Y = new AxisOptions { Title = "°C", Format = "0.0" },
            Series = [new SeriesOptions { Id = "s", Name = "London", Curve = CurveType.Monotone }, new SeriesOptions { Id = "e", Name = "Toronto" }],
        }, new() { ["s"] = Hourly(48, London), ["e"] = Hourly(48, Toronto) }),

        ["line-gaps-step"] = new(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "t", Name = "Outdoor" }, new SeriesOptions { Id = "sp", Name = "Forecast", Curve = CurveType.Step }],
        }, new()
        {
            ["t"] = Hourly(36, i => i is >= 12 and < 18 ? double.NaN : 21 + Math.Sin(i / 5.0)),
            ["sp"] = Hourly(36, i => i < 8 ? 18 : i < 20 ? 21 : 19, step: 4),
        }),

        ["line-categories"] = new(new ChartDefinition { Series = [new SeriesOptions { Id = "a", Name = "Alarms" }] },
            new() { ["a"] = Categories(["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"], 4, 7, 3, 9, 6, 1, 2) }),

        ["area-stacked"] = new(new ChartDefinition
        {
            Y = new AxisOptions { Title = "kW" },
            Series =
            [
                new SeriesOptions { Id = "h", Name = "Wind", Type = SeriesType.Area, Stack = "load" },
                new SeriesOptions { Id = "v", Name = "Solar", Type = SeriesType.Area, Stack = "load" },
            ],
        }, new()
        {
            ["h"] = Hourly(48, i => Math.Max(0, 14 - 9 * Math.Sin(i / 24.0 * 2 * Math.PI)), step: 2),
            ["v"] = Hourly(48, i => i % 24 is >= 7 and < 18 ? 6 : 2, step: 2),
        }),

        ["bars-grouped"] = new(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "a", Name = "2025", Type = SeriesType.Bar }, new SeriesOptions { Id = "b", Name = "2026", Type = SeriesType.Bar }],
        }, new() { ["a"] = Categories(Quarters, 120, 95, 60, 110), ["b"] = Categories(Quarters, 105, 90, 52, 98) }),

        ["bars-stacked-negative"] = new(new ChartDefinition
        {
            Series =
            [
                new SeriesOptions { Id = "in", Name = "Bought", Type = SeriesType.Bar, Stack = "e" },
                new SeriesOptions { Id = "out", Name = "Sold", Type = SeriesType.Bar, Stack = "e" },
            ],
        }, new() { ["in"] = Categories(Quarters, 40, 25, 10, 35), ["out"] = Categories(Quarters, -5, -18, -30, -8) }),

        ["bars-stacked-percent"] = new(new ChartDefinition
        {
            Title = "Sales channels",
            Y = new AxisOptions { Stacking = StackMode.Percent },
            Series =
            [
                new SeriesOptions { Id = "w", Name = "Web", Type = SeriesType.Bar },
                new SeriesOptions { Id = "s", Name = "Stores", Type = SeriesType.Bar },
                new SeriesOptions { Id = "p", Name = "Partners", Type = SeriesType.Bar },
            ],
        }, new() { ["w"] = Categories(Quarters, 120, 150, 170, 210), ["s"] = Categories(Quarters, 200, 180, 160, 150), ["p"] = Categories(Quarters, 40, 60, 55, 80) }),

        ["bars-data-labels"] = new(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "u", Name = "Energy", Type = SeriesType.Bar, DataLabels = true, Format = "0 MWh" }],
        }, new() { ["u"] = Categories(["Store 1", "Store 2", "Store 3", "Store 4"], 412, 236, 148, 97) }, Height: 260),

        ["pie-other"] = new(new ChartDefinition
        {
            Title = "Energy use",
            Series = [new SeriesOptions { Id = "p", Type = SeriesType.Pie, GroupSmallSlices = 0.03, Format = "0 MWh" }],
        }, new() { ["p"] = Categories(["Industry", "Homes", "Transport", "Offices", "Shops", "Farms", "Rail"], 412, 236, 148, 97, 31, 9, 6) }),

        ["donut-total"] = new(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "p", Type = SeriesType.Pie, InnerRadius = 0.6, DataLabels = false, Format = "0 MWh" }],
        }, new() { ["p"] = Categories(["Industry", "Homes", "Transport"], 412, 236, 148) }, Width: 400, Height: 300),

        ["radial-bar"] = new(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "h", Name = "Target", Type = SeriesType.RadialBar, Max = 100, Format = "0'%'" }],
        }, new() { ["h"] = Categories(["North", "South", "East", "West"], 87, 74, 41, 92) }, Width: 420, Height: 300),

        // A server's CPU load: one gauge with a scale in three bands and a target.
        ["gauge"] = new(new ChartDefinition
        {
            Title = "CPU load",
            Series =
            [
                new SeriesOptions
                {
                    Id = "cpu", Name = "Web server", Type = SeriesType.Gauge, Max = 100, Target = 70, Format = "0'%'",
                    ColorBands = [new ColorBand { Above = 60, Color = "--rio-series-2" }, new ColorBand { Above = 85, Color = "--rio-negative" }],
                },
            ],
        }, new() { ["cpu"] = Categories([""], 64) }, Width: 360, Height: 280),

        // Share of trips made by bike per city, 2016 against 2026 (rows: the classic dumbbell).
        ["dumbbell"] = new(new ChartDefinition
        {
            Title = "Trips by bike",
            Horizontal = true,
            Series = [new SeriesOptions { Id = "d", Name = "2016 → 2026", Type = SeriesType.Dumbbell, Format = "0'%'", DataLabels = true }],
        }, new()
        {
            ["d"] = Categories(["Utrecht", "Malmö", "Freiburg", "Ghent", "Bologna", "Lyon"], 41, 33, 29, 35, 18, 12).WithLow([33.0, 30, 27, 22, 9, 6]),
        }, Width: 520, Height: 300),

        // Planned against actual hours per month, upright.
        ["dumbbell-vertical"] = new(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "h", Name = "Plan → actual", Type = SeriesType.Dumbbell, Format = "0 h" }],
        }, new()
        {
            ["h"] = Categories(["Jan", "Feb", "Mar", "Apr", "May", "Jun"], 152, 148, 171, 160, 139, 166).WithLow([160.0, 150, 160, 160, 150, 150]),
        }, Width: 480, Height: 260),

        // Sessions per country, and per city as bubbles that add up per country on the world map.
        ["map"] = new(new ChartDefinition
        {
            Title = "Active sessions",
            Series =
            [
                new SeriesOptions { Id = "m", Name = "Sessions", Type = SeriesType.Map },
                new SeriesOptions { Id = "c", Name = "Cities", Type = SeriesType.MapBubble },
            ],
        }, new()
        {
            ["m"] = Categories(["SE", "NO", "FI", "DE", "GB", "US", "BR", "CN", "AU"], 1240, 310, 260, 420, 190, 210, 12, 75, 25),
            ["c"] = Categories(["Stockholm", "Gothenburg", "New York", "Shanghai"], 420, 380, 120, 60)
                .WithLocations([59.33, 57.71, 40.71, 31.23], [18.07, 11.97, -74.01, 121.47]),
        }, Width: 640, Height: 340),

        // The same countries in the Mercator projection of web maps, with each city as its own bubble.
        ["map-mercator"] = new(new ChartDefinition
        {
            Projection = MapProjection.Mercator,
            Series =
            [
                new SeriesOptions { Id = "m", Name = "Sessions", Type = SeriesType.Map },
                new SeriesOptions { Id = "c", Name = "Cities", Type = SeriesType.MapBubble, GroupByCountry = false },
            ],
        }, new()
        {
            ["m"] = Categories(["SE", "NO", "FI", "DE", "GB", "US", "BR", "CN", "AU"], 1240, 310, 260, 420, 190, 210, 12, 75, 25),
            ["c"] = Categories(["Stockholm", "Gothenburg", "New York", "Shanghai"], 420, 380, 120, 60)
                .WithLocations([59.33, 57.71, 40.71, 31.23], [18.07, 11.97, -74.01, 121.47]),
        }, Width: 640, Height: 400),

        // Average test scores per class in two years, with the standard deviation as error bars.
        ["error-bars"] = new(new ChartDefinition
        {
            Y = new AxisOptions { Title = "Score" },
            Series =
            [
                new SeriesOptions { Id = "a", Name = "2025", Type = SeriesType.Bar },
                new SeriesOptions { Id = "b", Name = "2026", Type = SeriesType.Bar },
            ],
        }, new()
        {
            ["a"] = Categories(["Class A", "Class B", "Class C", "Class D"], 72, 65, 80, 58).WithErrors([64.0, 59, 75, 47], [80.0, 71, 85, 69]),
            ["b"] = Categories(["Class A", "Class B", "Class C", "Class D"], 76, 70, 78, 66).WithErrors([70.0, 61, 74, 58], [82.0, 79, 82, 74]),
        }, Width: 520, Height: 300),

        // A median response time per day with its 90 % interval (uneven: the high end is further away).
        ["error-bars-line"] = new(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "m", Name = "Median", Format = "0 ms" }],
        }, new()
        {
            ["m"] = SeriesData.From(Enumerable.Range(0, 10).Select(i => Start.AddDays(i)).ToArray(), [120.0, 132, 118, 140, 151, 129, 122, 135, 160, 141])
                .WithErrors([104.0, 115, 101, 118, 130, 110, 106, 117, 133, 120], [158.0, 171, 149, 188, 206, 166, 159, 180, 221, 190]),
        }, Width: 520, Height: 260),

        // A shop's month: three measures, each on its own scale, with poor/fair/good ranges and a target.
        ["bullet"] = new(new ChartDefinition
        {
            Title = "March",
            Series =
            [
                new SeriesOptions
                {
                    Id = "revenue", Name = "Revenue", Type = SeriesType.Bullet, Max = 300, Target = 250, Format = "0 k€",
                    ColorBands = [new ColorBand { Below = 150, Color = "--rio-negative" }, new ColorBand { Above = 225, Color = "--rio-positive" }],
                },
                new SeriesOptions { Id = "margin", Name = "Margin", Type = SeriesType.Bullet, Max = 40, Target = 26, Format = "0'%'" },
                new SeriesOptions { Id = "rating", Name = "Rating", Type = SeriesType.Bullet, Min = 1, Max = 5, Target = 4.2, Format = "0.0" },
            ],
        }, new() { ["revenue"] = Categories([""], 268), ["margin"] = Categories([""], 22), ["rating"] = Categories([""], 4.4) }, Width: 520, Height: 260),

        // Sales per region against each region's own target.
        ["bullets-targets"] = new(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "s", Name = "Sales", Type = SeriesType.Bullet, Format = "0 k€", DataLabels = true }],
        }, new() { ["s"] = Categories(["North", "South", "East", "West"], 182, 96, 141, 120).WithTargets([160.0, 130, 140, 100]) }, Width: 520, Height: 220),

        // A shop funnel: stages narrowing down, each with its share of the stage before.
        ["funnel"] = new(new ChartDefinition
        {
            Title = "Shop funnel",
            Series = [new SeriesOptions { Id = "f", Name = "Users", Type = SeriesType.Funnel, Format = "#,0" }],
        }, new() { ["f"] = Categories(["Visits", "Product views", "Cart", "Checkout", "Purchase"], 15000, 10000, 7000, 4000, 2000) }, Width: 560, Height: 300),

        // Orders per day for a year: quieter weekends, a busier autumn, a few days without data.
        ["calendar"] = new(new ChartDefinition
        {
            Title = "Orders per day",
            Series = [new SeriesOptions { Id = "c", Name = "Orders", Type = SeriesType.Calendar }],
        }, new()
        {
            ["c"] = SeriesData.From(
                [.. Enumerable.Range(0, 365).Where(i => i % 23 != 5).Select(i => new DateTime(2026, 1, 1).AddDays(i))],
                [.. Enumerable.Range(0, 365).Where(i => i % 23 != 5).Select(i => Math.Round(
                    (new DateTime(2026, 1, 1).AddDays(i).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 6 : 16) + 6 * Math.Sin((i - 150) / 365.0 * 2 * Math.PI) + i * 7 % 5))]),
        }, Width: 760, Height: 200),

        // Disk use per drive: half-circle gauges in a row.
        ["gauges-half"] = new(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "d", Type = SeriesType.Gauge, Max = 100, StartAngle = -90, EndAngle = 90, Format = "0'%'" }],
        }, new() { ["d"] = Categories(["System", "Data", "Backup"], 42, 77, 93) }, Width: 600, Height: 200),

        ["dual-axes"] = new(new ChartDefinition
        {
            Y = new AxisOptions { Title = "°C" },
            YAxes = [new AxisOptions { Id = "kw", Title = "kW", Position = AxisPosition.Right }],
            Series =
            [
                new SeriesOptions { Id = "t", Name = "Outdoor" },
                new SeriesOptions { Id = "p", Name = "Power use", Type = SeriesType.Bar, YAxisId = "kw" },
            ],
        }, new()
        {
            ["t"] = Hourly(24, i => 4 + 5 * Math.Sin((i - 9) / 24.0 * 2 * Math.PI), step: 2),
            ["p"] = Hourly(24, i => Math.Max(0, 30 - 3 * (4 + 5 * Math.Sin((i - 9) / 24.0 * 2 * Math.PI))), step: 2),
        }),

        // One axis per series, two on each side: outer columns are as wide as their titles, so no title overlaps; each axis in its series' color.
        ["many-axes"] = new(new ChartDefinition
        {
            Y = new AxisOptions { Title = "Temperature °C", SeriesColor = true },
            YAxes =
            [
                new AxisOptions { Id = "kpa", Title = "Pressure kPa", Position = AxisPosition.Left, SeriesColor = true },
                new AxisOptions { Id = "kw", Title = "Power kW", SeriesColor = true },
                new AxisOptions { Id = "rh", Title = "Humidity %", SeriesColor = true },
            ],
            Series =
            [
                new SeriesOptions { Id = "t", Name = "Temperature" },
                new SeriesOptions { Id = "p", Name = "Pressure", YAxisId = "kpa" },
                new SeriesOptions { Id = "w", Name = "Power", YAxisId = "kw" },
                new SeriesOptions { Id = "h", Name = "Humidity", YAxisId = "rh" },
            ],
        }, new()
        {
            ["t"] = Hourly(24, London, step: 2),
            ["p"] = Hourly(24, i => 101.3 + 0.4 * Math.Sin(i / 6.0), step: 2),
            ["w"] = Hourly(24, i => 1200 + 300 * Math.Cos(i / 5.0), step: 2),
            ["h"] = Hourly(24, i => 55 + 10 * Math.Sin(i / 4.0), step: 2),
        }, Width: 720),

        // More categories than fit side by side: the labels slope at 45 degrees and all show.
        ["bar-rotated-labels"] = new(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "s", Name = "Issues", Type = SeriesType.Bar }],
        }, new()
        {
            ["s"] = Categories(["Sweden", "Norway", "Denmark", "Finland", "Germany", "Netherlands", "Belgium", "France", "Spain", "Portugal",
                "Italy", "Switzerland", "Austria", "Poland", "Czech Republic", "United Kingdom", "Ireland", "Estonia"],
                42, 31, 28, 25, 22, 19, 17, 15, 14, 12, 11, 9, 8, 7, 6, 5, 4, 3),
        }),

        // A filter left one category: the bar takes a fifth of the plot, centered, instead of all of it.
        ["bar-single"] = new(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "s", Name = "Issues", Type = SeriesType.Bar }],
        }, new() { ["s"] = Categories(["Sweden"], 42) }),

        // Six readings: a dot on each shows where the values are.
        ["line-dots"] = new(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "s", Name = "London", Highlight = PointHighlight.All }],
        }, new() { ["s"] = Hourly(24, London, step: 4) }),

        ["annotations"] = new(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "s", Name = "London" }],
            Annotations =
            [
                new Annotation { Type = AnnotationType.Band, From = 18.5, To = 20.5, Label = "Normal" },
                new Annotation { Type = AnnotationType.Line, Value = 21, Label = "High limit" },
                new Annotation { Type = AnnotationType.Band, Axis = AnnotationAxis.X, From = Start.AddHours(10), To = Start.AddHours(14), Label = "Service" },
                new Annotation { Type = AnnotationType.Callout, X = Start.AddHours(30), Y = London(30), Label = "Sensor fault" },
            ],
        }, new() { ["s"] = Hourly(48, London) }),

        ["time-zone-dst"] = new(new ChartDefinition
        {
            TimeZone = "Europe/Stockholm",
            Series = [new SeriesOptions { Id = "t", Name = "Outdoor" }],
        }, new()
        {
            ["t"] = SeriesData.From(Enumerable.Range(0, 36).Select(i => new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero).AddHours(i)).ToArray(),
                Enumerable.Range(0, 36).Select(i => 8 + 3 * Math.Sin(i / 6.0)).ToArray()),
        }),

        ["zoomed"] = new(new ChartDefinition { Series = [new SeriesOptions { Id = "s", Name = "London" }] },
            new() { ["s"] = Hourly(96, London) },
            XRange: (TimeValue.FromDateTime(Start.AddHours(30)), TimeValue.FromDateTime(Start.AddHours(42)))),

        ["compact-sparkline"] = new(new ChartDefinition
        {
            Compact = true,
            Series = [new SeriesOptions { Id = "s", Name = "London", Curve = CurveType.Monotone, Highlight = PointHighlight.Last | PointHighlight.Min | PointHighlight.Max }],
            Annotations = [new Annotation { Type = AnnotationType.Band, From = 18.5, To = 20.5 }],
        }, new() { ["s"] = Hourly(48, London) }, Width: 160, Height: 32),

        ["auto-compact-small"] = new(new ChartDefinition
        {
            Title = "Weather stations",
            X = new AxisOptions { Title = "Time" },
            Y = new AxisOptions { Title = "°C" },
            Series = [new SeriesOptions { Id = "s", Name = "London" }, new SeriesOptions { Id = "e", Name = "Toronto" }],
        }, new() { ["s"] = Hourly(48, London), ["e"] = Hourly(48, Toronto) }, Width: 360, Height: 130),

        ["scatter-trend"] = new(new ChartDefinition
        {
            X = new AxisOptions { Title = "Outdoor °C" },
            Y = new AxisOptions { Title = "Hot drinks sold, k" },
            Series =
            [
                new SeriesOptions { Id = "s", Name = "Café A", Type = SeriesType.Scatter, TrendLine = true },
                new SeriesOptions { Id = "t", Name = "Café B", Type = SeriesType.Scatter, MarkerSize = 4 },
            ],
        }, new()
        {
            ["s"] = SeriesData.From(Enumerable.Range(0, 40).Select(i => -10 + i * 0.8 + Math.Sin(i * 2.3) * 2).ToArray(),
                Enumerable.Range(0, 40).Select(i => 24 - 0.22 * (-10 + i * 0.8) + Math.Cos(i * 1.7) * 0.8).ToArray()),
            ["t"] = SeriesData.From(Enumerable.Range(0, 30).Select(i => -8 + i * 0.9 + Math.Cos(i * 1.9) * 2).ToArray(),
                Enumerable.Range(0, 30).Select(i => 21 - 0.1 * (-8 + i * 0.9) + Math.Sin(i * 2.1) * 0.6).ToArray()),
        }),

        ["heatmap-week"] = new(new ChartDefinition
        {
            Title = "Sessions per hour",
            Series = [.. new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" }.Select(d => new SeriesOptions { Id = d, Type = SeriesType.Heatmap, Format = "#,0" })],
        }, new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" }.Select((d, k) => (d, SeriesData.FromCategories(
                Enumerable.Range(0, 24).Select(h => h.ToString("00", CultureInfo.InvariantCulture)).ToList(),
                Enumerable.Range(0, 24).Select(h => Math.Round(Math.Max(5, (k >= 5 ? 260 : 200) * Math.Max(0, Math.Sin((h - 6) / 18.0 * Math.PI)) + (h is >= 19 and <= 22 ? 90 : 0)))).ToArray())))
            .ToDictionary(t => t.d, t => t.Item2), Width: 720),

        // Sessions against the same hour last week, in %: down in the mornings, up in the evenings (diverging from 0).
        ["heatmap-diverging"] = new(new ChartDefinition
        {
            Title = "Sessions against last week",
            Series = [.. new[] { "Mon", "Tue", "Wed", "Thu", "Fri" }.Select(d => new SeriesOptions { Id = d, Type = SeriesType.Heatmap, Format = "+0'%';-0'%';0'%'", Midpoint = 0 })],
        }, new[] { "Mon", "Tue", "Wed", "Thu", "Fri" }.Select((d, k) => (d, SeriesData.FromCategories(
                Enumerable.Range(8, 14).Select(h => h.ToString("00", CultureInfo.InvariantCulture)).ToList(),
                Enumerable.Range(8, 14).Select(h => Math.Round(30 * Math.Sin((h - 13) / 5.0) + 6 * Math.Sin(k * 1.9 + h))).ToArray())))
            .ToDictionary(t => t.d, t => t.Item2), Width: 720),

        ["range-band"] = new(new ChartDefinition
        {
            Y = new AxisOptions { Title = "Revenue per day" },
            Series =
            [
                new SeriesOptions { Id = "r", Name = "Range", Type = SeriesType.RangeArea, Curve = CurveType.Monotone },
                new SeriesOptions { Id = "m", Name = "Average", Curve = CurveType.Monotone },
            ],
        }, new()
        {
            ["r"] = SeriesData.From(Enumerable.Range(0, 30).Select(d => Start.AddDays(d)).ToArray(),
                    Enumerable.Range(0, 30).Select(d => Math.Round(9000 + 2500 * Math.Sin(d / 7.0 * Math.PI) + 1800 + d * 37 % 11 * 120)).ToArray())
                .WithLow(Enumerable.Range(0, 30).Select(d => Math.Round(9000 + 2500 * Math.Sin(d / 7.0 * Math.PI) - 1500 - d * 53 % 13 * 100)).ToArray()),
            ["m"] = SeriesData.From(Enumerable.Range(0, 30).Select(d => Start.AddDays(d)).ToArray(),
                Enumerable.Range(0, 30).Select(d => Math.Round(9000 + 2500 * Math.Sin(d / 7.0 * Math.PI))).ToArray()),
        }),

        // Outdoor temperatures in °C per weather station: a tight one, a wide one, one with outliers, and a
        // second series (last week) beside each box.
        ["box-plot"] = new(new ChartDefinition
        {
            Y = new AxisOptions { Title = "Temperature °C", Format = "0.0" },
            Series =
            [
                new SeriesOptions { Id = "now", Name = "This week", Type = SeriesType.BoxPlot },
                new SeriesOptions { Id = "before", Name = "Last week", Type = SeriesType.BoxPlot },
            ],
        }, new()
        {
            ["now"] = SeriesData.FromCategories(["Station 1", "Station 2", "Station 3"], [0.0, 0, 0]).WithBox(
            [
                BoxSummary.FromValues(Enumerable.Range(0, 40).Select(i => 19.5 + Math.Sin(i * 1.7) * 0.6)),
                BoxSummary.FromValues(Enumerable.Range(0, 40).Select(i => 20.0 + Math.Sin(i * 2.3) * 2.2)),
                BoxSummary.FromValues([.. Enumerable.Range(0, 40).Select(i => 18.8 + Math.Sin(i * 1.3) * 0.8), 24.6, 15.2]),
            ]),
            ["before"] = SeriesData.FromCategories(["Station 1", "Station 2", "Station 3"], [0.0, 0, 0]).WithBox(
            [
                BoxSummary.FromValues(Enumerable.Range(0, 40).Select(i => 19.8 + Math.Sin(i * 1.1) * 0.9)),
                BoxSummary.FromValues(Enumerable.Range(0, 40).Select(i => 20.6 + Math.Sin(i * 2.9) * 1.4)),
                BoxSummary.FromValues(Enumerable.Range(0, 40).Select(i => 19.1 + Math.Sin(i * 0.7) * 1.0)),
            ]),
        }),

        // Population in millions per continent and country (rounded), the small ones grouped per continent.
        ["treemap"] = new(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "p", Name = "Population", Type = SeriesType.Treemap, Format = "0 M", GroupSmallSlices = 0.05 }],
        }, new()
        {
            ["p"] = SeriesData.FromCategories(
                ["Asia/India", "Asia/China", "Asia/Indonesia", "Asia/Pakistan", "Asia/Japan", "Asia/Nepal", "Asia/Laos",
                 "Africa/Nigeria", "Africa/Ethiopia", "Africa/Egypt", "Africa/Kenya",
                 "Europe/Germany", "Europe/France", "Europe/Italy", "Europe/Spain", "Europe/Sweden", "Europe/Norway",
                 "Americas/USA", "Americas/Brazil", "Americas/Mexico", "Americas/Canada"],
                [1430, 1410, 278, 240, 124, 31, 8, 224, 127, 113, 55, 84, 68, 59, 48, 10.5, 5.5, 335, 216, 129, 40]),
        }),

        // Two laptops' test scores (0-100) against the class average; the old one's display was not tested.
        ["radar"] = new(new ChartDefinition
        {
            Y = new AxisOptions { Max = 100 },
            Series =
            [
                new SeriesOptions { Id = "unit", Name = "New laptop", Type = SeriesType.Radar },
                new SeriesOptions { Id = "ref", Name = "Class average", Type = SeriesType.Radar },
                new SeriesOptions { Id = "old", Name = "Old laptop", Type = SeriesType.Radar },
            ],
        }, new()
        {
            ["unit"] = SeriesData.FromCategories(["Performance", "Battery", "Display", "Keyboard", "Weight", "Price"], [82.0, 64, 71, 90, 55, 77]),
            ["ref"] = SeriesData.FromCategories(["Performance", "Battery", "Display", "Keyboard", "Weight", "Price"], [75.0, 75, 75, 75, 75, 75]),
            ["old"] = SeriesData.FromCategories(["Performance", "Battery", "Display", "Keyboard", "Weight", "Price"], [60.0, 48, double.NaN, 70, 40, 52]),
        }),

        // Outdoor temperature in °C as daily candles (first, highest, lowest, last reading), with
        // the daily average as a line on top.
        ["candles"] = new(new ChartDefinition
        {
            Y = new AxisOptions { Title = "Temperature °C", Format = "0.0" },
            Series =
            [
                new SeriesOptions { Id = "c", Name = "Daily range", Type = SeriesType.Candlestick },
                new SeriesOptions { Id = "m", Name = "Average", Curve = CurveType.Monotone },
            ],
        }, new()
        {
            ["c"] = SeriesData.From(Enumerable.Range(0, 21).Select(d => Start.AddDays(d)).ToArray(), new double[21]).WithCandles(
                Enumerable.Range(0, 21).Select(d =>
                {
                    var open = Math.Round(20 + Math.Sin(d * 0.6) * 1.6, 1);
                    var close = Math.Round(open + Math.Sin(d * 1.9) * 0.9, 1);
                    return new CandleSummary(open, Math.Max(open, close) + 0.4 + d % 3 * 0.2, Math.Min(open, close) - 0.3 - d % 4 * 0.15, close);
                }).ToArray()),
            ["m"] = SeriesData.From(Enumerable.Range(0, 21).Select(d => Start.AddDays(d)).ToArray(),
                Enumerable.Range(0, 21).Select(d => Math.Round(20 + Math.Sin(d * 0.6) * 1.6 + Math.Sin(d * 1.9) * 0.45, 1)).ToArray()),
        }),

        // A month's result in k€: from revenue through costs to the result, with a subtotal.
        ["waterfall"] = new(new ChartDefinition
        {
            Title = "Result, March",
            Y = new AxisOptions { Title = "k€" },
            Series = [new SeriesOptions { Id = "w", Name = "Result", Type = SeriesType.Waterfall, DataLabels = true }],
        }, new()
        {
            ["w"] = SeriesData.FromCategories(["Revenue", "Cost of sales", "Gross profit", "Salaries", "Marketing", "Other income", "Result"],
                [420.0, -180, double.NaN, -120, -45, 15, double.NaN]).WithTotals([false, false, true, false, false, false, true]),
        }),

        // Response times in ms before and after a release, 400 requests each, in bins of 10 ms (overlaid).
        ["histogram"] = new(new ChartDefinition
        {
            Title = "Response time",
            X = new AxisOptions { Title = "ms" },
            Y = new AxisOptions { Title = "Requests" },
            Series =
            [
                new SeriesOptions { Id = "before", Name = "Before", Type = SeriesType.Histogram, BinWidth = 10 },
                new SeriesOptions { Id = "after", Name = "After", Type = SeriesType.Histogram, BinWidth = 10 },
            ],
        }, new()
        {
            ["before"] = SeriesData.FromBins(Enumerable.Range(0, 400).Select(i => 150 + 28 * (Math.Sin(i * 1.7) + Math.Sin(i * 0.37) + Math.Sin(i * 2.9)) / 3 * 1.6),
                v => v, 10, null, out _, out _),
            ["after"] = SeriesData.FromBins(Enumerable.Range(0, 400).Select(i => 120 + 20 * (Math.Sin(i * 1.3) + Math.Sin(i * 0.53) + Math.Sin(i * 3.1)) / 3 * 1.6),
                v => v, 10, null, out _, out _),
        }),

        // A packing line's machines through a shift: running, idle and stopped periods (a state timeline), with "now".
        ["range-bar"] = new(new ChartDefinition
        {
            Title = "Packing line",
            Series =
            [
                new SeriesOptions { Id = "running", Name = "Running", Type = SeriesType.RangeBar, Color = "--rio-positive" },
                new SeriesOptions { Id = "idle", Name = "Idle", Type = SeriesType.RangeBar, Color = "--rio-text-muted" },
                new SeriesOptions { Id = "stopped", Name = "Stopped", Type = SeriesType.RangeBar, Color = "--rio-negative" },
            ],
            Annotations = [new Annotation { Axis = AnnotationAxis.X, Value = new DateTime(2026, 3, 2, 13, 0, 0), Label = "Now" }],
        }, new()
        {
            ["running"] = Ranges(("Filler", 6, 9.5), ("Filler", 10, 13), ("Capper", 6.5, 8), ("Capper", 8.25, 12), ("Labeler", 7, 11), ("Labeler", 11.75, 13),
                ("Palletizer", 6, 13)),
            ["idle"] = Ranges(("Capper", 8, 8.25), ("Capper", 12, 13), ("Labeler", 6, 7)),
            ["stopped"] = Ranges(("Filler", 9.5, 10), ("Labeler", 11, 11.75)),
        }),

        // Stores: floor area against revenue per m², sized by visitors per day, in two cities (bubbles).
        ["bubble"] = new(new ChartDefinition
        {
            Title = "Stores",
            X = new AxisOptions { Title = "Floor area, m²" },
            Y = new AxisOptions { Title = "Revenue per m², €" },
            Series =
            [
                new SeriesOptions { Id = "lund", Name = "Lund", Type = SeriesType.Scatter, SizeName = "Visitors", Format = "0 €" },
                new SeriesOptions { Id = "malmo", Name = "Malmö", Type = SeriesType.Scatter, SizeName = "Visitors", Format = "0 €" },
            ],
        }, new()
        {
            ["lund"] = SeriesData.From([420.0, 680, 950, 1300], [310.0, 285, 350, 260]).WithSizes([180.0, 520, 940, 1350]),
            ["malmo"] = SeriesData.From([500.0, 820, 1150, 1600, 2100], [270.0, 330, 295, 240, 315]).WithSizes([300.0, 760, 1100, 1900, 2600]),
        }),

        ["bars-horizontal"] = new(new ChartDefinition
        {
            Horizontal = true,
            Y = new AxisOptions { Title = "Revenue, k€" },
            Series =
            [
                new SeriesOptions { Id = "a", Name = "2025", Type = SeriesType.Bar },
                new SeriesOptions { Id = "b", Name = "2026", Type = SeriesType.Bar, DataLabels = true },
            ],
            Annotations = [new Annotation { Value = 100, Label = "Target" }],
        }, new()
        {
            ["a"] = Categories(["Trail shoe · Ridge 2", "Rain jacket · Storm", "Headlamp · Beam 400", "Merino base layer, long sleeve", "Tent · Dome 2p"], 112, 84, 55, 61, 41),
            ["b"] = Categories(["Trail shoe · Ridge 2", "Rain jacket · Storm", "Headlamp · Beam 400", "Merino base layer, long sleeve", "Tent · Dome 2p"], 128, 92, 71, 58, 22),
        }, Height: 340),

        // Outdoor temperatures in °C per weather station, lying down: this week and last week in each row.
        ["box-plot-horizontal"] = new(new ChartDefinition
        {
            Horizontal = true,
            Y = new AxisOptions { Title = "Temperature °C", Format = "0.0" },
            Series =
            [
                new SeriesOptions { Id = "now", Name = "This week", Type = SeriesType.BoxPlot },
                new SeriesOptions { Id = "before", Name = "Last week", Type = SeriesType.BoxPlot },
            ],
        }, new()
        {
            ["now"] = SeriesData.FromCategories(["Station 1", "Station 2", "Station 3"], [0.0, 0, 0]).WithBox(
            [
                BoxSummary.FromValues(Enumerable.Range(0, 40).Select(i => 19.5 + Math.Sin(i * 1.7) * 0.6)),
                BoxSummary.FromValues(Enumerable.Range(0, 40).Select(i => 20.0 + Math.Sin(i * 2.3) * 2.2)),
                BoxSummary.FromValues([.. Enumerable.Range(0, 40).Select(i => 18.8 + Math.Sin(i * 1.3) * 0.8), 24.6, 15.2]),
            ]),
            ["before"] = SeriesData.FromCategories(["Station 1", "Station 2", "Station 3"], [0.0, 0, 0]).WithBox(
            [
                BoxSummary.FromValues(Enumerable.Range(0, 40).Select(i => 19.8 + Math.Sin(i * 1.1) * 0.9)),
                BoxSummary.FromValues(Enumerable.Range(0, 40).Select(i => 20.6 + Math.Sin(i * 2.9) * 1.4)),
                BoxSummary.FromValues(Enumerable.Range(0, 40).Select(i => 19.1 + Math.Sin(i * 0.7) * 1.0)),
            ]),
        }),

        // The month's result in k€ from the waterfall above, running down the rows.
        ["waterfall-horizontal"] = new(new ChartDefinition
        {
            Title = "Result, March",
            Horizontal = true,
            Y = new AxisOptions { Title = "k€" },
            Series = [new SeriesOptions { Id = "w", Name = "Result", Type = SeriesType.Waterfall, DataLabels = true }],
        }, new()
        {
            ["w"] = SeriesData.FromCategories(["Revenue", "Cost of sales", "Gross profit", "Salaries", "Marketing", "Other income", "Result"],
                [420.0, -180, double.NaN, -120, -45, 15, double.NaN]).WithTotals([false, false, true, false, false, false, true]),
        }, Height: 340),

        ["line-log"] = new(new ChartDefinition
        {
            Title = "Downloads per day",
            Y = new AxisOptions { Scale = ScaleType.Log, Title = "Downloads" },
            Series = [new SeriesOptions { Id = "d", Name = "Downloads", Format = "#,0" }, new SeriesOptions { Id = "e", Name = "Errors", Format = "#,0" }],
            Annotations = [new Annotation { Value = 1000, Label = "1k" }],
        }, new()
        {
            ["d"] = SeriesData.From(Enumerable.Range(0, 60).Select(i => Start.AddDays(i)).ToArray(),
                Enumerable.Range(0, 60).Select(i => Math.Round(20 * Math.Pow(1.17, i) * (1 + 0.15 * Math.Sin(i * 0.9)))).ToArray()),
            ["e"] = SeriesData.From(Enumerable.Range(0, 60).Select(i => Start.AddDays(i)).ToArray(),
                Enumerable.Range(0, 60).Select(i => i % 9 == 4 ? 0 : Math.Round(2 + 0.4 * Math.Pow(1.15, i))).ToArray()),
        }),

        // Page load time against page size, both over several powers of ten (log on both axes), with a line at 1 MB.
        ["scatter-log-log"] = new(new ChartDefinition
        {
            Title = "Load time by page size",
            X = new AxisOptions { Scale = ScaleType.Log, Title = "Page size, kB" },
            Y = new AxisOptions { Scale = ScaleType.Log, Title = "Load time, ms" },
            Series = [new SeriesOptions { Id = "p", Name = "Pages", Type = SeriesType.Scatter, Format = "#,0 'ms'" }],
            Annotations = [new Annotation { Axis = AnnotationAxis.X, Value = 1000.0, Label = "1 MB" }],
        }, new()
        {
            ["p"] = SeriesData.From(Enumerable.Range(0, 40).Select(i => Math.Round(Math.Pow(10, 1 + i * 0.075), 1)).ToArray(),
                Enumerable.Range(0, 40).Select(i => Math.Round(60 * Math.Pow(10, (1 + i * 0.075) * 0.55) * (1 + 0.35 * Math.Sin(i * 2.3)))).ToArray()),
        }),

        ["empty-swedish"] = new(new ChartDefinition { Title = "Utetemperatur", Series = [new SeriesOptions { Id = "s", Name = "Utetemperatur" }] },
            new() { ["s"] = SeriesData.Empty }, Culture: "sv-SE"),

        ["single-point"] = new(new ChartDefinition { Series = [new SeriesOptions { Id = "s", Name = "London", Format = "0.0 °C" }] },
            new() { ["s"] = Hourly(1, _ => 20) }, Height: 200),
    };

    public static TheoryData<string> Names() => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(Names))]
    public void Chart_matches_its_snapshot(string name)
    {
        var c = Cases[name];
        var layout = ChartLayout.Compute(c.Spec, c.Data, new LayoutOptions
        {
            Width = c.Width,
            Height = c.Height,
            Culture = CultureInfo.GetCultureInfo(c.Culture),
            ViewerTimeZone = "Europe/Stockholm",
            XRange = c.XRange,
        });
        var svg = SvgExporter.Write(layout.Scene, ExportTheme.Light, "sans-serif", "#ffffff").ReplaceLineEndings("\n");

        Verify(name, svg);
    }

    private static void Verify(string name, string actual)
    {
        var dir = SnapshotDir();
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, name + ".svg");
        var received = Path.Combine(dir, name + ".received.svg");
        var accept = Environment.GetEnvironmentVariable("RIOCHARTS_ACCEPT_SNAPSHOTS") == "1";

        if (File.Exists(file) && File.ReadAllText(file).ReplaceLineEndings("\n") == actual)
        {
            File.Delete(received);
            return;
        }

        if (accept)
        {
            File.WriteAllText(file, actual);
            File.Delete(received);
            return;
        }

        if (!File.Exists(file))
        {
            File.WriteAllText(file, actual);
            Assert.Fail($"New snapshot {name}.svg written; review it and run the tests again.");
        }

        File.WriteAllText(received, actual);
        Assert.Fail($"{name}.svg changed: compare it with {name}.received.svg. If the change is intended, run with RIOCHARTS_ACCEPT_SNAPSHOTS=1 (or copy the received file over).");
    }
}
