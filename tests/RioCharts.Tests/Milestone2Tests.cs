using System.Globalization;
using System.Text.Json;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Paths;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class Milestone2Tests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };

    private static ChartLayoutResult Layout(ChartSpec spec, Dictionary<string, SeriesData> data, LayoutOptions? options = null) =>
        ChartLayout.Compute(spec, data, options ?? Options);

    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static IEnumerable<Node> All(Scene scene) => All(scene.Static).Concat(All(scene.Series)).Concat(All(scene.Overlay));

    private static List<RectNode> Bars(ChartLayoutResult r, string id) =>
        All(r.Scene.Series).Where(n => n is GroupNode g && g.Key == "series:" + id).SelectMany(All).OfType<RectNode>().ToList();

    // ---------- Stacking ----------

    [Fact]
    public void Stacked_bars_extend_the_domain_to_the_sum_and_share_a_slot()
    {
        var spec = new ChartSpec
        {
            Series =
            [
                new SeriesSpec { Id = "a", Type = SeriesType.Bar, Stack = "s" },
                new SeriesSpec { Id = "b", Type = SeriesType.Bar, Stack = "s" },
            ],
        };
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(["Q1", "Q2"], [10.0, 20]),
            ["b"] = SeriesData.FromCategories(["Q1", "Q2"], [30.0, 40]),
        };

        var result = Layout(spec, data);
        var a = Bars(result, "a");
        var b = Bars(result, "b");

        Assert.True(result.YScale.DomainMax >= 60);
        Assert.Equal(a[0].Bounds.X, b[0].Bounds.X, 0.01); // same slot
        Assert.True(b[0].Bounds.Bottom <= a[0].Bounds.Y + 0.01); // b sits on top of a
    }

    [Fact]
    public void Hiding_a_stacked_series_closes_the_stack()
    {
        var spec = new ChartSpec
        {
            Series =
            [
                new SeriesSpec { Id = "a", Type = SeriesType.Area, Stack = "s" },
                new SeriesSpec { Id = "b", Type = SeriesType.Area, Stack = "s" },
            ],
        };
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.From([0.0, 1], [100.0, 100]),
            ["b"] = SeriesData.From([0.0, 1], [5.0, 5]),
        };

        var hidden = Layout(spec, data, Options with { HiddenSeries = new HashSet<string> { "a" } });

        Assert.True(hidden.YScale.DomainMax < 20);
    }

    [Fact]
    public void Negative_values_stack_downwards()
    {
        var spec = new ChartSpec
        {
            Series =
            [
                new SeriesSpec { Id = "a", Type = SeriesType.Bar, Stack = "s" },
                new SeriesSpec { Id = "b", Type = SeriesType.Bar, Stack = "s" },
            ],
        };
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.From([0.0], [10.0]),
            ["b"] = SeriesData.From([0.0], [-4.0]),
        };

        var result = Layout(spec, data);

        Assert.True(result.YScale.DomainMin <= -4);
        Assert.True(result.YScale.DomainMax >= 10 && result.YScale.DomainMax < 14);
    }

    [Fact]
    public void Area_between_two_lines_is_closed()
    {
        var path = LineGenerator.AreaBetween([0, 10, 20], [1, 2, 3], [5, 5, 5], CurveType.Monotone);

        Assert.Equal(PathCommand.MoveTo, path.Commands[0]);
        Assert.Equal(PathCommand.Close, path.Commands[^1]);
        Assert.Equal(1, path.Commands.Count(c => c == PathCommand.MoveTo));
    }

    // ---------- Multiple Y axes ----------

    [Fact]
    public void Series_on_a_second_axis_get_their_own_domain_on_the_right()
    {
        var spec = new ChartSpec
        {
            YAxes = [new AxisSpec { Id = "energy", Title = "kWh" }],
            Series =
            [
                new SeriesSpec { Id = "temp" },
                new SeriesSpec { Id = "kwh", Type = SeriesType.Bar, YAxisId = "energy" },
            ],
        };
        var data = new Dictionary<string, SeriesData>
        {
            ["temp"] = SeriesData.From([0.0, 1, 2], [20.0, 21, 22]),
            ["kwh"] = SeriesData.From([0.0, 1, 2], [500.0, 800, 900]),
        };

        var result = Layout(spec, data);

        Assert.True(result.YScales["y"].DomainMax < 30);
        Assert.True(result.YScales["energy"].DomainMax >= 900);
        var right = All(result.Scene.Static).OfType<GroupNode>().Single(g => g.Key == "axis:energy");
        Assert.All(right.Children.OfType<TextNode>(), t => Assert.True(t.At.X >= result.Plot.Right));

        // The right axis labels sit on the left axis' grid lines.
        var left = All(result.Scene.Static).OfType<GroupNode>().Single(g => g.Key == "axis:y");
        Assert.Equal(left.Children.OfType<TextNode>().Select(t => t.At.Y), right.Children.OfType<TextNode>().Select(t => t.At.Y));
    }

    [Fact]
    public void Unknown_axis_id_falls_back_with_a_warning()
    {
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a", YAxisId = "nope" }] };

        var result = Layout(spec, new() { ["a"] = SeriesData.From([0.0, 1], [1.0, 2]) });

        Assert.True(result.HasData);
        Assert.Contains(result.Warnings, w => w.Contains("axis 'nope' not found", StringComparison.Ordinal));
    }

    // ---------- Annotations ----------

    [Fact]
    public void Reference_line_expands_the_domain_but_a_band_does_not()
    {
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From([0.0, 1], [20.0, 22]) };
        var line = Layout(new ChartSpec
        {
            Series = [new SeriesSpec { Id = "a" }],
            Annotations = [new AnnotationSpec { Value = 30, Label = "Limit" }],
        }, data);
        var band = Layout(new ChartSpec
        {
            Series = [new SeriesSpec { Id = "a" }],
            Annotations = [new AnnotationSpec { Type = AnnotationType.Band, From = 40, To = 50 }],
        }, data);

        Assert.True(line.YScale.DomainMax >= 30);
        Assert.Contains(All(line.Scene), n => n is TextNode { Text: "Limit" });
        Assert.True(band.YScale.DomainMax < 30);
        Assert.DoesNotContain(All(band.Scene), n => n.Class == "rio-annotation-band"); // outside the range
    }

    [Fact]
    public void Time_annotations_accept_dates_and_iso_strings()
    {
        var start = new DateTime(2026, 3, 1);
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.From(Enumerable.Range(0, 48).Select(h => start.AddHours(h)).ToArray(), Enumerable.Range(0, 48).Select(h => (double)h).ToArray()),
        };
        var json = JsonSerializer.Deserialize<AnnotationSpec>("""{ "type": "Band", "axis": "X", "from": "2026-03-01T06:00", "to": "2026-03-01T09:00", "label": "Fault" }""", JsonSerializerOptions.Web)!;
        var spec = new ChartSpec
        {
            Series = [new SeriesSpec { Id = "a" }],
            Annotations = [json, new AnnotationSpec { Axis = AnnotationAxis.X, Value = start.AddHours(24), Id = "midnight" }],
        };

        var result = Layout(spec, data, Options with { InteractiveAnnotations = true });

        var band = All(result.Scene).OfType<RectNode>().Single(r => r.Class == "rio-annotation-band");
        var expectedLeft = result.XScale!.Map(TimeValue.FromDateTime(start.AddHours(6)));
        Assert.Equal(expectedLeft, band.Bounds.X, 0.5);
        var group = All(result.Scene).OfType<GroupNode>().Single(g => g.Key == "annotation:midnight");
        Assert.Equal("annotation", group.Interaction!.Kind);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Invalid_annotation_values_are_reported()
    {
        var spec = new ChartSpec
        {
            Series = [new SeriesSpec { Id = "a" }],
            Annotations = [new AnnotationSpec { Value = "warm" }],
        };

        var result = Layout(spec, new() { ["a"] = SeriesData.From([0.0, 1], [1.0, 2]) });

        Assert.Contains(result.Warnings, w => w.StartsWith("annotations[0].value", StringComparison.Ordinal));
    }

    // ---------- Data labels ----------

    [Fact]
    public void Data_labels_are_drawn_and_never_overlap()
    {
        var n = 60;
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a", DataLabels = true }] };
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.From(Enumerable.Range(0, n).Select(i => (double)i).ToArray(), Enumerable.Range(0, n).Select(i => 1000.0 + i).ToArray()),
        };

        var result = Layout(spec, data);
        var labels = All(result.Scene).Where(g => g is GroupNode { Class: "rio-data-labels" }).SelectMany(All).OfType<TextNode>().ToList();

        Assert.NotEmpty(labels);
        Assert.True(labels.Count < n); // crowded labels are dropped
        var rects = labels.Select(l =>
        {
            var w = Options.Measurer.Measure(l.Text, 11);
            return (Left: l.At.X - w / 2, Right: l.At.X + w / 2, l.At.Y, l.Baseline);
        }).ToList();
        for (var i = 1; i < rects.Count; i++)
        {
            var same = rects.Take(i).Where(r => r.Baseline == rects[i].Baseline && Math.Abs(r.Y - rects[i].Y) < 13);
            Assert.All(same, r => Assert.True(r.Right <= rects[i].Left || rects[i].Right <= r.Left));
        }
    }

    // ---------- Tooltip model ----------

    [Fact]
    public void Tooltip_model_has_formatted_values_per_drawn_point()
    {
        var start = new DateTime(2026, 3, 1, 8, 0, 0);
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a", Name = "London", Format = "0.0" }] };
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.From([start, start.AddMinutes(15)], [21.0, 21.25]),
        };

        var result = Layout(spec, data);
        var s = Assert.Single(result.Tooltip.Series);

        Assert.True(result.Tooltip.Enabled);
        Assert.Equal("London", s.Name);
        Assert.Equal(["21.0", "21.3"], s.Values);
        Assert.Equal("03/01/2026 08:15", s.XLabels[1]);
        Assert.True(s.Snap > 1);
    }

    [Fact]
    public void Grouped_bar_hits_share_the_x_position()
    {
        var spec = new ChartSpec
        {
            Series = [new SeriesSpec { Id = "a", Type = SeriesType.Bar }, new SeriesSpec { Id = "b", Type = SeriesType.Bar }],
        };
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(["Q1", "Q2"], [1.0, 2]),
            ["b"] = SeriesData.FromCategories(["Q1", "Q2"], [3.0, 4]),
        };

        var result = Layout(spec, data);
        var (a, b) = (result.Tooltip.Series[0], result.Tooltip.Series[1]);

        Assert.Equal(a.X, b.X);
        Assert.NotEqual(a.BarOffset, b.BarOffset);
        Assert.Equal(["Q1", "Q2"], a.XLabels);
        Assert.True(result.Tooltip.BandWidth > 0);
    }

    // ---------- Values ----------

    [Theory]
    [InlineData(1234.5, "1,234.5")]
    [InlineData(21.256, "21.26")]
    [InlineData(2_500_000, "2.5M")]
    [InlineData(-0.001, "0")]
    public void Values_are_formatted_compactly(double value, string expected) =>
        Assert.Equal(expected, RioCharts.Core.Formatting.ValueFormatter.Format(value, CultureInfo.InvariantCulture));

    [Fact]
    public void Axis_values_convert_from_common_types()
    {
        Assert.Equal(21, AxisValue.ToDouble(21m, XKind.Numeric));
        Assert.Equal(1, AxisValue.ToDouble("Q2", XKind.Category, ["Q1", "Q2"]));
        Assert.Equal(TimeValue.FromDateTime(new DateTime(2026, 3, 1, 6, 0, 0)), AxisValue.ToDouble("2026-03-01T06:00:00+02:00", XKind.Time));
        Assert.True(double.IsNaN(AxisValue.ToDouble("x", XKind.Numeric)));
    }
}
