using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Skia;

namespace RioCharts.Tests;

/// <summary>Edge cases that threw or drew outside the chart (found in a review of the layouts).</summary>
public class LayoutEdgeCaseTests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };

    private static ChartLayoutResult Layout(ChartDefinition spec, SeriesData data, LayoutOptions? options = null) =>
        ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { [spec.Series[0].Id] = data }, options ?? Options);

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    private static IEnumerable<Node> Nodes(ChartLayoutResult layout) =>
        All<Node>(layout.Scene.Static).Concat(All<Node>(layout.Scene.Series)).Concat(All<Node>(layout.Scene.Overlay));

    [Fact]
    public void A_callout_wider_than_the_plot_is_shortened_not_a_crash()
    {
        var spec = new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "l" }],
            Annotations = [new Annotation { Type = AnnotationType.Callout, X = 1.0, Y = 2.0, Label = "A callout label that is clearly much wider than the whole plot area" }],
        };

        var layout = Layout(spec, SeriesData.From([0.0, 1, 2], [1.0, 2, 3]), Options with { Width = 300 });

        var label = Assert.Single(Nodes(layout).OfType<TextNode>(), t => t.Class == "rio-callout-label");
        Assert.EndsWith("…", label.Text, StringComparison.Ordinal);
        var box = Assert.Single(Nodes(layout).OfType<RectNode>(), r => r.Class == "rio-callout-box");
        Assert.True(box.Bounds.X >= layout.Plot.X && box.Bounds.Right <= layout.Plot.Right + 0.5f);
    }

    [Fact]
    public void A_bullet_with_fixed_limits_between_nice_steps_still_gets_ticks()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "b", Type = SeriesType.Bullet, Min = 5.1, Max = 9.3 }] };

        var layout = Layout(spec, SeriesData.FromCategories(["A"], [7.0]), Options with { Width = 200 });

        Assert.True(layout.HasData);
    }

    [Fact]
    public void A_null_field_name_in_json_is_an_error_not_a_crash()
    {
        var parsed = ChartDefinitionJson.Parse("""{"dataset":"d","series":[{"type":"bar","x":"region","y":"sales","filter":{"field":null,"op":"eq","value":"x"}}]}""");
        var rows = new List<IReadOnlyDictionary<string, object?>> { new Dictionary<string, object?> { ["region"] = "x", ["sales"] = 1.0 } };

        var bound = ChartDefinitionBinder.Bind(parsed.Definition!, new Dictionary<string, ChartDataset> { ["d"] = ChartDataset.FromRows("d", rows) });

        var error = Assert.Single(bound.Errors);
        Assert.Equal("series[0].filter[0].field", error.Path);
        Assert.Contains("available: ", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_y_min_above_every_value_keeps_a_usable_axis()
    {
        // Winter temperatures (°C) with the axis asked to start at 0.
        var spec = new ChartDefinition { Y = new AxisOptions { Min = 0 }, Series = [new SeriesOptions { Id = "l" }] };

        var layout = Layout(spec, SeriesData.From([0.0, 1, 2], [-5.0, -3, -1]));

        Assert.Equal(0, layout.YScale.DomainMin);
        Assert.True(layout.YScale.DomainMax > 0);
    }

    [Fact]
    public void A_y_max_below_every_value_keeps_a_usable_axis()
    {
        var spec = new ChartDefinition { Y = new AxisOptions { Max = 0 }, Series = [new SeriesOptions { Id = "l" }] };

        var layout = Layout(spec, SeriesData.From([0.0, 1, 2], [1.0, 3, 5]));

        Assert.Equal(0, layout.YScale.DomainMax);
        Assert.True(layout.YScale.DomainMin < 0);
    }

    [Fact]
    public void An_x_min_beyond_the_data_keeps_the_fixed_end()
    {
        var spec = new ChartDefinition { X = new AxisOptions { Min = 10 }, Series = [new SeriesOptions { Id = "l" }] };

        var layout = Layout(spec, SeriesData.From([0.0, 1, 2], [1.0, 2, 3]));

        Assert.Equal(10, layout.XScale!.DomainMin);
        Assert.True(layout.XScale.DomainMax > 10);
    }

    [Fact]
    public void Horizontal_bars_stay_in_the_plot_with_a_fixed_value_range()
    {
        var spec = new ChartDefinition { Horizontal = true, Y = new AxisOptions { Min = 50, Max = 80 }, Series = [new SeriesOptions { Id = "b", Type = SeriesType.Bar }] };

        var layout = Layout(spec, SeriesData.FromCategories(["A", "B"], [60.0, 100]));

        var plot = Assert.Single(All<GroupNode>(layout.Scene.Series), g => g.Class == "rio-plot");
        Assert.NotNull(plot.Clip);
        Assert.All(All<RectNode>(plot), r => Assert.True(r.Bounds.X >= layout.Plot.X - 0.5f && r.Bounds.Right <= layout.Plot.Right + 0.5f));
    }

    [Fact]
    public void Heatmap_cells_are_clipped_to_the_plot()
    {
        var spec = new ChartDefinition { X = new AxisOptions { Min = 12 }, Series = [new SeriesOptions { Id = "h", Type = SeriesType.Heatmap }] };

        var layout = Layout(spec, SeriesData.From([.. Enumerable.Range(0, 24).Select(h => (double)h)], [.. Enumerable.Range(0, 24).Select(h => (double)h)]));

        Assert.NotNull(Assert.Single(All<GroupNode>(layout.Scene.Series), g => g.Class == "rio-plot").Clip);
    }

    [Fact]
    public void A_funnel_with_many_stages_stays_in_the_chart()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "f", Type = SeriesType.Funnel }] };
        var names = Enumerable.Range(1, 30).Select(i => "Stage " + i).ToList();
        var data = new Dictionary<string, SeriesData> { ["f"] = SeriesData.FromCategories(names, [.. Enumerable.Range(1, 30).Select(i => 100.0 - i)]) };

        var svg = ChartExport.ToSvg(spec, data, new ExportOptions { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture });

        var ys = System.Text.RegularExpressions.Regex.Matches(svg, " y=\"([0-9.]+)\"").Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        Assert.True(ys.Max() <= 300, $"lowest y {ys.Max()}");
    }

    [Fact]
    public void A_radar_with_equal_limits_warns_and_uses_a_real_range()
    {
        var spec = new ChartDefinition { Y = new AxisOptions { Min = 5, Max = 5 }, Series = [new SeriesOptions { Id = "r", Type = SeriesType.Radar }] };

        var layout = Layout(spec, SeriesData.FromCategories(["A", "B", "C"], [5.0, 5, 5]));

        Assert.Contains(layout.Warnings, w => w.Contains("must be above min", StringComparison.Ordinal));
        var svg = ChartExport.ToSvg(spec, new Dictionary<string, SeriesData> { ["r"] = SeriesData.FromCategories(["A", "B", "C"], [5.0, 5, 5]) },
            new ExportOptions { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture });
        Assert.DoesNotContain("M0 0L0 0L0 0", svg, StringComparison.Ordinal); // NaN geometry is written as 0
    }

    [Fact]
    public void The_description_gives_times_in_the_display_zone()
    {
        // 11:00 UTC is 12:00 in Stockholm (UTC+1 in March).
        var spec = new ChartDefinition { TimeZone = "Europe/Stockholm", Series = [new SeriesOptions { Id = "l", Name = "Temperature" }] };
        var data = SeriesData.From([new DateTime(2026, 3, 1, 11, 0, 0, DateTimeKind.Utc), new DateTime(2026, 3, 1, 13, 0, 0, DateTimeKind.Utc)], [2.0, 5]);

        var layout = Layout(spec, data);

        Assert.Contains("12:00", layout.Scene.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("11:00", layout.Scene.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void The_description_writes_numeric_x_like_the_axis()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "l" }] };

        var layout = Layout(spec, SeriesData.From([12345.0, 23456], [1.0, 2]));

        Assert.Contains("12,345", layout.Scene.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("E+", layout.Scene.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Legend_items_say_hidden_in_the_chart_language()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "a", Name = "Temperatur" }, new SeriesOptions { Id = "b", Name = "Fukt" }] };
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From([0.0, 1], [1.0, 2]), ["b"] = SeriesData.From([0.0, 1], [3.0, 4]) };

        var layout = ChartLayout.Compute(spec, data, Options with { Culture = CultureInfo.GetCultureInfo("sv-SE"), HiddenSeries = new HashSet<string> { "a" } });

        var item = Assert.Single(All<GroupNode>(layout.Scene.Static), g => g.Key == "legend:a");
        Assert.Equal("Temperatur, dold", item.AriaLabel);
    }
}
