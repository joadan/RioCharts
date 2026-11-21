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

    private static double Supply(int i) => Math.Round(19 + 1.5 * Math.Sin(i / 24.0 * 2 * Math.PI) + 0.4 * Math.Sin(i * 1.7), 2);

    private static double Extract(int i) => Math.Round(22 + 0.8 * Math.Sin((i - 3) / 24.0 * 2 * Math.PI) + 0.2 * Math.Cos(i * 1.3), 2);

    private static SeriesData Categories(string[] names, params double[] values) => SeriesData.FromCategories(names, values);

    private static readonly string[] Quarters = ["Q1", "Q2", "Q3", "Q4"];

    private sealed record Case(ChartSpec Spec, Dictionary<string, SeriesData> Data, double Width = 640, double Height = 320, string Culture = "", (double, double)? XRange = null);

    private static readonly Dictionary<string, Case> Cases = new()
    {
        ["line-time"] = new(new ChartSpec
        {
            Title = "Air handling unit",
            X = new AxisSpec { Title = "Time" },
            Y = new AxisSpec { Title = "°C", Format = "0.0" },
            Series = [new SeriesSpec { Id = "s", Name = "Supply", Curve = CurveType.Monotone }, new SeriesSpec { Id = "e", Name = "Extract" }],
        }, new() { ["s"] = Hourly(48, Supply), ["e"] = Hourly(48, Extract) }),

        ["line-gaps-step"] = new(new ChartSpec
        {
            Series = [new SeriesSpec { Id = "t", Name = "Room" }, new SeriesSpec { Id = "sp", Name = "Setpoint", Curve = CurveType.Step }],
        }, new()
        {
            ["t"] = Hourly(36, i => i is >= 12 and < 18 ? double.NaN : 21 + Math.Sin(i / 5.0)),
            ["sp"] = Hourly(36, i => i < 8 ? 18 : i < 20 ? 21 : 19, step: 4),
        }),

        ["line-categories"] = new(new ChartSpec { Series = [new SeriesSpec { Id = "a", Name = "Alarms" }] },
            new() { ["a"] = Categories(["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"], 4, 7, 3, 9, 6, 1, 2) }),

        ["area-stacked"] = new(new ChartSpec
        {
            Y = new AxisSpec { Title = "kW" },
            Series =
            [
                new SeriesSpec { Id = "h", Name = "Heating", Type = SeriesType.Area, Stack = "load" },
                new SeriesSpec { Id = "v", Name = "Ventilation", Type = SeriesType.Area, Stack = "load" },
            ],
        }, new()
        {
            ["h"] = Hourly(48, i => Math.Max(0, 14 - 9 * Math.Sin(i / 24.0 * 2 * Math.PI)), step: 2),
            ["v"] = Hourly(48, i => i % 24 is >= 7 and < 18 ? 6 : 2, step: 2),
        }),

        ["bars-grouped"] = new(new ChartSpec
        {
            Series = [new SeriesSpec { Id = "a", Name = "2025", Type = SeriesType.Bar }, new SeriesSpec { Id = "b", Name = "2026", Type = SeriesType.Bar }],
        }, new() { ["a"] = Categories(Quarters, 120, 95, 60, 110), ["b"] = Categories(Quarters, 105, 90, 52, 98) }),

        ["bars-stacked-negative"] = new(new ChartSpec
        {
            Series =
            [
                new SeriesSpec { Id = "in", Name = "Bought", Type = SeriesType.Bar, Stack = "e" },
                new SeriesSpec { Id = "out", Name = "Sold", Type = SeriesType.Bar, Stack = "e" },
            ],
        }, new() { ["in"] = Categories(Quarters, 40, 25, 10, 35), ["out"] = Categories(Quarters, -5, -18, -30, -8) }),

        ["bars-data-labels"] = new(new ChartSpec
        {
            Series = [new SeriesSpec { Id = "u", Name = "Energy", Type = SeriesType.Bar, DataLabels = true, Format = "0 MWh" }],
        }, new() { ["u"] = Categories(["AHU 1", "AHU 2", "AHU 3", "AHU 4"], 412, 236, 148, 97) }, Height: 260),

        ["pie-other"] = new(new ChartSpec
        {
            Title = "Energy use",
            Series = [new SeriesSpec { Id = "p", Type = SeriesType.Pie, GroupSmallSlices = 0.03, Format = "0 MWh" }],
        }, new() { ["p"] = Categories(["Heating", "Ventilation", "Cooling", "Lighting", "Pumps", "Elevators", "Controls"], 412, 236, 148, 97, 31, 9, 6) }),

        ["donut-total"] = new(new ChartSpec
        {
            Series = [new SeriesSpec { Id = "p", Type = SeriesType.Pie, InnerRadius = 0.6, DataLabels = false, Format = "0 MWh" }],
        }, new() { ["p"] = Categories(["Heating", "Ventilation", "Cooling"], 412, 236, 148) }, Width: 400, Height: 300),

        ["radial-bar"] = new(new ChartSpec
        {
            Series = [new SeriesSpec { Id = "h", Name = "Health", Type = SeriesType.RadialBar, Max = 100, Format = "0'%'" }],
        }, new() { ["h"] = Categories(["Supply fan", "Extract fan", "Filter", "Heat recovery"], 87, 74, 41, 92) }, Width: 420, Height: 300),

        ["dual-axes"] = new(new ChartSpec
        {
            Y = new AxisSpec { Title = "°C" },
            YAxes = [new AxisSpec { Id = "kw", Title = "kW", Position = AxisPosition.Right }],
            Series =
            [
                new SeriesSpec { Id = "t", Name = "Outdoor" },
                new SeriesSpec { Id = "p", Name = "Heating", Type = SeriesType.Bar, YAxisId = "kw" },
            ],
        }, new()
        {
            ["t"] = Hourly(24, i => 4 + 5 * Math.Sin((i - 9) / 24.0 * 2 * Math.PI), step: 2),
            ["p"] = Hourly(24, i => Math.Max(0, 30 - 3 * (4 + 5 * Math.Sin((i - 9) / 24.0 * 2 * Math.PI))), step: 2),
        }),

        ["annotations"] = new(new ChartSpec
        {
            Series = [new SeriesSpec { Id = "s", Name = "Supply" }],
            Annotations =
            [
                new AnnotationSpec { Type = AnnotationType.Band, From = 18.5, To = 20.5, Label = "Comfort" },
                new AnnotationSpec { Type = AnnotationType.Line, Value = 21, Label = "High limit" },
                new AnnotationSpec { Type = AnnotationType.Band, Axis = AnnotationAxis.X, From = Start.AddHours(10), To = Start.AddHours(14), Label = "Service" },
                new AnnotationSpec { Type = AnnotationType.Callout, X = Start.AddHours(30), Y = Supply(30), Label = "Valve stuck" },
            ],
        }, new() { ["s"] = Hourly(48, Supply) }),

        ["time-zone-dst"] = new(new ChartSpec
        {
            TimeZone = "Europe/Stockholm",
            Series = [new SeriesSpec { Id = "t", Name = "Outdoor" }],
        }, new()
        {
            ["t"] = SeriesData.From(Enumerable.Range(0, 36).Select(i => new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero).AddHours(i)).ToArray(),
                Enumerable.Range(0, 36).Select(i => 8 + 3 * Math.Sin(i / 6.0)).ToArray()),
        }),

        ["zoomed"] = new(new ChartSpec { Series = [new SeriesSpec { Id = "s", Name = "Supply" }] },
            new() { ["s"] = Hourly(96, Supply) },
            XRange: (TimeValue.FromDateTime(Start.AddHours(30)), TimeValue.FromDateTime(Start.AddHours(42)))),

        ["compact-sparkline"] = new(new ChartSpec
        {
            Compact = true,
            Series = [new SeriesSpec { Id = "s", Name = "Supply", Curve = CurveType.Monotone, Highlight = PointHighlight.Last | PointHighlight.Min | PointHighlight.Max }],
            Annotations = [new AnnotationSpec { Type = AnnotationType.Band, From = 18.5, To = 20.5 }],
        }, new() { ["s"] = Hourly(48, Supply) }, Width: 160, Height: 32),

        ["auto-compact-small"] = new(new ChartSpec
        {
            Title = "Air handling unit",
            X = new AxisSpec { Title = "Time" },
            Y = new AxisSpec { Title = "°C" },
            Series = [new SeriesSpec { Id = "s", Name = "Supply" }, new SeriesSpec { Id = "e", Name = "Extract" }],
        }, new() { ["s"] = Hourly(48, Supply), ["e"] = Hourly(48, Extract) }, Width: 360, Height: 130),

        ["scatter-trend"] = new(new ChartSpec
        {
            X = new AxisSpec { Title = "Outdoor °C" },
            Y = new AxisSpec { Title = "Supply °C" },
            Series =
            [
                new SeriesSpec { Id = "s", Name = "AHU 1", Type = SeriesType.Scatter, TrendLine = true },
                new SeriesSpec { Id = "t", Name = "AHU 2", Type = SeriesType.Scatter, MarkerSize = 5 },
            ],
        }, new()
        {
            ["s"] = SeriesData.From(Enumerable.Range(0, 40).Select(i => -10 + i * 0.8 + Math.Sin(i * 2.3) * 2).ToArray(),
                Enumerable.Range(0, 40).Select(i => 24 - 0.22 * (-10 + i * 0.8) + Math.Cos(i * 1.7) * 0.8).ToArray()),
            ["t"] = SeriesData.From(Enumerable.Range(0, 30).Select(i => -8 + i * 0.9 + Math.Cos(i * 1.9) * 2).ToArray(),
                Enumerable.Range(0, 30).Select(i => 21 - 0.1 * (-8 + i * 0.9) + Math.Sin(i * 2.1) * 0.6).ToArray()),
        }),

        ["empty-swedish"] = new(new ChartSpec { Title = "Tilluft", Series = [new SeriesSpec { Id = "s", Name = "Tilluft" }] },
            new() { ["s"] = SeriesData.Empty }, Culture: "sv-SE"),

        ["single-point"] = new(new ChartSpec { Series = [new SeriesSpec { Id = "s", Name = "Supply", Format = "0.0 °C" }] },
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
