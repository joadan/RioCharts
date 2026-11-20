using RioCharts.Core.Data;

namespace RioCharts.Tests;

public class DataTests
{
    private sealed record Reading(DateTime Time, double? Value);

    [Fact]
    public void Items_with_selectors_become_columns()
    {
        var t0 = new DateTime(2026, 3, 1, 12, 0, 0);
        var items = new[] { new Reading(t0, 1), new Reading(t0.AddHours(1), null), new Reading(t0.AddHours(2), 3) };

        var data = SeriesData.FromItems(items, r => r.Time, r => r.Value);

        Assert.Equal(XKind.Time, data.Kind);
        Assert.Equal(3, data.Count);
        Assert.True(double.IsNaN(data.Y[1])); // null is a gap, not zero
        Assert.Equal(t0, TimeValue.ToDateTime(data.X[0]));
    }

    [Fact]
    public void Time_is_used_as_given_whatever_the_kind()
    {
        var local = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Local);
        var utc = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var offset = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.FromHours(5));

        Assert.Equal(TimeValue.FromDateTime(local), TimeValue.FromDateTime(utc));
        Assert.Equal(TimeValue.FromDateTime(utc), TimeValue.FromDateTimeOffset(offset));
    }

    [Fact]
    public void Unsorted_input_is_sorted_once_with_source_mapping()
    {
        var data = SeriesData.From([3.0, 1, 2], [30.0, 10, 20]);

        Assert.Equal([1.0, 2, 3], data.X);
        Assert.Equal([10.0, 20, 30], data.Y);
        Assert.Equal(0, data.ToSourceIndex(2));
        Assert.Equal(1, data.ToSourceIndex(0));
    }

    [Fact]
    public void Sorted_input_keeps_identity_mapping()
    {
        var data = SeriesData.From([1.0, 2, 3], [1.0, 2, 3]);

        Assert.Null(data.SourceIndex);
        Assert.Equal(2, data.ToSourceIndex(2));
    }

    [Fact]
    public void Strings_become_categories_in_first_appearance_order()
    {
        var items = new[] { ("b", 1.0), ("a", 2.0), ("b", 3.0) };

        var data = SeriesData.FromItems(items, i => i.Item1, i => i.Item2);

        Assert.Equal(XKind.Category, data.Kind);
        Assert.Equal(["b", "a"], data.Categories);
        Assert.Equal([0.0, 1, 0], data.X);
    }

    [Fact]
    public void Integer_and_decimal_x_values_are_numeric()
    {
        var data = SeriesData.FromItems(new[] { 1, 2, 3 }, i => i, i => i * 2);

        Assert.Equal(XKind.Numeric, data.Kind);
        Assert.Equal([1.0, 2, 3], data.X);
    }

    [Fact]
    public void Points_without_x_are_dropped()
    {
        var data = SeriesData.From([1.0, double.NaN, 3], [1.0, 2, 3]);

        Assert.Equal(2, data.Count);
        Assert.Equal(2, data.ToSourceIndex(1));
    }

    [Fact]
    public void Missing_y_values_are_padded_as_gaps()
    {
        var data = SeriesData.From([1.0, 2, 3], [1.0]);

        Assert.True(double.IsNaN(data.Y[2]));
    }

    [Fact]
    public void Lttb_keeps_ends_and_count()
    {
        var x = Enumerable.Range(0, 1000).Select(i => (double)i).ToArray();
        var y = x.Select(v => Math.Sin(v / 30)).ToArray();

        var kept = Lttb.Downsample(x, y, 0, x.Length, 100);

        Assert.Equal(100, kept.Length);
        Assert.Equal(0, kept[0]);
        Assert.Equal(999, kept[^1]);
        Assert.Equal(kept.Order(), kept);
    }

    [Fact]
    public void Lttb_keeps_gaps()
    {
        var x = Enumerable.Range(0, 1000).Select(i => (double)i).ToArray();
        var y = x.Select(v => v is >= 400 and < 500 ? double.NaN : Math.Sin(v)).ToArray();

        var kept = Lttb.Downsample(x, y, 0, x.Length, 100);

        Assert.Contains(kept, i => double.IsNaN(y[i]));
        Assert.Contains(kept, i => i < 400);
        Assert.Contains(kept, i => i >= 500);
    }

    [Fact]
    public void Lttb_keeps_a_spike()
    {
        var x = Enumerable.Range(0, 2000).Select(i => (double)i).ToArray();
        var y = x.Select(v => v == 1234 ? 100.0 : 0).ToArray();

        Assert.Contains(1234, Lttb.Downsample(x, y, 0, x.Length, 50));
    }
}
