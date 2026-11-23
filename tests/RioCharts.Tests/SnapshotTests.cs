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
