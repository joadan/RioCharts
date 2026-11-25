using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class CalendarTests
{
    private static readonly LayoutOptions Options = new() { Width = 720, Height = 220, Culture = CultureInfo.InvariantCulture };

    private static SeriesData Days(DateTime first, int count, Func<int, double>? value = null) =>
        SeriesData.From([.. Enumerable.Range(0, count).Select(i => first.AddDays(i))], [.. Enumerable.Range(0, count).Select(i => value?.Invoke(i) ?? i % 9)]);

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    private static ChartLayoutResult Calendar(SeriesData data, SeriesOptions? spec = null, LayoutOptions? options = null, ChartDefinition? chart = null) =>
        ChartLayout.Compute((chart ?? new ChartDefinition()) with { Series = [(spec ?? new SeriesOptions { Id = "c" }) with { Type = SeriesType.Calendar }] },
            new Dictionary<string, SeriesData> { [spec?.Id ?? "c"] = data }, options ?? Options);

    private static List<string> Texts(ChartLayoutResult layout, string cls) =>
        All<TextNode>(layout.Scene.Static).Where(t => t.Class == cls).Select(t => t.Text).ToList();

    private static int Cells(ChartLayoutResult layout, string cls) =>
        All<GroupNode>(layout.Scene.Series).Where(g => g.Class?.Contains(cls, StringComparison.Ordinal) == true).Sum(g => g.Children.Count);

    [Fact]
    public void Days_are_weekday_rows_in_week_columns()
    {
        // 1 January 2026 is a Thursday; the invariant culture's week starts on Sunday.
        var layout = Calendar(Days(new DateTime(2026, 1, 1), 31));

        var s = layout.Tooltip.Series.Single();
        Assert.Equal(31, s.X.Length);
        Assert.Equal(31, Cells(layout, "rio-series-calendar"));
        int At(string day) => Array.FindIndex(s.XLabels, l => l.EndsWith(day, StringComparison.Ordinal));
        int thu = At("01/01/2026"), sun = At("01/04/2026"), wed = At("01/07/2026");
        Assert.Equal(s.X[thu], s.X[At("01/03/2026")], 1); // Thursday to Saturday: one column
        Assert.True(s.X[sun] > s.X[thu] && s.Y[sun] < s.Y[thu]); // Sunday starts the next column, on top
        Assert.Equal(s.X[sun], s.X[wed], 1);
        Assert.Equal(s.Y[sun] + 3 * s.CellHeight, s.Y[wed], 1);
        Assert.Equal(s.BarWidth, s.CellHeight); // square cells
        Assert.StartsWith("Thu ", s.XLabels[thu], StringComparison.Ordinal);
        Assert.Empty(layout.Warnings);
    }

    [Fact]
    public void The_week_starts_on_the_cultures_first_day()
    {
        var layout = Calendar(Days(new DateTime(2026, 1, 1), 14), options: Options with { Culture = new CultureInfo("sv-SE") });

        var s = layout.Tooltip.Series.Single();
        var top = s.Y.Min();
        // Monday 5 January is the top of its column in Sweden.
        Assert.Equal(top, s.Y[Array.FindIndex(s.XLabels, l => l.EndsWith("2026-01-05", StringComparison.Ordinal))], 1);
        Assert.Contains("mån", Texts(layout, "rio-row-label"));
    }

    [Fact]
    public void Missing_days_are_empty_cells()
    {
        var data = SeriesData.From([new DateTime(2026, 3, 2), new DateTime(2026, 3, 9)], [4.0, 7]);

        var layout = Calendar(data);

        Assert.Equal(2, Cells(layout, "rio-series-calendar"));
        Assert.Equal(6, Cells(layout, "rio-calendar-empty"));
    }

    [Fact]
    public void Months_are_named_above_their_first_full_week()
    {
        var layout = Calendar(Days(new DateTime(2026, 1, 1), 90));

        Assert.Equal(["Jan", "Feb", "Mar"], Texts(layout, "rio-month-label"));
        Assert.Equal(7, Texts(layout, "rio-row-label").Count); // room for every weekday
        var year = Calendar(Days(new DateTime(2026, 1, 1), 365), options: Options with { Height = 160 });
        Assert.Equal(["Mon", "Wed", "Fri"], Texts(year, "rio-row-label")); // small cells: every other day
        Assert.Equal(12, Texts(year, "rio-month-label").Count);
    }

    [Fact]
    public void More_than_a_year_is_a_block_per_year()
    {
        var layout = Calendar(Days(new DateTime(2025, 6, 1), 400), options: Options with { Height = 400 });

        Assert.Equal(["2025", "2026"], Texts(layout, "rio-calendar-year"));
        Assert.Equal(365 * 2, Cells(layout, "rio-series-calendar") + Cells(layout, "rio-calendar-empty"));
        Assert.Equal(24, Texts(layout, "rio-month-label").Count);
    }

    [Fact]
    public void Days_follow_the_display_time_zone()
    {
        // 23:30 UTC on 1 March is already 2 March in Stockholm.
        var data = SeriesData.From([new DateTimeOffset(2026, 3, 1, 23, 30, 0, TimeSpan.Zero)], [5.0]);

        var utc = Calendar(data).Tooltip.Series.Single().XLabels.Single();
        var stockholm = Calendar(data, chart: new ChartDefinition { TimeZone = "Europe/Stockholm" }).Tooltip.Series.Single().XLabels.Single();

        Assert.Equal("Sun 03/01/2026", utc);
        Assert.Equal("Mon 03/02/2026", stockholm);
    }

    [Fact]
    public void Colors_follow_the_values_and_can_diverge()
    {
        var data = Days(new DateTime(2026, 1, 1), 10, i => i - 4);

        var plain = Calendar(data);
        var diverging = Calendar(data, new SeriesOptions { Id = "c", Midpoint = 0 });

        Assert.Contains(plain.Scene.Styles.Paint.Keys, k => k == "heat-1");
        Assert.DoesNotContain(plain.Scene.Styles.Paint.Keys, k => k.StartsWith("heat-below", StringComparison.Ordinal));
        Assert.Contains(diverging.Scene.Styles.Paint.Keys, k => k.StartsWith("heat-below", StringComparison.Ordinal));
        Assert.Contains("rio-legend rio-heat-legend", All<GroupNode>(plain.Scene.Static).Select(g => g.Class));
    }

    [Fact]
    public void Edge_cases_render_a_clean_state()
    {
        var empty = Calendar(SeriesData.Empty);
        Assert.False(empty.HasData);
        Assert.Contains("No data available", All<TextNode>(empty.Scene.Overlay).Select(t => t.Text));

        var single = Calendar(SeriesData.From([new DateTime(2026, 5, 5)], [3.0]));
        Assert.Single(single.Tooltip.Series.Single().X);

        var numbers = Calendar(SeriesData.From([1.0, 2, 3], [1.0, 2, 3]));
        Assert.Contains(numbers.Warnings, w => w.Contains("a calendar needs dates or times", StringComparison.Ordinal));

        var twice = Calendar(SeriesData.From([new DateTime(2026, 5, 5, 8, 0, 0), new DateTime(2026, 5, 5, 17, 0, 0)], [1.0, 2]));
        Assert.Contains(twice.Warnings, w => w.Contains("several values on", StringComparison.Ordinal));

        var compact = Calendar(Days(new DateTime(2026, 1, 1), 60), options: Options with { Width = 120, Height = 30 }, chart: new ChartDefinition { Compact = true });
        Assert.Empty(Texts(compact, "rio-month-label"));
        Assert.Equal(60, compact.Tooltip.Series.Single().X.Length);
    }

    [Fact]
    public void The_description_names_the_lowest_and_highest_day()
    {
        var layout = Calendar(Days(new DateTime(2026, 1, 1), 5, i => i == 2 ? 40 : 1), chart: new ChartDefinition { Title = "Orders" });

        Assert.Equal("Orders. Calendar heatmap with 5 days: lowest 1 at Thu 01/01/2026, highest 40 at Sat 01/03/2026.", layout.Scene.Description);
    }

    private static Dictionary<string, ChartDataset> Orders() => new()
    {
        ["orders"] = ChartDataset.FromRows("orders", new (DateTime At, double Amount)[]
            {
                (new DateTime(2026, 2, 2, 9, 0, 0), 120), (new DateTime(2026, 2, 2, 15, 0, 0), 80), (new DateTime(2026, 2, 3, 11, 0, 0), 40),
            }
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["at"] = r.At, ["amount"] = r.Amount })),
    };

    [Theory]
    [InlineData("week")]
    [InlineData("auto")]
    public void A_json_calendar_buckets_by_day_whatever_timeBucket_says(string bucket)
    {
        var parsed = ChartDefinitionJson.Parse($$"""{ "dataset": "orders", "series": [ { "type": "calendar", "x": "at", "y": "amount", "timeBucket": "{{bucket}}" } ] }""");

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, Orders());

        Assert.True(bound.Success, string.Join("; ", bound.Errors.Select(e => e.Message)));
        Assert.Contains(bound.Warnings, w => w.Contains($"'{bucket}' is ignored", StringComparison.Ordinal));
        Assert.Equal([200.0, 40], bound.Data.Values.Single().Y); // one value per day, as without timeBucket
    }

    [Theory]
    [InlineData("""{ "dataset": "orders", "series": [ { "type": "calendar", "x": "at", "y": "amount" } ] }""", "200", "40")]
    [InlineData("""{ "dataset": "orders", "series": [ { "type": "calendarHeatmap", "x": "at" } ] }""", "2", "1")]
    public void A_json_calendar_sums_or_counts_rows_per_day(string json, string first, string second)
    {
        var parsed = ChartDefinitionJson.Parse(json);
        Assert.True(parsed.Success, string.Join("; ", parsed.Errors.Select(e => e.Message)));
        Assert.Equal(SeriesType.Calendar, parsed.Definition!.Series[0].Type);

        var bound = ChartDefinitionBinder.Bind(parsed.Definition, Orders());

        Assert.True(bound.Success, string.Join("; ", bound.Errors.Select(e => e.Message)));
        Assert.Empty(bound.Warnings);
        var layout = ChartLayout.Compute(bound.Definition, bound.Data, Options);
        Assert.Equal([first, second], layout.Tooltip.Series.Single().Values);
    }

    [Fact]
    public void A_json_calendar_needs_a_time_x()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "orders", "series": [ { "type": "calendar", "x": "amount" } ] }""");

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, Orders());

        Assert.Contains(bound.Errors, e => e.Message.Contains("a calendar needs a date or time x field", StringComparison.Ordinal));
    }

    [Fact]
    public void Razor_for_a_calendar_is_a_calendar_series()
    {
        var razor = ChartRazor.FromDefinition(ChartDefinitionJson.Parse(
            """{ "dataset": "orders", "series": [ { "type": "calendar", "x": "at", "y": "amount", "midpoint": 50 } ] }""").Definition!);

        Assert.Contains("<CalendarSeries XValue=\"r => r.At\" YValue=\"r => r.Amount\"", razor, StringComparison.Ordinal);
        Assert.Contains("Midpoint=\"50\"", razor, StringComparison.Ordinal);
    }
}
