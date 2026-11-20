using RioCharts.Layout;

namespace RioCharts.Tests;

public class NiceScaleTests
{
    [Theory]
    [InlineData(100, 6, 20)]
    [InlineData(10, 6, 2)]
    [InlineData(1, 6, 0.2)]
    [InlineData(7, 6, 2)]
    [InlineData(55, 6, 20)]
    [InlineData(12, 5, 5)]
    public void Step_picks_nice_values(double span, int ticks, double expected)
    {
        Assert.Equal(expected, NiceScale.Step(span, ticks), 10);
    }

    [Fact]
    public void Ticks_are_rounded_multiples_of_step()
    {
        var ticks = NiceScale.Ticks(0, 1, 0.1);

        Assert.Equal(11, ticks.Count);
        Assert.Equal(0.3, ticks[3]); // not 0.30000000000000004
        Assert.Equal(1, ticks[^1]);
    }

    [Fact]
    public void Ticks_start_at_first_multiple_inside_range()
    {
        var ticks = NiceScale.Ticks(3, 17, 5);

        Assert.Equal([5, 10, 15], ticks);
    }

    [Fact]
    public void Ticks_never_contain_negative_zero()
    {
        var ticks = NiceScale.Ticks(-1, 1, 0.5);

        Assert.Contains(0.0, ticks);
        Assert.All(ticks, t => Assert.False(double.IsNegative(t) && t == 0));
    }

    [Fact]
    public void Monthly_date_ticks_land_on_first_of_month()
    {
        var min = new DateTime(2026, 1, 1).ToOADate();
        var max = new DateTime(2026, 4, 15).ToOADate();

        var ticks = NiceScale.DateTicks(min, max, 30).Select(DateTime.FromOADate).ToList();

        Assert.Equal([new(2026, 1, 1), new(2026, 2, 1), new(2026, 3, 1), new(2026, 4, 1)], ticks);
    }

    [Fact]
    public void Quarterly_date_ticks_skip_months()
    {
        var min = new DateTime(2025, 1, 10).ToOADate();
        var max = new DateTime(2026, 1, 10).ToOADate();

        var ticks = NiceScale.DateTicks(min, max, 91).Select(DateTime.FromOADate).ToList();

        Assert.All(ticks, t => Assert.Equal(1, t.Day));
        Assert.All(ticks, t => Assert.Equal(0, (t.Month - 1) % 3));
        Assert.Equal(4, ticks.Count); // Apr, Jul, Oct 2025, Jan 2026
    }

    [Fact]
    public void DateStep_picks_calendar_friendly_steps()
    {
        Assert.Equal(1, NiceScale.DateStep(5, 6));    // 5 days -> 1 day
        Assert.Equal(7, NiceScale.DateStep(30, 6));   // a month -> weekly
        Assert.Equal(1.0 / 24, NiceScale.DateStep(0.2, 6), 10); // ~5 hours -> hourly
    }
}
