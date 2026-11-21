using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Specs;

namespace RioCharts.Tests;

public class RangeAreaTests
{
    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static readonly LayoutOptions Options = new() { Width = 640, Height = 300, Culture = CultureInfo.InvariantCulture };

    private static readonly DateTime Start = new(2026, 11, 16);

    private static int MoveTos(PathData path) => path.Commands.Count(c => c == PathCommand.MoveTo);

    private static ChartSpec Spec(string? stack = null) => new()
    {
        Series = [new SeriesSpec { Id = "r", Name = "Range", Type = SeriesType.RangeArea, Format = "0", Stack = stack }],
    };

    /// <summary>Low 10 12 8 9, high 20 24 18 21.</summary>
    private static SeriesData Band() =>
        SeriesData.From(Enumerable.Range(0, 4).Select(i => Start.AddDays(i)).ToArray(), [20.0, 24, 18, 21]).WithLow([10.0, 12, 8, 9]);

    private static ChartLayoutResult Layout(SeriesData data, ChartSpec? spec = null) =>
        ChartLayout.Compute(spec ?? Spec(), new Dictionary<string, SeriesData> { ["r"] = data }, Options);

    [Fact]
    public void The_band_fills_between_low_and_high()
    {
        var layout = Layout(Band());

        var band = All(layout.Scene.Series).OfType<PathNode>().Single(n => n.Class == "rio-range");
        Assert.Equal(1, MoveTos(band.Path));
        Assert.True(layout.YScale.DomainMin <= 8 && layout.YScale.DomainMax >= 24); // the lowest low and the highest high
        Assert.DoesNotContain(All(layout.Scene.Series), n => n.Class == "rio-line"); // no outline
    }

    [Fact]
    public void The_tooltip_shows_low_to_high()
    {
        var layout = Layout(Band());

        Assert.Equal(["10 – 20", "12 – 24", "8 – 18", "9 – 21"], layout.Tooltip.Series[0].Values);
        Assert.Equal("Range: 8 to 24, last 9 – 21", layout.Legend[0].Tooltip);
    }

    [Fact]
    public void A_missing_end_is_a_gap()
    {
        var data = SeriesData.From(Enumerable.Range(0, 5).Select(i => Start.AddDays(i)).ToArray(), [20.0, 24, 18, double.NaN, 22])
            .WithLow([10.0, 12, double.NaN, 9, 11]);

        var band = All(Layout(data).Scene.Series).OfType<PathNode>().Single(n => n.Class == "rio-range");

        Assert.Equal(2, MoveTos(band.Path)); // 0-1, then 4 alone is a lone point (no area), so two pieces at most
    }

    [Fact]
    public void Low_values_follow_their_points_through_sorting_and_time_zones()
    {
        // Unordered input: the low of input item i stays with item i after sorting by x.
        var items = new[] { (Day: 2, Low: 8.0, High: 18.0), (Day: 0, Low: 10.0, High: 20.0), (Day: 1, Low: 12.0, High: 24.0) };
        var data = SeriesData.FromItems(items, i => Start.AddDays(i.Day), i => i.High).WithLow(items.Select(i => i.Low).ToArray());

        Assert.Equal([10.0, 12, 8], data.Low!);
        var zoned = ChartLayout.Compute(Spec() with { TimeZone = "Europe/Stockholm" }, new Dictionary<string, SeriesData> { ["r"] = data }, Options);
        Assert.Equal(["10 – 20", "12 – 24", "8 – 18"], zoned.Tooltip.Series[0].Values);
    }

    [Fact]
    public void Without_low_values_nothing_is_drawn_and_there_is_a_warning()
    {
        var layout = Layout(SeriesData.From([1.0, 2], [3.0, 4]));

        Assert.Contains(layout.Warnings, w => w.Contains("needs low values", StringComparison.Ordinal));
        Assert.DoesNotContain(All(layout.Scene.Series), n => n.Class == "rio-range");
    }

    [Fact]
    public void Ranges_are_never_stacked()
    {
        var layout = Layout(Band(), Spec(stack: "s"));

        Assert.Equal(["10 – 20", "12 – 24", "8 – 18", "9 – 21"], layout.Tooltip.Series[0].Values);
    }

    [Fact]
    public void A_json_spec_reads_the_low_field_and_keeps_the_extremes_when_aggregating()
    {
        var rows = Enumerable.Range(0, 48).Select(h => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["time"] = Start.AddHours(h), ["min"] = 10.0 + h % 24, ["max"] = 30.0 + h % 24,
        });
        var datasets = new Dictionary<string, ChartDataset> { ["d"] = ChartDataset.FromRows("d", rows) };
        var parsed = ChartSpecJson.Parse("""{ "dataset": "d", "series": [ { "type": "range", "x": "time", "low": "min", "y": "max", "timeBucket": "day" } ] }""");

        var bound = ChartSpecBinder.Bind(parsed.Spec!, datasets);

        Assert.Empty(bound.Errors);
        var data = bound.Data.Values.Single();
        Assert.Equal([10.0, 10], data.Low!); // the lowest low per day (not the average)
        Assert.Equal([53.0, 53], data.Y);    // the highest high
        Assert.Contains(" LowValue=\"r => r.Min\"", ChartRazor.FromSpec(parsed.Spec!, "Row", "rows"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_json_range_without_low_explains_what_is_missing()
    {
        var rows = new[] { (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["time"] = Start, ["max"] = 3.0 } };
        var parsed = ChartSpecJson.Parse("""{ "dataset": "d", "series": [ { "type": "rangeArea", "x": "time", "y": "max" } ] }""");

        var bound = ChartSpecBinder.Bind(parsed.Spec!, new Dictionary<string, ChartDataset> { ["d"] = ChartDataset.FromRows("d", rows) });

        var error = Assert.Single(bound.Errors);
        Assert.Equal("series[0].low", error.Path);
    }
}
