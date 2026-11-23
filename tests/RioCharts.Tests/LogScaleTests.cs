using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Layout;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;
using RioCharts.Core.Definitions;

namespace RioCharts.Tests;

public class LogScaleTests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };

    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static IEnumerable<Node> All(Scene scene) => All(scene.Static).Concat(All(scene.Series)).Concat(All(scene.Overlay));

    private static ChartDefinition LogSpec(params SeriesOptions[] series) => new() { Y = new AxisOptions { Scale = ScaleType.Log }, Series = series };

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
        var scale = ContinuousScale.Log(1, 1000, 300, 0);

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
        var scale = ContinuousScale.Log(1, 1000, 300, 0);

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
        var scale = ContinuousScale.Log(min, max, 300, 0);

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
        var result = ChartLayout.Compute(LogSpec(new SeriesOptions { Id = "a" }), new Dictionary<string, SeriesData> { ["a"] = Numbers(3, 40, 900, 25_000, 410_000) }, Options);

        Assert.True(result.YScale.IsLog);
        Assert.Equal(1, result.YScale.DomainMin);
        Assert.Equal(1_000_000, result.YScale.DomainMax);
        Assert.Empty(result.Warnings);
        AssertFinite(result);
    }

    [Fact]
    public void Zero_and_negative_values_are_gaps_with_a_warning()
    {
        var spec = LogSpec(new SeriesOptions { Id = "a" });
        spec = spec with { Annotations = [new Annotation { Value = 0, Label = "Zero" }] };

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
        var spec = LogSpec(new SeriesOptions { Id = "a", Type = SeriesType.Bar, DataLabels = true });

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
        var spec = LogSpec(new SeriesOptions { Id = "a", Type = SeriesType.Area, Stack = "s" }, new SeriesOptions { Id = "b", Type = SeriesType.Area, Stack = "s" });

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
        var result = ChartLayout.Compute(LogSpec(new SeriesOptions { Id = "a" }), new Dictionary<string, SeriesData> { ["a"] = Numbers(values) }, Options);

        Assert.True(result.YScale.DomainMin > 0);
        Assert.True(result.YScale.DomainMax > result.YScale.DomainMin);
        AssertFinite(result);
    }

    [Fact]
    public void Min_and_max_are_kept_when_positive_and_ignored_otherwise()
    {
        var data = new Dictionary<string, SeriesData> { ["a"] = Numbers(3, 40, 900) };

        var set = ChartLayout.Compute(new ChartDefinition { Y = new AxisOptions { Scale = ScaleType.Log, Min = 2, Max = 5000 }, Series = [new SeriesOptions { Id = "a" }] }, data, Options);
        var zero = ChartLayout.Compute(new ChartDefinition { Y = new AxisOptions { Scale = ScaleType.Log, Min = 0 }, Series = [new SeriesOptions { Id = "a" }] }, data, Options);

        Assert.Equal((2d, 5000d), (set.YScale.DomainMin, set.YScale.DomainMax));
        Assert.Equal(1, zero.YScale.DomainMin);
    }

    // ---------- Log x axis ----------

    // Page load time in ms against page size in kB, which spans three powers of ten.
    private static readonly SeriesData Pages = SeriesData.From([12.0, 40, 85, 300, 950, 2400, 8000], [110.0, 140, 160, 260, 480, 900, 2300]);

    private static ChartDefinition LogX(params SeriesOptions[] series) => new()
    {
        X = new AxisOptions { Scale = ScaleType.Log },
        Series = series.Length > 0 ? series : [new SeriesOptions { Id = "a", Type = SeriesType.Scatter }],
    };

    [Fact]
    public void A_log_x_axis_rounds_out_to_powers_of_ten_and_spaces_ratios_evenly()
    {
        var result = ChartLayout.Compute(LogX(), new Dictionary<string, SeriesData> { ["a"] = Pages }, Options);

        var x = result.XScale!;
        Assert.True(x.IsLog);
        Assert.Equal((10.0, 10_000.0), (x.DomainMin, x.DomainMax));
        Assert.Equal(x.Map(100) - x.Map(10), x.Map(1000) - x.Map(100), 3); // each decade as wide
        Assert.Empty(result.Warnings);
        var labels = All(result.Scene.Static).OfType<GroupNode>().Single(g => g.Class == "rio-axis-x").Children.OfType<TextNode>().Select(t => t.Text).ToList();
        Assert.Contains("100", labels);
        Assert.Contains("1k", labels);
        Assert.Contains("10k", labels);
        AssertFinite(result);
    }

    [Fact]
    public void A_log_x_axis_keeps_a_positive_min_and_max()
    {
        var spec = LogX() with { X = new AxisOptions { Scale = ScaleType.Log, Min = 5, Max = 20_000 } };

        var x = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["a"] = Pages }, Options).XScale!;

        Assert.Equal((5.0, 20_000.0), (x.DomainMin, x.DomainMax));
    }

    [Fact]
    public void Points_at_x_zero_or_below_are_left_out_with_a_warning()
    {
        var data = SeriesData.From([-5.0, 0, 10, 100, 1000], [1.0, 2, 3, 4, 5]);

        var result = ChartLayout.Compute(LogX(new SeriesOptions { Id = "a" }), new Dictionary<string, SeriesData> { ["a"] = data }, Options);

        Assert.Contains(result.Warnings, w => w.Contains("2 point(s) at x zero or below", StringComparison.Ordinal));
        Assert.Equal(10, result.XScale!.DomainMin);
        Assert.Equal(3, result.HitTest.Series.Single().X.Length);
        AssertFinite(result);
    }

    [Fact]
    public void Lines_on_a_log_x_axis_and_a_log_y_axis_make_a_log_log_chart()
    {
        var spec = LogX(new SeriesOptions { Id = "a" }) with { Y = new AxisOptions { Scale = ScaleType.Log } };

        var result = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["a"] = Pages }, Options);

        Assert.True(result.XScale!.IsLog && result.YScale.IsLog);
        var hits = result.HitTest.Series.Single();
        Assert.Equal(result.XScale.Map(12), hits.X[0], 1);
        Assert.Equal(result.YScale.Map(110), hits.Y[0], 1);
    }

    [Fact]
    public void A_log_x_axis_is_linear_with_a_warning_for_bars_and_for_times()
    {
        var bars = ChartLayout.Compute(LogX(new SeriesOptions { Id = "a", Type = SeriesType.Bar }), new Dictionary<string, SeriesData> { ["a"] = Numbers(1, 2, 3) }, Options);
        var times = ChartLayout.Compute(LogX(new SeriesOptions { Id = "a" }),
            new Dictionary<string, SeriesData> { ["a"] = SeriesData.From([new DateTime(2026, 3, 1), new DateTime(2026, 3, 2)], [1.0, 2]) }, Options);

        Assert.Contains(bars.Warnings, w => w.StartsWith("x.scale: a log x axis cannot be used with bar-like series", StringComparison.Ordinal));
        Assert.False(bars.XScale!.IsLog);
        Assert.Contains(times.Warnings, w => w.StartsWith("x.scale: a log scale needs numbers", StringComparison.Ordinal));
        Assert.False(times.XScale!.IsLog);
    }

    [Fact]
    public void Annotations_on_a_log_x_axis_are_placed_by_ratio_and_left_out_at_zero()
    {
        var spec = LogX() with
        {
            Annotations =
            [
                new Annotation { Axis = AnnotationAxis.X, Value = 1000.0, Label = "1 MB", Draggable = true, Id = "mb" },
                new Annotation { Axis = AnnotationAxis.X, Value = 0.0, Label = "Zero" },
            ],
        };

        var result = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["a"] = Pages }, Options);

        var line = All(result.Scene).OfType<PathNode>().First(p => p.Class == "rio-annotation-line");
        Assert.Equal(result.XScale!.Map(1000), line.Path.Coords[0], 1);
        Assert.DoesNotContain(All(result.Scene), n => n is TextNode { Text: "Zero" });
        Assert.Contains(result.Warnings, w => w.Contains("x value of zero or below", StringComparison.Ordinal));
        var handle = Assert.Single(result.DragHandles);
        Assert.True(handle.Log && handle.IsX);
        Assert.Equal(result.XScale.Map(1000), handle.Pixel, 1); // at the line, not at the plot's top
        Assert.Equal(3, handle.DomainMax - 1, 6); // log10 units: 1 to 4
    }

    [Fact]
    public void A_zoomed_log_x_axis_shows_the_range_and_ignores_one_reaching_zero()
    {
        var zoomed = ChartLayout.Compute(LogX(), new Dictionary<string, SeriesData> { ["a"] = Pages }, Options with { XRange = (50, 500) });
        var invalid = ChartLayout.Compute(LogX(), new Dictionary<string, SeriesData> { ["a"] = Pages }, Options with { XRange = (0, 500) });

        Assert.Equal((50.0, 500.0), (zoomed.XScale!.DomainMin, zoomed.XScale.DomainMax));
        Assert.Equal(10, invalid.XScale!.DomainMin);
    }

    [Fact]
    public void A_draggable_line_on_a_log_axis_drags_in_log_units()
    {
        var spec = LogSpec(new SeriesOptions { Id = "a" }) with { Annotations = [new Annotation { Id = "limit", Value = 100, Draggable = true }] };

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
        var parsed = ChartDefinitionJson.Parse("""{ "y": { "scale": "log" }, "series": [{ "id": "a" }] }""");

        Assert.Empty(parsed.Errors);
        Assert.Equal(ScaleType.Log, parsed.Definition!.Y.Scale);
    }
}
