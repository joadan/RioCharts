using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class RangeBarTests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    private sealed record Period(string Machine, string State, DateTime From, DateTime? To);

    private static readonly DateTime Day = new(2026, 3, 2, 0, 0, 0, DateTimeKind.Unspecified);

    // A packing line's machines through a morning: running, idle and stopped periods.
    private static readonly Period[] Periods =
    [
        new("Filler", "Running", Day.AddHours(6), Day.AddHours(9.5)),
        new("Capper", "Running", Day.AddHours(6.5), Day.AddHours(8)),
        new("Filler", "Stopped", Day.AddHours(9.5), Day.AddHours(10)),
        new("Capper", "Idle", Day.AddHours(8), Day.AddHours(8.25)),
        new("Capper", "Running", Day.AddHours(8.25), Day.AddHours(12)),
        new("Labeler", "Running", Day.AddHours(7), Day.AddHours(12)),
        new("Filler", "Running", Day.AddHours(10), Day.AddHours(12)),
    ];

    private static SeriesData State(string state) =>
        SeriesData.FromRanges(Periods.Where(p => p.State == state), p => p.Machine, p => p.From, p => p.To);

    private static ChartLayoutResult Layout(ChartDefinition? spec = null, LayoutOptions? options = null) =>
        ChartLayout.Compute(spec ?? new ChartDefinition
        {
            Series =
            [
                new SeriesOptions { Id = "running", Name = "Running", Type = SeriesType.RangeBar },
                new SeriesOptions { Id = "idle", Name = "Idle", Type = SeriesType.RangeBar },
                new SeriesOptions { Id = "stopped", Name = "Stopped", Type = SeriesType.RangeBar },
            ],
        }, new Dictionary<string, SeriesData> { ["running"] = State("Running"), ["idle"] = State("Idle"), ["stopped"] = State("Stopped") }, options ?? Options);

    private static List<RectNode> Bars(ChartLayoutResult layout, string id) =>
        All<GroupNode>(layout.Scene.Series).Single(g => g.Key == "series:" + id).Children.OfType<RectNode>().ToList();

    // ---------- Data ----------

    [Fact]
    public void From_ranges_keeps_start_end_and_row_sorted_by_start()
    {
        var data = State("Running");

        Assert.Equal(XKind.Time, data.Kind);
        Assert.Equal(5, data.Count);
        Assert.Equal(["Filler", "Capper", "Labeler", "Capper", "Filler"], data.Rows!);
        Assert.Equal(TimeValue.FromDateTime(Day.AddHours(7)), data.X[2]);
        Assert.Equal(TimeValue.FromDateTime(Day.AddHours(12)), data.Y[2]);
        Assert.Equal(3, data.ToSourceIndex(2)); // Labeler is input item 3 of the running periods
    }

    [Fact]
    public void Items_without_a_start_are_left_out_and_a_missing_end_is_a_gap()
    {
        var data = SeriesData.FromRanges(
            [(Row: "a", From: (double?)1, To: (double?)3), (Row: "b", From: null, To: 4), (Row: "c", From: 2, To: null)],
            t => t.Row, t => t.From, t => t.To);

        Assert.Equal(XKind.Numeric, data.Kind);
        Assert.Equal(["a", "c"], data.Rows!);
        Assert.True(double.IsNaN(data.Y[1]));
    }

    // ---------- Layout ----------

    [Fact]
    public void Rows_are_in_order_of_first_appearance_and_bars_span_start_to_end()
    {
        var layout = Layout();
        var labels = All<TextNode>(layout.Scene.Static).Where(t => t.Class == "rio-row-label").ToList();

        Assert.Equal(["Filler", "Capper", "Labeler"], labels.Select(l => l.Text));
        Assert.Empty(layout.Warnings);
        Assert.Equal(XKind.Time, layout.XKind);

        var running = Bars(layout, "running");
        Assert.Equal(5, running.Count);
        var labeler = running.Single(b => Math.Abs(b.Bounds.Y + b.Bounds.Height / 2 - labels[2].At.Y) < 0.5);
        Assert.Equal(layout.XScale!.Map(TimeValue.FromDateTime(Day.AddHours(7))), labeler.Bounds.X, 1);
        Assert.Equal(layout.XScale.Map(TimeValue.FromDateTime(Day.AddHours(12))), labeler.Bounds.Right, 1);
        Assert.Equal(TimeValue.FromDateTime(Day.AddHours(6)), layout.XScale.DomainMin);
        Assert.Equal(TimeValue.FromDateTime(Day.AddHours(12)), layout.XScale.DomainMax);
    }

    [Fact]
    public void Series_share_the_rows_and_get_a_legend()
    {
        var layout = Layout();

        Assert.Equal(["Running", "Idle", "Stopped"], layout.Legend.Select(l => l.Label));
        var idle = Bars(layout, "idle").Single();
        var capperRunning = Bars(layout, "running").Where(b => Math.Abs(b.Bounds.Y - idle.Bounds.Y) < 0.5).ToList();
        Assert.Equal(2, capperRunning.Count); // the same row as the idle period between them
        Assert.Contains(All<GroupNode>(layout.Scene.Static), g => g.Class?.Contains("rio-legend", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Tooltips_show_the_row_the_times_and_the_duration()
    {
        var s = Layout().Tooltip.Series.Single(t => t.Id == "stopped");

        Assert.Equal("Filler", s.XLabels.Single());
        Assert.Equal("09:30 – 10:00 (30 min)", s.Values.Single());
        Assert.True(s.CellHeight > 0);
        Assert.NotNull(s.CellWidths);
    }

    [Fact]
    public void Bars_over_several_days_show_dates_and_keep_the_end_short_on_the_same_day()
    {
        var data = SeriesData.FromRanges(
            [(From: Day.AddHours(22), To: Day.AddHours(30)), (From: Day.AddDays(1).AddHours(8), To: Day.AddDays(1).AddHours(12))],
            null, t => t.From, t => t.To);
        var values = ChartLayout.Compute(new ChartDefinition { Series = [new SeriesOptions { Id = "a", Name = "Shift", Type = SeriesType.RangeBar }] },
            new Dictionary<string, SeriesData> { ["a"] = data }, Options).Tooltip.Series.Single().Values;

        Assert.Equal("03/02/2026 22:00 – 03/03/2026 06:00 (8 h)", values[0]);
        Assert.Equal("03/03/2026 08:00 – 12:00 (4 h)", values[1]);
    }

    [Fact]
    public void Durations_read_naturally()
    {
        string[] units = ["d", "h", "min", "s"];
        var c = CultureInfo.InvariantCulture;

        Assert.Equal("45 s", ChartLayout.FormatDuration(45_000, units, c));
        Assert.Equal("2.5 s", ChartLayout.FormatDuration(2_500, units, c));
        Assert.Equal("12 min", ChartLayout.FormatDuration(12 * 60_000, units, c));
        Assert.Equal("2 h 30 min", ChartLayout.FormatDuration(150 * 60_000, units, c));
        Assert.Equal("3 h", ChartLayout.FormatDuration(3 * 3_600_000, units, c));
        Assert.Equal("3 d 4 h", ChartLayout.FormatDuration(76 * 3_600_000, units, c));
    }

    [Fact]
    public void Duration_units_follow_the_language()
    {
        var layout = Layout(options: Options with { Culture = CultureInfo.GetCultureInfo("fr-FR") });

        Assert.EndsWith("(30 min)", layout.Tooltip.Series.Single(t => t.Id == "stopped").Values.Single(), StringComparison.Ordinal);
        Assert.Contains("barres sur", layout.Scene.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void The_description_summarizes_each_series()
    {
        var description = Layout().Scene.Description;

        Assert.Contains("Running: 5 bars in 3 rows, from", description, StringComparison.Ordinal);
        Assert.Contains("Idle: 1 bar in 1 rows", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_rows_each_series_is_a_row()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Series =
            [
                new SeriesOptions { Id = "a", Name = "Design", Type = SeriesType.RangeBar },
                new SeriesOptions { Id = "b", Name = "Build", Type = SeriesType.RangeBar },
            ],
        }, new Dictionary<string, SeriesData> { ["a"] = SeriesData.From([0.0], [4.0]), ["b"] = SeriesData.From([3.0], [9.0]) }, Options);

        Assert.Equal(["Design", "Build"], All<TextNode>(layout.Scene.Static).Where(t => t.Class == "rio-row-label").Select(t => t.Text));
        Assert.Equal(XKind.Numeric, layout.XKind);
        Assert.Equal("3 – 9 (6)", layout.Tooltip.Series[1].Values.Single());
    }

    [Fact]
    public void Hidden_series_keep_their_rows()
    {
        var layout = Layout(options: Options with { HiddenSeries = new HashSet<string> { "running" } });

        Assert.Equal(3, All<TextNode>(layout.Scene.Static).Count(t => t.Class == "rio-row-label"));
        Assert.DoesNotContain(All<GroupNode>(layout.Scene.Series), g => g.Key == "series:running");
    }

    [Fact]
    public void Point_colors_and_data_labels()
    {
        var data = State("Running").WithColors(["--rio-negative", null, null, null, null]);
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "r", Type = SeriesType.RangeBar, DataLabels = true }],
        }, new Dictionary<string, SeriesData> { ["r"] = data }, Options with { Width = 900 });

        Assert.Equal("--rio-negative", layout.Scene.Styles.Get(Bars(layout, "r")[0].Style).Fill);
        Assert.Contains(All<TextNode>(layout.Scene.Series), t => t.Text == "3 h 30 min");
    }

    [Fact]
    public void X_lines_and_bands_are_drawn_across_the_rows()
    {
        var layout = Layout(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "running", Type = SeriesType.RangeBar }],
            Annotations =
            [
                new Annotation { Axis = AnnotationAxis.X, Value = Day.AddHours(11), Label = "Now" },
                new Annotation { Type = AnnotationType.Band, Axis = AnnotationAxis.X, From = Day.AddHours(9.5), To = Day.AddHours(10) },
                new Annotation { Value = 5 },
            ],
        });

        var line = All<PathNode>(layout.Scene.Series).Single(p => p.Class?.Contains("rio-annotation-line", StringComparison.Ordinal) == true);
        Assert.NotNull(line);
        Assert.Single(All<RectNode>(layout.Scene.Series), r => r.Class?.Contains("rio-annotation-band", StringComparison.Ordinal) == true);
        Assert.Contains(layout.Warnings, w => w.Contains("x lines and bands only", StringComparison.Ordinal));
    }

    [Fact]
    public void Bars_ending_before_they_start_are_skipped_with_a_warning()
    {
        var layout = ChartLayout.Compute(new ChartDefinition { Series = [new SeriesOptions { Id = "a", Type = SeriesType.RangeBar }] },
            new Dictionary<string, SeriesData> { ["a"] = SeriesData.From([1.0, 5.0], [3.0, 4.0]) }, Options);

        Assert.Single(Bars(layout, "a"));
        Assert.Contains(layout.Warnings, w => w.Contains("end before they start", StringComparison.Ordinal));
    }

    [Fact]
    public void Other_series_types_are_skipped_with_a_warning()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "a", Type = SeriesType.RangeBar }, new SeriesOptions { Id = "l", Type = SeriesType.Line }],
        }, new Dictionary<string, SeriesData> { ["a"] = SeriesData.From([1.0], [3.0]), ["l"] = SeriesData.From([1.0, 2], [1.0, 2]) }, Options);

        Assert.Contains(layout.Warnings, w => w.Contains("cannot be mixed with range bar", StringComparison.Ordinal));
    }

    [Fact]
    public void A_zoom_window_narrows_the_axis()
    {
        var from = TimeValue.FromDateTime(Day.AddHours(9));
        var to = TimeValue.FromDateTime(Day.AddHours(11));
        var layout = Layout(options: Options with { XRange = (from, to) });

        Assert.True(layout.Zoomed);
        Assert.Equal(from, layout.XScale!.DomainMin);
        Assert.Equal(TimeValue.FromDateTime(Day.AddHours(6)), layout.XExtent!.Value.Min);
        Assert.All(Bars(layout, "running"), b => Assert.True(b.Bounds.X >= layout.Plot.X - 0.5 && b.Bounds.Right <= layout.Plot.Right + 0.5));
    }

    [Fact]
    public void A_display_time_zone_converts_starts_and_ends()
    {
        var utc = SeriesData.FromRanges([(From: new DateTime(2026, 3, 2, 6, 0, 0, DateTimeKind.Utc), To: new DateTime(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc))],
            null, t => t.From, t => t.To);
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            TimeZone = "Europe/Stockholm",
            Series = [new SeriesOptions { Id = "a", Name = "Shift", Type = SeriesType.RangeBar }],
        }, new Dictionary<string, SeriesData> { ["a"] = utc }, Options with { ViewerTimeZone = "Europe/Stockholm" });

        Assert.Equal("07:00 – 09:00 (2 h)", layout.Tooltip.Series.Single().Values.Single());
    }

    [Fact]
    public void Odd_data_renders_cleanly()
    {
        foreach (var data in new[]
        {
            SeriesData.Empty, SeriesData.FromCategories(["a"], [1.0]), SeriesData.From([5.0], [5.0]), SeriesData.From([5.0], [double.NaN]),
            SeriesData.FromRanges(Array.Empty<Period>(), p => p.Machine, p => p.From, p => p.To),
        })
        {
            var layout = ChartLayout.Compute(new ChartDefinition { Series = [new SeriesOptions { Id = "a", Type = SeriesType.RangeBar }] },
                new Dictionary<string, SeriesData> { ["a"] = data }, Options);
            Assert.NotNull(layout.Scene);
        }

        var compact = ChartLayout.Compute(new ChartDefinition { Compact = true, Series = [new SeriesOptions { Id = "a", Type = SeriesType.RangeBar }] },
            new Dictionary<string, SeriesData> { ["a"] = State("Running") }, Options with { Width = 120, Height = 30 });
        Assert.Equal(5, Bars(compact, "a").Count);
    }

    // ---------- JSON definitions ----------

    private static Dictionary<string, ChartDataset> Line() => new()
    {
        ["line"] = ChartDataset.FromRows("line", Periods.Select(p => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["machine"] = p.Machine, ["state"] = p.State, ["from"] = p.From, ["to"] = p.To,
        })),
    };

    [Fact]
    public void A_json_timeline_groups_by_state()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "line", "series": [ { "type": "timeline", "row": "machine", "start": "from", "end": "to", "groupBy": "state" } ] }""");
        Assert.True(parsed.Success, string.Join("; ", parsed.Errors.Select(e => e.Message)));

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, Line());

        Assert.True(bound.Success, string.Join("; ", bound.Errors.Select(e => e.Message)));
        Assert.Equal(["Running", "Stopped", "Idle"], bound.Definition.Series.Select(s => s.Name));
        Assert.All(bound.Definition.Series, s => Assert.Equal(SeriesType.RangeBar, s.Type));
        Assert.Equal(5, bound.Data[bound.Definition.Series[0].Id].Count);
        var layout = ChartLayout.Compute(bound.Definition, bound.Data, Options);
        Assert.Equal(7, layout.Tooltip.Series.Sum(s => s.X.Length));
    }

    [Fact]
    public void A_json_range_bar_explains_missing_and_wrong_fields()
    {
        var missing = ChartDefinitionBinder.Bind(ChartDefinitionJson.Parse("""{ "dataset": "line", "series": [ { "type": "gantt", "row": "machine", "start": "from" } ] }""").Definition!, Line());
        Assert.Contains(missing.Errors, e => e.Path == "series[0].end" && e.Message.Contains("candidates: from, to", StringComparison.Ordinal));

        var text = ChartDefinitionBinder.Bind(ChartDefinitionJson.Parse("""{ "dataset": "line", "series": [ { "type": "rangeBar", "start": "state", "end": "to" } ] }""").Definition!, Line());
        Assert.Contains(text.Errors, e => e.Path == "series[0].start" && e.Message.Contains("is text", StringComparison.Ordinal));

        var inferred = ChartDefinitionBinder.Bind(ChartDefinitionJson.Parse("""{ "dataset": "line", "series": [ { "type": "rangeBar", "row": "machine" } ] }""").Definition!, Line());
        Assert.True(inferred.Success);
        Assert.Contains(inferred.Warnings, w => w.Contains("using 'from' to 'to'", StringComparison.Ordinal));
    }

    [Fact]
    public void Razor_for_a_range_bar_uses_row_start_and_end()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "line", "series": [ { "type": "rangeBar", "row": "machine", "start": "from", "end": "to" } ] }""");
        var razor = ChartRazor.FromDefinition(parsed.Definition!, "Period", "periods");

        Assert.Contains("<RangeBarSeries", razor, StringComparison.Ordinal);
        Assert.Contains(" RowValue=\"r => r.Machine\" StartValue=\"r => r.From\" EndValue=\"r => r.To\"", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("XValue", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("YValue", razor, StringComparison.Ordinal);
    }
}
