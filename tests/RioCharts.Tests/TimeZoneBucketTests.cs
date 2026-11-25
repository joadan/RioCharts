using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;

namespace RioCharts.Tests;

public class TimeZoneBucketTests
{
    private sealed record Sale(object Time, double Amount);

    private static readonly TimeConversion Stockholm = TimeConversion.For("Europe/Stockholm", UnspecifiedTimes.Utc)!;
    private static readonly TimeConversion NewYork = TimeConversion.For("America/New_York", UnspecifiedTimes.Utc)!;

    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void A_day_is_midnight_to_midnight_in_the_zone()
    {
        // 23:30 UTC is half past midnight the next day in Stockholm (UTC+1 in November).
        var start = Assert.IsType<DateTimeOffset>(Stockholm.Floor(Utc(11, 16, 23, 30), TimeBucket.Day));

        Assert.Equal(new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.FromHours(1)), start);
        Assert.Equal(TimeSpan.FromHours(1), start.Offset);
    }

    [Fact]
    public void Time_strings_with_an_offset_are_instants_whatever_the_machine_zone()
    {
        static double Ms(int hour) => TimeValue.FromDateTime(new DateTime(2026, 3, 1, hour, 0, 0));

        Assert.Equal(Ms(10), Stockholm.ToInstant("2026-03-01 12:00+02:00")); // a space instead of "T"
        Assert.Equal(Ms(17), Stockholm.ToInstant("2026-03-01T12:00-05")); // hours only
        Assert.Equal(Ms(12), Stockholm.ToInstant("2026-03-01T12:00:00Z"));
        Assert.Equal(Ms(12), Stockholm.ToInstant("2026-03-01T12:00")); // unspecified: UTC here
        Assert.True(double.IsNaN(Stockholm.ToInstant("not a time")));
    }

    [Fact]
    public void Months_and_weeks_follow_the_zone_calendar()
    {
        // 03:00 UTC on 1 December is still 30 November (a Monday) in New York.
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4)), NewYork.Floor(Utc(12, 1, 3), TimeBucket.Month)); // EDT on 1 November
        Assert.Equal(new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.FromHours(-5)), NewYork.Floor(Utc(12, 1, 3), TimeBucket.Week));
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(-4)), NewYork.Floor(Utc(12, 31, 23), TimeBucket.Quarter));
    }

    [Fact]
    public void Days_across_a_daylight_saving_change_keep_their_own_offsets()
    {
        // Stockholm moves to summer time on 29 March 2026: that day is 23 hours long.
        var spring = Stockholm.Floor(Utc(3, 29, 12), TimeBucket.Day);
        var after = Stockholm.Floor(Utc(3, 29, 22, 30), TimeBucket.Day); // 00:30 on the 30th, summer time

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.FromHours(1)), spring);
        Assert.Equal(new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.FromHours(2)), after);
        Assert.Equal(TimeSpan.FromHours(23), (DateTimeOffset)after - (DateTimeOffset)spring);
    }

    [Fact]
    public void Times_without_a_zone_are_read_as_set()
    {
        var late = new DateTime(2026, 11, 16, 23, 30, 0, DateTimeKind.Unspecified);
        var display = TimeConversion.For("Europe/Stockholm", UnspecifiedTimes.DisplayZone)!;

        Assert.Equal(new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.FromHours(1)), Stockholm.Floor(late, TimeBucket.Day)); // read as UTC
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.FromHours(1)), display.Floor(late, TimeBucket.Day)); // already Stockholm time
    }

    [Fact]
    public void Dates_and_other_values_bucket_as_without_a_zone()
    {
        Assert.Equal(new DateOnly(2026, 11, 1), Stockholm.Floor(new DateOnly(2026, 11, 22), TimeBucket.Month));
        Assert.Equal("x", Stockholm.Floor("x", TimeBucket.Day));
        Assert.Equal(Utc(11, 16, 23, 30).DateTime.Date, TimeBuckets.Floor(Utc(11, 16, 23, 30).UtcDateTime, TimeBucket.Day, null));
    }

    [Fact]
    public void For_is_null_without_a_known_zone()
    {
        Assert.Null(TimeConversion.For(null, null));
        Assert.Null(TimeConversion.For(" ", null));
        Assert.Null(TimeConversion.For("Mars/Olympus", null));
        Assert.Equal(TimeZoneInfo.Utc, TimeConversion.For("local", null)!.Zone); // viewer not known yet
        Assert.Equal(UnspecifiedTimes.Utc, TimeConversion.For("UTC", null)!.Unspecified);
    }

    [Fact]
    public void Items_group_per_day_in_the_zone()
    {
        Sale[] sales = [new(Utc(11, 16, 22), 1), new(Utc(11, 16, 23, 30), 2), new(Utc(11, 17, 12), 4)];

        var plain = SeriesData.FromTimeBuckets(sales, s => s.Time, TimeBucket.Day, Aggregation.For<Sale>(Aggregate.Sum, s => s.Amount), out _);
        var zoned = SeriesData.FromTimeBuckets(sales, s => s.Time, TimeBucket.Day, Aggregation.For<Sale>(Aggregate.Sum, s => s.Amount), out var groups, Stockholm);

        Assert.Equal([3.0, 4], plain.Y); // UTC wall-clock days
        Assert.Equal([1.0, 6], zoned.Y); // 23:30 UTC is the 17th in Stockholm
        Assert.Equal([[0], [1, 2]], groups);
        Assert.All(zoned.TimeOffsets!, o => Assert.Equal(3_600_000, o)); // starts are instants: the layout places them exactly
    }

    [Fact]
    public void Candles_bucket_in_the_zone()
    {
        Sale[] readings = [new(Utc(11, 16, 22), 5), new(Utc(11, 16, 23, 30), 7), new(Utc(11, 17, 1), 6)];

        var data = SeriesData.FromCandleBuckets(readings, r => r.Time, r => r.Amount, TimeBucket.Day, out _, Stockholm);

        Assert.Equal(2, data.Count);
        Assert.Equal(new CandleSummary(7, 7, 6, 6, 2), data.Candles!.At(1, data.Y[1]));
    }

    [Fact]
    public void Definitions_bucket_in_the_chart_time_zone()
    {
        var parsed = ChartDefinitionJson.Parse("""
            {
              "timeZone": "Europe/Stockholm",
              "data": [
                { "time": "2026-11-16T22:00:00Z", "amount": 1 },
                { "time": "2026-11-16T23:30:00Z", "amount": 2 },
                { "time": "2026-11-17T12:00:00Z", "amount": 4 }
              ],
              "series": [ { "type": "bar", "x": "time", "y": "amount", "timeBucket": "day", "aggregate": "sum" } ]
            }
            """);

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, new Dictionary<string, ChartDataset>());

        Assert.Empty(bound.Errors);
        Assert.Equal([1.0, 6], bound.Data.Values.Single().Y);
        Assert.DoesNotContain(bound.Warnings, w => w.Contains("no zone", StringComparison.Ordinal));
    }

    [Fact]
    public void Definitions_warn_when_bucketed_times_have_no_zone()
    {
        var spec = new ChartDefinition
        {
            TimeZone = "local",
            Data = [new Dictionary<string, object?> { ["time"] = new DateTime(2026, 11, 16, 23, 30, 0), ["amount"] = 1.0 }],
            Series = [new SeriesOptions { Type = SeriesType.Bar, X = "time", Y = "amount", TimeBucket = TimeBucket.Day }],
        };

        var guessed = ChartDefinitionBinder.Bind(spec, new Dictionary<string, ChartDataset>(), viewerTimeZone: "Europe/Stockholm");
        var stated = ChartDefinitionBinder.Bind(spec with { UnspecifiedTimes = UnspecifiedTimes.DisplayZone }, new Dictionary<string, ChartDataset>(), viewerTimeZone: "Europe/Stockholm");

        Assert.Contains(guessed.Warnings, w => w.Contains("timeBucket: some times have no zone", StringComparison.Ordinal));
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.FromHours(1)).UtcDateTime, Instant(guessed));
        Assert.DoesNotContain(stated.Warnings, w => w.Contains("no zone", StringComparison.Ordinal));
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.FromHours(1)).UtcDateTime, Instant(stated));

        static DateTime Instant(DefinitionBindResult bound)
        {
            var data = bound.Data.Values.Single();
            return TimeValue.ToDateTime(data.X[0] - data.TimeOffsets![0]);
        }
    }
}
