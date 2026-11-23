using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;

namespace RioCharts.Tests;

public class TimeBucketTests
{
    private sealed record Order(object Time, double? Amount);

    private static readonly DateTime Monday = new(2026, 11, 16);

    [Theory]
    [InlineData(1.0 / 24, TimeBucket.Minute)] // an hour: 60 minutes
    [InlineData(1.0, TimeBucket.Hour)]
    [InlineData(7.0, TimeBucket.Hour)]       // 168 hours is closer to 50 than 7 days
    [InlineData(30.0, TimeBucket.Day)]
    [InlineData(365.0, TimeBucket.Week)]
    [InlineData(3 * 365.0, TimeBucket.Month)]
    [InlineData(10 * 365.0, TimeBucket.Quarter)]
    [InlineData(30 * 365.0, TimeBucket.Year)]
    public void Auto_picks_the_bucket_closest_to_50_buckets(double days, TimeBucket expected)
    {
        Assert.Equal(expected, TimeBuckets.ForSpan(TimeSpan.FromDays(days)));
    }

    [Fact]
    public void Auto_without_a_span_is_a_day()
    {
        Assert.Equal(TimeBucket.Day, TimeBuckets.ForSpan(TimeSpan.Zero));
        Assert.Equal(TimeBucket.Day, TimeBuckets.Resolve(TimeBucket.Auto, []));
        Assert.Equal(TimeBucket.Day, TimeBuckets.Resolve(TimeBucket.Auto, [Monday, "text", null, 4.0]));
    }

    [Fact]
    public void Resolve_reads_the_span_of_all_kinds_of_times()
    {
        object?[] values = [new DateOnly(2026, 1, 1), new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(2)), Monday, null, "x"];

        Assert.Equal(TimeBucket.Week, TimeBuckets.Resolve(TimeBucket.Auto, values));
        Assert.Equal(TimeBucket.Month, TimeBuckets.Resolve(TimeBucket.Month, values)); // not Auto: unchanged
    }

    [Fact]
    public void Floor_handles_DateOnly_and_refuses_auto()
    {
        Assert.Equal(new DateOnly(2026, 11, 1), TimeBuckets.Floor(new DateOnly(2026, 11, 22), TimeBucket.Month));
        Assert.Equal(new DateOnly(2026, 11, 16), TimeBuckets.Floor(new DateOnly(2026, 11, 22), TimeBucket.Week));
        Assert.Equal(new DateOnly(2026, 11, 22), TimeBuckets.Floor(new DateOnly(2026, 11, 22), TimeBucket.Hour));
        Assert.Throws<ArgumentException>(() => TimeBuckets.Floor(Monday, TimeBucket.Auto));
    }

    [Fact]
    public void Items_group_per_bucket_and_map_back()
    {
        Order[] orders = [new(Monday.AddHours(20), 5), new(Monday.AddHours(2), 10), new(Monday.AddDays(1), 7), new(null!, 99), new(Monday.AddHours(9), null)];

        var data = SeriesData.FromTimeBuckets(orders, o => o.Time, TimeBucket.Day, Aggregation.For<Order>(Aggregate.Sum, o => o.Amount), out var groups);

        Assert.Equal(XKind.Time, data.Kind);
        Assert.Equal([TimeValue.FromDateTime(Monday), TimeValue.FromDateTime(Monday.AddDays(1))], data.X);
        Assert.Equal([15.0, 7], data.Y);
        Assert.Equal([0, 1, 4], groups[data.ToSourceIndex(0)]); // null time left out
    }

    [Fact]
    public void Auto_buckets_a_year_of_days_into_weeks()
    {
        var orders = Enumerable.Range(0, 365).Select(i => new Order(Monday.AddDays(i), 1)).ToList();

        var data = SeriesData.FromTimeBuckets(orders, o => o.Time, TimeBucket.Auto, g => g.Count, out var groups);

        Assert.Equal(53, data.Count);
        Assert.Equal(7, data.Y[0]);
        Assert.Equal(1, data.Y[^1]);
        Assert.Equal(365, groups.Sum(g => g.Length));
    }

    [Fact]
    public void Empty_items_give_empty_data()
    {
        var data = SeriesData.FromTimeBuckets(Array.Empty<Order>(), o => o.Time, TimeBucket.Auto, g => g.Count, out var groups);

        Assert.Equal(0, data.Count);
        Assert.Empty(groups);
    }

    [Fact]
    public void Candles_take_an_auto_bucket()
    {
        // Readings every 15 minutes for a day: hours.
        var readings = Enumerable.Range(0, 96).Select(i => new Order(Monday.AddMinutes(15 * i), i)).ToList();

        var data = SeriesData.FromCandleBuckets(readings, r => r.Time, r => r.Amount, TimeBucket.Auto, out _);

        Assert.Equal(24, data.Count);
        Assert.Equal(new CandleSummary(0, 3, 0, 3, 4), data.Candles!.At(0, data.Y[0]));
    }

    [Fact]
    public void Specs_take_an_auto_bucket_and_say_what_it_chose()
    {
        var rows = string.Join(",", Enumerable.Range(0, 365).Select(i => $$"""{"time":"{{Monday.AddDays(i):yyyy-MM-dd}}","amount":1}"""));
        var parsed = ChartDefinitionJson.Parse($$"""
            { "data": [{{rows}}], "series": [ { "type": "bar", "x": "time", "y": "amount", "timeBucket": "automatic", "aggregate": "sum" } ] }
            """);
        Assert.Empty(parsed.Errors);
        Assert.Equal(TimeBucket.Auto, parsed.Definition!.Series[0].TimeBucket);

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, new Dictionary<string, ChartDataset>());

        Assert.Empty(bound.Errors);
        Assert.Contains(bound.Warnings, w => w.Contains("timeBucket: auto chose week", StringComparison.Ordinal));
        Assert.Equal(53, bound.Data.Values.Single().Count);
    }

    [Fact]
    public void Razor_export_writes_the_bucket_for_line_area_and_bar()
    {
        var spec = ChartDefinitionJson.Parse("""
            { "series": [ { "type": "bar", "x": "time", "y": "energy", "aggregate": "sum", "timeBucket": "day" },
                          { "type": "line", "x": "time", "y": "energy", "timeBucket": "auto" },
                          { "type": "scatter", "x": "time", "y": "energy", "aggregate": "max", "timeBucket": "hour" } ] }
            """).Definition!;

        var razor = ChartRazor.FromDefinition(spec, "Reading", "readings");

        Assert.Contains("<BarSeries XValue=\"r => r.Time\" YValue=\"r => r.Energy\" Aggregate=\"Aggregate.Sum\" Bucket=\"TimeBucket.Day\" />", razor);
        Assert.Contains("<LineSeries XValue=\"r => r.Time\" YValue=\"r => r.Energy\" Bucket=\"TimeBucket.Auto\" />", razor);
        Assert.Contains("per hour; aggregate: max", razor); // scatter has no Bucket: a note for the query
    }
}
