using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Specs;

namespace RioCharts.Tests;

public class AggregationTests
{
    private sealed record Alarm(string? Store, double? Minutes);

    private sealed record Sample(object X, double Value);

    private static readonly Alarm[] Alarms =
    [
        new("Store 2", 30), new("Store 1", 10), new("Store 2", 20), new(null, 99), new("Store 1", null), new("Store 3", 5),
    ];

    [Fact]
    public void Groups_keep_first_appearance_order_and_map_back_to_items()
    {
        var data = SeriesData.FromGroups(Alarms, a => a.Store, g => g.Sum(a => a.Minutes ?? 0), out var groups);

        Assert.Equal(XKind.Category, data.Kind);
        Assert.Equal(["Store 2", "Store 1", "Store 3"], data.Categories);
        Assert.Equal([50.0, 10, 5], data.Y);
        Assert.Equal([0, 2], groups[data.ToSourceIndex(0)]); // null x is left out
        Assert.Equal([1, 4], groups[data.ToSourceIndex(1)]);
    }

    [Fact]
    public void Numbers_group_by_value_and_are_sorted()
    {
        Sample[] items = [new(2, 1), new(1.0, 2), new(1, 3), new(2L, 4)];

        var data = SeriesData.FromGroups(items, s => s.X, g => g.Sum(s => s.Value), out var groups);

        Assert.Equal(XKind.Numeric, data.Kind);
        Assert.Equal([1.0, 2], data.X);
        Assert.Equal([5.0, 5], data.Y);
        Assert.Equal([1, 2], groups[data.ToSourceIndex(0)]);
    }

    [Fact]
    public void Times_group_on_the_exact_value()
    {
        var t = new DateTime(2026, 11, 16, 8, 0, 0);
        Sample[] items = [new(t, 1), new(t.AddMilliseconds(1), 2), new(t, 3)];

        var data = SeriesData.FromGroups(items, s => s.X, Aggregation.For<Sample>(Aggregate.Max, s => s.Value));

        Assert.Equal(XKind.Time, data.Kind);
        Assert.Equal(2, data.Count);
        Assert.Equal(3, data.Y[0]);
    }

    [Theory]
    [InlineData(Aggregate.Sum, 40.0)]
    [InlineData(Aggregate.Average, 20.0)]
    [InlineData(Aggregate.Min, 10.0)]
    [InlineData(Aggregate.Max, 30.0)]
    [InlineData(Aggregate.Last, 30.0)]
    [InlineData(Aggregate.Count, 4.0)]
    public void Built_in_aggregates_skip_missing_values_except_count(Aggregate aggregate, double expected)
    {
        Assert.Equal(expected, Aggregation.Apply(aggregate, [10.0, null, 30.0, double.NaN]));
    }

    [Fact]
    public void A_group_without_values_is_a_gap_and_count_needs_no_y()
    {
        Assert.Null(Aggregation.Apply(Aggregate.Sum, [null, null]));
        Assert.Equal(2, Aggregation.For<Alarm>(Aggregate.Count, null)([Alarms[0], Alarms[1]]));
        Assert.Null(Aggregation.For<Alarm>(Aggregate.Sum, null)([Alarms[0]]));

        var data = SeriesData.FromGroups(Alarms, a => a.Store, Aggregation.For<Alarm>(Aggregate.Average, a => a.Minutes));
        Assert.Equal(10, data.Y[1]); // Store 1: 10 and null
    }

    [Fact]
    public void Empty_items_give_empty_data()
    {
        var data = SeriesData.FromGroups(Array.Empty<Alarm>(), a => a.Store, g => g.Count, out var groups);

        Assert.Equal(0, data.Count);
        Assert.Empty(groups);
    }

    [Fact]
    public void Layout_warns_about_repeated_categories_and_bar_x_but_not_line_steps()
    {
        var options = new LayoutOptions { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };
        var categories = SeriesData.FromCategories(["A", "B", "A"], [1.0, 2, 3]);
        var times = SeriesData.From([0.0, 1, 1, 2], [1.0, 2, 3, 4]);

        var bars = ChartLayout.Compute(new ChartSpec { Series = [new SeriesSpec { Id = "b", Type = SeriesType.Bar }] }, new Dictionary<string, SeriesData> { ["b"] = categories }, options);
        var numericBars = ChartLayout.Compute(new ChartSpec { Series = [new SeriesSpec { Id = "b", Type = SeriesType.Bar }] }, new Dictionary<string, SeriesData> { ["b"] = times }, options);
        var line = ChartLayout.Compute(new ChartSpec { Series = [new SeriesSpec { Id = "l" }] }, new Dictionary<string, SeriesData> { ["l"] = times }, options);
        var grouped = ChartLayout.Compute(new ChartSpec { Series = [new SeriesSpec { Id = "b", Type = SeriesType.Bar }] },
            new Dictionary<string, SeriesData> { ["b"] = SeriesData.FromGroups(Alarms, a => a.Store, g => g.Count) }, options);

        Assert.Contains(bars.Warnings, w => w.Contains("x value 'A' appears more than once", StringComparison.Ordinal));
        Assert.Contains(numericBars.Warnings, w => w.Contains("x value 1 appears more than once", StringComparison.Ordinal));
        Assert.DoesNotContain(line.Warnings, w => w.Contains("more than once", StringComparison.Ordinal));
        Assert.DoesNotContain(grouped.Warnings, w => w.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void Razor_export_writes_the_aggregate_as_a_parameter()
    {
        var spec = ChartSpecJson.Parse("""
            { "series": [ { "type": "bar", "x": "store", "aggregate": "count" },
                          { "type": "bar", "x": "store", "y": "minutes", "aggregate": "sum" },
                          { "type": "bar", "x": "time", "y": "energy", "aggregate": "sum", "timeBucket": "day" } ] }
            """).Spec!;

        var razor = ChartRazor.FromSpec(spec, "Alarm", "alarms");

        Assert.Contains("<BarSeries XValue=\"r => r.Store\" Aggregate=\"Aggregate.Count\" />", razor);
        Assert.Contains("<BarSeries XValue=\"r => r.Store\" YValue=\"r => r.Minutes\" Aggregate=\"Aggregate.Sum\" />", razor);
        Assert.Contains("per day; aggregate: sum", razor);
        Assert.Contains("<BarSeries XValue=\"r => r.Time\" YValue=\"r => r.Energy\" />", razor);
    }
}
