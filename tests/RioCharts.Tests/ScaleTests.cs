using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;

namespace RioCharts.Tests;

public class ScaleTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Sv = CultureInfo.GetCultureInfo("sv-SE");

    [Theory]
    [InlineData(100, 5, 20)]
    [InlineData(10, 5, 2)]
    [InlineData(1, 5, 0.2)]
    [InlineData(7, 5, 2)]
    [InlineData(3, 6, 0.5)]
    public void Nice_steps_are_1_2_or_5(double span, int count, double expected)
    {
        Assert.Equal(expected, NiceTicks.Step(span, count), 10);
    }

    [Fact]
    public void Nice_domain_rounds_out()
    {
        var (min, max, step) = NiceTicks.NiceDomain(3, 47, 5);

        Assert.Equal(0, min);
        Assert.Equal(50, max);
        Assert.Equal(10, step);
    }

    [Fact]
    public void Ticks_have_no_floating_point_noise()
    {
        var ticks = NiceTicks.Ticks(0, 1, 0.1);

        Assert.Equal(0.3, ticks[3]);
        Assert.Equal(11, ticks.Count);
    }

    [Fact]
    public void Linear_scale_maps_and_inverts()
    {
        var scale = new ContinuousScale(0, 100, 50, 250);

        Assert.Equal(150, scale.Map(50));
        Assert.Equal(50, scale.Invert(150), 9);
    }

    [Fact]
    public void Degenerate_domain_does_not_divide_by_zero()
    {
        var scale = new ContinuousScale(5, 5, 0, 100);

        Assert.Equal(50, scale.Map(5), 9);
    }

    [Fact]
    public void Band_scale_spaces_categories_evenly()
    {
        var band = new BandScale(["a", "b", "c"], 0, 300, paddingInner: 0, paddingOuter: 0);

        Assert.Equal(100, band.Bandwidth, 9);
        Assert.Equal(50, band.Center(0), 9);
        Assert.Equal(250, band.Center(2), 9);
    }

    [Fact]
    public void Hourly_ticks_land_on_whole_hours()
    {
        var min = TimeValue.FromDateTime(new DateTime(2026, 3, 1, 9, 17, 0));
        var max = TimeValue.FromDateTime(new DateTime(2026, 3, 1, 14, 5, 0));

        var ticks = TimeTicks.Generate(min, max, new TimeInterval(TimeUnit.Hour, 1)).Select(TimeValue.ToDateTime).ToList();

        Assert.Equal([10, 11, 12, 13, 14], ticks.Select(t => t.Hour));
        Assert.All(ticks, t => Assert.Equal(0, t.Minute));
    }

    [Fact]
    public void Six_hour_ticks_align_to_midnight()
    {
        var min = TimeValue.FromDateTime(new DateTime(2026, 3, 1, 1, 0, 0));
        var max = TimeValue.FromDateTime(new DateTime(2026, 3, 2, 1, 0, 0));

        var hours = TimeTicks.Generate(min, max, new TimeInterval(TimeUnit.Hour, 6)).Select(t => TimeValue.ToDateTime(t).Hour);

        Assert.Equal([6, 12, 18, 0], hours);
    }

    [Fact]
    public void Weekly_ticks_are_mondays()
    {
        var min = TimeValue.FromDateTime(new DateTime(2026, 3, 1));
        var max = TimeValue.FromDateTime(new DateTime(2026, 4, 1));

        var ticks = TimeTicks.Generate(min, max, new TimeInterval(TimeUnit.Week, 1)).Select(TimeValue.ToDateTime).ToList();

        Assert.All(ticks, t => Assert.Equal(DayOfWeek.Monday, t.DayOfWeek));
        Assert.Equal(new DateTime(2026, 3, 2), ticks[0]);
    }

    [Fact]
    public void Quarterly_ticks_are_first_of_quarter()
    {
        var min = TimeValue.FromDateTime(new DateTime(2025, 2, 10));
        var max = TimeValue.FromDateTime(new DateTime(2026, 2, 10));

        var ticks = TimeTicks.Generate(min, max, new TimeInterval(TimeUnit.Month, 3)).Select(TimeValue.ToDateTime).ToList();

        Assert.Equal([new(2025, 4, 1), new(2025, 7, 1), new(2025, 10, 1), new(2026, 1, 1)], ticks);
    }

    [Fact]
    public void Interval_choice_follows_span()
    {
        var day = 86_400_000d;

        Assert.Equal(TimeUnit.Hour, TimeTicks.Choose(0, day, 8).Unit);
        Assert.Equal(TimeUnit.Month, TimeTicks.Choose(0, 365 * day, 12).Unit);
        Assert.Equal(TimeUnit.Year, TimeTicks.Choose(0, 3650 * day, 10).Unit);
    }

    [Fact]
    public void Time_labels_adapt_to_interval()
    {
        var noon = TimeValue.FromDateTime(new DateTime(2026, 3, 1, 12, 0, 0));
        var midnight = TimeValue.FromDateTime(new DateTime(2026, 3, 2));
        var march = TimeValue.FromDateTime(new DateTime(2026, 3, 1));
        var january = TimeValue.FromDateTime(new DateTime(2026, 1, 1));
        var hour = new TimeInterval(TimeUnit.Hour, 3);
        var month = new TimeInterval(TimeUnit.Month, 1);

        Assert.Equal("12:00", TimeFormatter.Format(noon, hour, Sv));
        Assert.Equal("Mar 2", TimeFormatter.Format(midnight, hour, En)); // day boundary shows the date
        Assert.Equal("Mar", TimeFormatter.Format(march, month, En));
        Assert.Equal("2026", TimeFormatter.Format(january, month, En)); // year boundary shows the year
    }

    [Fact]
    public void Large_numbers_are_compact()
    {
        var format = NumberFormatter.ForTicks([0, 500, 1000, 1500], 500, En);

        Assert.Equal("1.5k", format(1500));
        Assert.Equal("0", format(0));
    }

    [Fact]
    public void Millions_use_M()
    {
        var format = NumberFormatter.ForTicks([0, 2_000_000, 4_000_000], 2_000_000, En);

        Assert.Equal("4M", format(4_000_000));
    }

    [Fact]
    public void Small_numbers_get_decimals_from_step()
    {
        var format = NumberFormatter.ForTicks([0, 0.25, 0.5], 0.25, Sv);

        Assert.Equal("0,25", format(0.25));
    }

    [Fact]
    public void Custom_format_wins()
    {
        var format = NumberFormatter.ForTicks([0, 0.5], 0.5, En, "P0");

        Assert.Equal("50%", format(0.5).Replace(" ", "", StringComparison.Ordinal));
    }
}
