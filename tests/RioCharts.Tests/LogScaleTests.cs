using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Layout;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;
using RioCharts.Core.Specs;

namespace RioCharts.Tests;

public class LogScaleTests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };

    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static IEnumerable<Node> All(Scene scene) => All(scene.Static).Concat(All(scene.Series)).Concat(All(scene.Overlay));

    private static ChartSpec LogSpec(params SeriesSpec[] series) => new() { Y = new AxisSpec { Scale = ScaleType.Log }, Series = series };

    private static SeriesData Numbers(params double[] y) => SeriesData.From(Enumerable.Range(0, y.Length).Select(i => (double)i).ToArray(), y);

    /// <summary>Every coordinate and rectangle in the scene is a finite number (no NaN or infinity reaches the SVG).</summary>
    private static void AssertFinite(ChartLayoutResult result)
    {
        foreach (var node in All(result.Scene))
        {
            switch (node)
            {
                case PathNode p:
                    Assert.All(p.Path.Coords, c => Assert.True(float.IsFinite(c)));
                    break;
                case RectNode r:
                    Assert.True(float.IsFinite(r.Bounds.Y) && float.IsFinite(r.Bounds.Height), $"rect {r.Bounds}");
                    break;
            }
        }
    }

    // ---------- Scale ----------

    [Fact]
    public void Log_scale_gives_equal_ratios_equal_distances()
    {
        var scale = LinearScale.Log(1, 1000, 300, 0);

        Assert.True(scale.IsLog);
        Assert.Equal(300, scale.Map(1), 6);
        Assert.Equal(200, scale.Map(10), 6);
        Assert.Equal(100, scale.Map(100), 6);
        Assert.Equal(0, scale.Map(1000), 6);
        Assert.Equal(100, scale.Invert(scale.Map(100)), 6);
    }

    [Fact]
    public void Log_scale_puts_zero_and_below_at_the_low_end()
    {
        var scale = LinearScale.Log(1, 1000, 300, 0);

        Assert.Equal(300, scale.Map(0));
        Assert.Equal(300, scale.Map(-5));
        Assert.True(double.IsNaN(scale.Map(double.NaN)));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-3, 100)]
    [InlineData(5, 5)]
    [InlineData(double.NaN, double.NaN)]
    public void Log_scale_widens_an_invalid_domain(double min, double max)
    {
        var scale = LinearScale.Log(min, max, 300, 0);

        Assert.True(scale.DomainMin > 0);
        Assert.True(scale.DomainMax > scale.DomainMin);
        Assert.True(double.IsFinite(scale.Map(scale.DomainMin)));
    }

    // ---------- Ticks and labels ----------

    [Fact]
    public void Log_domain_rounds_out_to_powers_of_ten()
    {
        Assert.Equal((1d, 1000d), NiceTicks.LogDomain(3, 420));
        Assert.Equal((0.01, 1d), NiceTicks.LogDomain(0.02, 0.7));
        Assert.Equal((10d, 100d), NiceTicks.LogDomain(10, 10));
        Assert.Equal((1d, 10d), NiceTicks.LogDomain(0, 10));
    }

    [Fact]
    public void Log_ticks_are_powers_of_ten_with_2_and_5_for_short_ranges()
    {
        Assert.Equal([1, 2, 5, 10, 20, 50, 100], NiceTicks.LogTicks(1, 100, 6));
        Assert.Equal([1, 10, 100, 1000], NiceTicks.LogTicks(1, 1000, 4));
        Assert.Equal([1, 100, 10_000, 1_000_000], NiceTicks.LogTicks(1, 1e6, 4)); // every other power of ten
        Assert.Equal([0.001, 0.002, 0.005, 0.01], NiceTicks.LogTicks(0.001, 0.01, 5));
    }

    [Fact]
    public void Log_tick_labels_are_formatted_one_by_one()
    {
        var format = NumberFormatter.ForLogTicks(CultureInfo.InvariantCulture);

        Assert.Equal(["0.01", "0.5", "1", "500", "1k", "20k", "5M"], new[] { 0.01, 0.5, 1, 500, 1000, 20_000, 5e6 }.Select(format));
        Assert.Equal("1.000", NumberFormatter.ForLogTicks(CultureInfo.InvariantCulture, "N3")(1));
    }

    // ---------- Layout ----------

    [Fact]
    public void Log_axis_spans_the_data_in_powers_of_ten()
    {
        var result = ChartLayout.Compute(LogSpec(new SeriesSpec { Id = "a" }), new Dictionary<string, SeriesData> { ["a"] = Numbers(3, 40, 900, 25_000, 410_000) }, Options);

        Assert.True(result.YScale.IsLog);
        Assert.Equal(1, result.YScale.DomainMin);
        Assert.Equal(1_000_000, result.YScale.DomainMax);
        Assert.Empty(result.Warnings);
        AssertFinite(result);
    }

    [Fact]
    public void Zero_and_negative_values_are_gaps_with_a_warning()
    {
        var spec = LogSpec(new SeriesSpec { Id = "a" });
        spec = spec with { Annotations = [new AnnotationSpec { Value = 0, Label = "Zero" }] };

        var result = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["a"] = Numbers(5, 0, 50, -2, 500) }, Options);

        Assert.Contains(result.Warnings, w => w.Contains("2 value(s) of zero or below", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.StartsWith("annotation", StringComparison.Ordinal));
        Assert.DoesNotContain(All(result.Scene), n => n is TextNode { Text: "Zero" });
        Assert.Equal(1, result.YScale.DomainMin);
        AssertFinite(result);
    }

    [Fact]
    public void Bars_on_a_log_axis_start_at_its_bottom()
    {
        var spec = LogSpec(new SeriesSpec { Id = "a", Type = SeriesType.Bar, DataLabels = true });

        var result = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["a"] = SeriesData.FromCategories(["a", "b", "c"], [8, 300, 9_000]) }, Options);
        var bars = All(result.Scene.Series).OfType<RectNode>().ToList();

        Assert.Equal(3, bars.Count);
        Assert.All(bars, b => Assert.Equal(result.Plot.Bottom, b.Bounds.Bottom, 1));
        Assert.Equal(1, result.YScale.DomainMin); // no zero on a log axis
        AssertFinite(result);
    }

    [Fact]
    public void Stacked_areas_on_a_log_axis_render()
    {
        var spec = LogSpec(new SeriesSpec { Id = "a", Type = SeriesType.Area, Stack = "s" }, new SeriesSpec { Id = "b", Type = SeriesType.Area, Stack = "s" });

        var result = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["a"] = Numbers(5, 50, 500), ["b"] = Numbers(10, 100, 1000) }, Options);

        Assert.Empty(result.Warnings);
        AssertFinite(result);
    }

    [Theory]
    [InlineData(new double[0])]
    [InlineData(new[] { 42.0 })]
    [InlineData(new[] { 7.0, 7, 7 })]
    [InlineData(new[] { 0.0, -1 })]
    public void Edge_cases_on_a_log_axis_render_cleanly(double[] values)
    {
        var result = ChartLayout.Compute(LogSpec(new SeriesSpec { Id = "a" }), new Dictionary<string, SeriesData> { ["a"] = Numbers(values) }, Options);

        Assert.True(result.YScale.DomainMin > 0);
        Assert.True(result.YScale.DomainMax > result.YScale.DomainMin);
        AssertFinite(result);
    }

    [Fact]
    public void Min_and_max_are_kept_when_positive_and_ignored_otherwise()
    {
        var data = new Dictionary<string, SeriesData> { ["a"] = Numbers(3, 40, 900) };

        var set = ChartLayout.Compute(new ChartSpec { Y = new AxisSpec { Scale = ScaleType.Log, Min = 2, Max = 5000 }, Series = [new SeriesSpec { Id = "a" }] }, data, Options);
        var zero = ChartLayout.Compute(new ChartSpec { Y = new AxisSpec { Scale = ScaleType.Log, Min = 0 }, Series = [new SeriesSpec { Id = "a" }] }, data, Options);

        Assert.Equal((2d, 5000d), (set.YScale.DomainMin, set.YScale.DomainMax));
        Assert.Equal(1, zero.YScale.DomainMin);
    }

    [Fact]
    public void A_log_x_axis_is_linear_with_a_warning()
    {
        var spec = new ChartSpec { X = new AxisSpec { Scale = ScaleType.Log }, Series = [new SeriesSpec { Id = "a" }] };

        var result = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["a"] = Numbers(1, 2, 3) }, Options);

        Assert.Contains(result.Warnings, w => w.StartsWith("x.scale", StringComparison.Ordinal));
        Assert.False(result.XScale!.IsLog);
    }

    [Fact]
    public void A_draggable_line_on_a_log_axis_drags_in_log_units()
    {
        var spec = LogSpec(new SeriesSpec { Id = "a" }) with { Annotations = [new AnnotationSpec { Id = "limit", Value = 100, Draggable = true }] };

        var result = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["a"] = Numbers(3, 40, 900) }, Options);
        var handle = Assert.Single(result.DragHandles);

        Assert.True(handle.Log);
        Assert.Equal((0d, 3d), (handle.DomainMin, handle.DomainMax));
        var value = Math.Pow(10, handle.DomainMin + (handle.Pixel - handle.RangeStart) / (handle.RangeEnd - handle.RangeStart) * (handle.DomainMax - handle.DomainMin));
        Assert.Equal(100, value, 3);
    }

    [Fact]
    public void Json_specs_take_a_log_scale()
    {
        var parsed = ChartSpecJson.Parse("""{ "y": { "scale": "log" }, "series": [{ "id": "a" }] }""");

        Assert.Empty(parsed.Errors);
        Assert.Equal(ScaleType.Log, parsed.Spec!.Y.Scale);
    }
}
