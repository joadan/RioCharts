using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Definitions;

namespace RioCharts.Tests;

public class CandlestickTests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };
    private static readonly DateTime Start = new(2026, 11, 16);

    private sealed record Reading(DateTime Time, double? Temp);

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    // ---------- Candles ----------

    [Fact]
    public void A_candle_is_the_first_highest_lowest_and_last_value()
    {
        var c = CandleSummary.FromValues([20.1, 21.4, double.NaN, 19.8, 20.6]);

        Assert.Equal(new CandleSummary(20.1, 21.4, 19.8, 20.6, 4), c);
        Assert.False(CandleSummary.FromValues([double.NaN]).IsComplete);
    }

    [Fact]
    public void Merged_candles_keep_the_extremes()
    {
        var merged = CandleSummary.Merge([new(20, 22, 19, 21, 4), CandleSummary.Empty, new(21, 25, 20.5, 24, 4), new(24, 24.5, 18, 19, 4)]);

        Assert.Equal(new CandleSummary(20, 25, 18, 19, 12), merged);
    }

    [Fact]
    public void Readings_are_bucketed_per_hour_in_time_order()
    {
        // Outdoor temperature in °C every 15 minutes, given out of order.
        Reading[] readings =
        [
            new(Start.AddMinutes(45), 20.4), new(Start, 20.0), new(Start.AddMinutes(15), 21.2), new(Start.AddMinutes(30), 19.6),
            new(Start.AddHours(1), 20.4), new(Start.AddHours(1.25), null),
        ];

        var data = SeriesData.FromCandleBuckets(readings, r => r.Time, r => r.Temp, TimeBucket.Hour, out var groups);

        Assert.Equal(2, data.Count);
        Assert.Equal(TimeValue.FromDateTime(Start.AddHours(1)), data.X[1]);
        Assert.Equal(new CandleSummary(20.0, 21.2, 19.6, 20.4, 4), data.Candles!.At(0, data.Y[0]));
        Assert.Equal(new CandleSummary(20.4, 20.4, 20.4, 20.4, 1), data.Candles.At(1, data.Y[1]));
        Assert.Equal(4, groups[0].Length);
    }

    [Fact]
    public void Incomplete_candles_are_gaps()
    {
        var data = SeriesData.From([0.0, 1], [0.0, 0]).WithCandles([new(1, 2, 0.5, 1.5), new(1, double.NaN, 0.5, 1.5)]);

        Assert.Equal(1.5, data.Y[0]);
        Assert.True(double.IsNaN(data.Y[1]));
    }

    // ---------- Layout ----------

    private static SeriesData Days(int count, int seed = 0) =>
        SeriesData.From(Enumerable.Range(0, count).Select(d => Start.AddDays(d)).ToArray(), new double[count]).WithCandles(
            Enumerable.Range(0, count).Select(d =>
            {
                var open = 20 + Math.Sin((d + seed) * 0.7) * 2;
                var close = open + (d % 3 == 0 ? -0.8 : 0.6);
                return new CandleSummary(open, Math.Max(open, close) + 0.5, Math.Min(open, close) - 0.4, close);
            }).ToArray());

    private static ChartLayoutResult Layout(Dictionary<string, SeriesData> data, LayoutOptions? options = null, params SeriesOptions[] series) =>
        ChartLayout.Compute(new ChartDefinition { Series = series.Length > 0 ? series : [new SeriesOptions { Id = "t", Type = SeriesType.Candlestick }] }, data, options ?? Options);

    [Fact]
    public void Rising_candles_are_light_and_falling_ones_solid_in_the_series_color()
    {
        var layout = Layout(new() { ["t"] = Days(12) });

        var bodies = All<RectNode>(layout.Scene.Series).Where(n => n.Class?.StartsWith("rio-candle", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(12, bodies.Count);
        Assert.Equal(4, bodies.Count(b => b.Class == "rio-candle rio-candle-down")); // days 0, 3, 6, 9
        var up = layout.Scene.Styles.Get(bodies.First(b => b.Class == "rio-candle rio-candle-up").Style);
        var down = layout.Scene.Styles.Get(bodies.First(b => b.Class == "rio-candle rio-candle-down").Style);
        Assert.Equal((ThemeTokens.Series(0), ThemeTokens.Series(0), 0.22f), (up.Fill, up.Stroke, up.FillOpacity)); // see-through: the fill shows the direction
        Assert.Equal((ThemeTokens.Series(0), (float?)null), (down.Fill, down.FillOpacity));
        Assert.Contains(All<Node>(layout.Scene.Series), n => n.Class == "rio-wicks rio-wicks-up");
        Assert.Null(layout.Tooltip.Series.Single().PointColors); // the tooltip dot is the series color
        Assert.Empty(layout.Warnings);
    }

    [Fact]
    public void Direction_colors_make_rising_candles_green_and_falling_ones_red()
    {
        var layout = Layout(new() { ["t"] = Days(12) }, null, new SeriesOptions { Id = "t", Type = SeriesType.Candlestick, DirectionColors = true });

        var bodies = All<RectNode>(layout.Scene.Series).Where(n => n.Class?.StartsWith("rio-candle", StringComparison.Ordinal) == true).ToList();
        var up = layout.Scene.Styles.Get(bodies.First(b => b.Class == "rio-candle rio-candle-up").Style);
        Assert.Equal((ThemeTokens.Positive, ThemeTokens.Positive, 0.22f), (up.Fill, up.Stroke, up.FillOpacity));
        Assert.Equal(ThemeTokens.Negative, layout.Scene.Styles.Get(bodies.First(b => b.Class == "rio-candle rio-candle-down").Style).Fill);
        var colors = layout.Tooltip.Series.Single().PointColors!; // the tooltip dot follows the candle
        Assert.Equal(ThemeTokens.Negative, colors[0]);
        Assert.Equal(ThemeTokens.Positive, colors[1]);
    }

    [Fact]
    public void Two_candle_series_get_their_own_colors()
    {
        var layout = Layout(new() { ["a"] = Days(10), ["b"] = Days(10, 3) }, null,
            new SeriesOptions { Id = "a", Type = SeriesType.Candlestick }, new SeriesOptions { Id = "b", Type = SeriesType.Candlestick });

        string FillOf(string id) => layout.Scene.Styles.Get(All<GroupNode>(layout.Scene.Series).Single(g => g.Key == "series:" + id)
            .Children.OfType<RectNode>().First().Style).Fill!;
        Assert.Equal(ThemeTokens.Series(0), FillOf("a"));
        Assert.Equal(ThemeTokens.Series(1), FillOf("b"));
    }

    [Fact]
    public void The_y_axis_covers_highs_and_lows_without_forcing_zero()
    {
        var layout = Layout(new() { ["t"] = Days(12) });

        Assert.True(layout.YScale.DomainMin > 10 && layout.YScale.DomainMin <= 17.6);
        Assert.True(layout.YScale.DomainMax >= 22.5);
    }

    [Fact]
    public void Narrow_candles_merge_and_keep_their_extremes()
    {
        var data = Days(400);
        var layout = Layout(new() { ["t"] = data });

        var bodies = All<RectNode>(layout.Scene.Series).Count(n => n.Class?.StartsWith("rio-candle", StringComparison.Ordinal) == true);
        Assert.InRange(bodies, 5, 399); // fewer candles than days
        var s = layout.Tooltip.Series.Single();
        Assert.Contains(" – ", s.XLabels[0]); // a merged candle says from when to when
        var highest = Enumerable.Range(0, data.Count).Max(i => data.Candles!.High[i]);
        Assert.Contains(s.PartValues!, p => p[1] == ValueFormatterInvariant(highest));
    }

    [Fact]
    public void Merged_candles_follow_the_calendar()
    {
        // Three weeks of hourly candles: too narrow, so they merge into whole periods of the day.
        var hours = SeriesData.From(Enumerable.Range(0, 21 * 24).Select(h => Start.AddHours(h)).ToArray(), new double[21 * 24])
            .WithCandles(Enumerable.Range(0, 21 * 24).Select(h => new CandleSummary(20, 21, 19, 20.5 + h % 3 * 0.1)).ToArray());

        var layout = Layout(new() { ["t"] = hours });

        var s = layout.Tooltip.Series.Single();
        var x = hours.X;
        var period = x[s.Index[2]] - x[s.Index[1]];
        Assert.Contains(period, new[] { 2.0, 3, 6, 12, 24 }.Select(h => h * 3_600_000)); // a whole number of hours that divides a day
        Assert.All(s.Index.Take(s.Index.Length - 1), last => Assert.Equal(0, (x[last + 1] - x[0]) % period)); // each starts on the period
        Assert.Contains(" – ", s.XLabels[0]);
    }

    [Fact]
    public void Merged_candles_follow_the_display_zone_calendar()
    {
        // Daily candles at midnight UTC are 19:00 the evening before in New York.
        var times = Enumerable.Range(0, 400).Select(d => DateTime.SpecifyKind(Start.AddDays(d), DateTimeKind.Utc)).ToArray();
        var days = Days(400);
        var utc = SeriesData.From(times, new double[400]).WithCandles([.. Enumerable.Range(0, 400).Select(i => days.Candles!.At(i, 0))]);
        var spec = new ChartDefinition { TimeZone = "America/New_York", Series = [new SeriesOptions { Id = "t", Type = SeriesType.Candlestick }] };

        var s = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["t"] = utc }, Options).Tooltip.Series.Single();

        var zone = TimeConversion.For("America/New_York", null)!.Zone;
        var starts = s.Index.Take(s.Index.Length - 1).Select(last => TimeZoneInfo.ConvertTimeFromUtc(times[last + 1], zone)).ToList();
        Assert.NotEmpty(starts);
        var weeks = starts.Count > 1 && (starts[1] - starts[0]).TotalDays < 10;
        // Each merged candle starts on a Monday (weeks) or the 1st (months), on New York's calendar.
        Assert.All(starts, t => Assert.True(weeks ? t.DayOfWeek == DayOfWeek.Monday : t.Day == 1, $"{t:yyyy-MM-dd ddd}"));
    }

    private static string ValueFormatterInvariant(double v) => RioCharts.Core.Formatting.ValueFormatter.Format(v, CultureInfo.InvariantCulture, null);

    [Fact]
    public void Tooltip_shows_open_high_low_close_and_the_change()
    {
        var data = SeriesData.FromCategories(["Mon"], [0.0]).WithCandles([new CandleSummary(20, 22.5, 19.5, 21)]);

        var s = Layout(new() { ["t"] = data }, null, new SeriesOptions { Id = "t", Type = SeriesType.Candlestick, Format = "0.0" }).Tooltip.Series.Single();

        Assert.Equal("21.0", s.Values[0]);
        Assert.Equal(["Open", "High", "Low", "Close", "Change"], s.PartLabels!);
        Assert.Equal(["20.0", "22.5", "19.5", "21.0", "+1.0"], s.PartValues![0]); // no percent: it means nothing for e.g. temperatures

        var prices = Layout(new() { ["t"] = data }, null, new SeriesOptions { Id = "t", Type = SeriesType.Candlestick, Format = "0.0", DirectionColors = true });
        Assert.Equal("+1.0 (+5.0%)", prices.Tooltip.Series.Single().PartValues![0][4]);
    }

    [Fact]
    public void Two_candle_series_sit_side_by_side()
    {
        var layout = Layout(new() { ["a"] = Days(10), ["b"] = Days(10, 3) }, null,
            new SeriesOptions { Id = "a", Type = SeriesType.Candlestick, Stack = "s" }, new SeriesOptions { Id = "b", Type = SeriesType.Candlestick, Stack = "s" });

        var hits = layout.Tooltip.Series;
        Assert.Equal(2, hits.Count);
        Assert.NotEqual(hits[0].BarOffset, hits[1].BarOffset);
        Assert.Equal(2, All<GroupNode>(layout.Scene.Static).Count(g => g.Class == "rio-swatch rio-swatch-candle")); // a candle in the legend
    }

    [Fact]
    public void A_candle_series_without_candles_warns()
    {
        var layout = Layout(new() { ["t"] = SeriesData.From([0.0, 1], [1.0, 2]) });

        Assert.Contains(layout.Warnings, w => w.Contains("candlestick needs candles"));
    }

    [Fact]
    public void A_selected_candle_is_outlined_from_high_to_low()
    {
        var data = Days(10);
        var layout = Layout(new() { ["t"] = data }, Options with { SelectedPoints = new Dictionary<string, IReadOnlyCollection<int>> { ["t"] = [2] } });

        var outline = All<RectNode>(layout.Scene.Series).Single(n => n.Class == "rio-selected-bar");
        Assert.Equal(layout.YScale.Map(data.Candles!.High[2]) - 1.5, outline.Bounds.Y, 1);
    }

    // ---------- JSON definitions ----------

    private static Dictionary<string, ChartDataset> Readings(int hours) => new()
    {
        ["d"] = ChartDataset.FromRows("d", Enumerable.Range(0, hours * 4).Select(i => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["time"] = Start.AddMinutes(15 * i), ["temp"] = 20 + i % 4 * 0.5,
        })),
    };

    [Fact]
    public void A_json_candlestick_makes_candles_from_raw_values_per_bucket()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "ohlc", "x": "time", "y": "temp", "timeBucket": "hour" } ] }""");

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, Readings(3));

        Assert.Empty(bound.Errors);
        Assert.DoesNotContain(bound.Warnings, w => w.Contains("using average"));
        var data = bound.Data.Values.Single();
        Assert.Equal(3, data.Count);
        Assert.Equal(new CandleSummary(20, 21.5, 20, 21.5, 4), data.Candles!.At(0, data.Y[0]));
        Assert.Contains("<CandlestickSeries", ChartRazor.FromDefinition(parsed.Definition!, "Row", "rows"), StringComparison.Ordinal);
        Assert.Contains(" Bucket=\"TimeBucket.Hour\"", ChartRazor.FromDefinition(parsed.Definition!, "Row", "rows"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_json_candlestick_reads_precomputed_candles()
    {
        var rows = new[] { (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["day"] = Start, ["first"] = 20.0, ["max"] = 22.0, ["min"] = 19.0, ["last"] = 21.0,
        } };
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "candlestick", "x": "day", "open": "first", "high": "max", "low": "min", "y": "last" } ] }""");

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, new Dictionary<string, ChartDataset> { ["d"] = ChartDataset.FromRows("d", rows) });

        Assert.Empty(bound.Errors);
        var data = bound.Data.Values.Single();
        Assert.Equal(new CandleSummary(20, 22, 19, 21), data.Candles!.At(0, data.Y[0]));
        Assert.Contains(" OpenValue=\"r => r.First\" HighValue=\"r => r.Max\" LowValue=\"r => r.Min\"", ChartRazor.FromDefinition(parsed.Definition!, "Row", "rows"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_json_candlestick_explains_missing_fields()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "dataset": "d", "series": [ { "type": "candlestick", "x": "time", "y": "temp", "open": "temp" } ] }""");

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, Readings(1));

        Assert.Equal(["series[0].low", "series[0].high"], bound.Errors.Select(e => e.Path).Order().Reverse());
    }

    [Fact]
    public void Definitions_and_razor_export_carry_direction_colors()
    {
        var parsed = ChartDefinitionJson.Parse("""{ "series": [ { "type": "candlestick", "x": "time", "y": "price", "timeBucket": "day", "directionColors": true } ] }""");

        Assert.True(parsed.Definition!.Series[0].DirectionColors);
        Assert.Contains("DirectionColors=\"true\"", ChartRazor.FromDefinition(parsed.Definition, "Trade", "trades"));
        Assert.DoesNotContain("DirectionColors", ChartRazor.FromDefinition(parsed.Definition with { Series = [parsed.Definition.Series[0] with { DirectionColors = false }] }));
    }
}
