using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class SelectionTests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };

    private static readonly Dictionary<string, SeriesData> Line = new()
    {
        ["a"] = SeriesData.From([0.0, 1, 2, 3, 4], [1.0, 3, double.NaN, 2, 5]),
    };

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    private static GroupNode? Group(Scene scene, string key) => All<GroupNode>(scene.Series).FirstOrDefault(g => g.Key == key);

    [Fact]
    public void Selected_range_is_a_band_across_the_plot()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "a" }] };
        var layout = ChartLayout.Compute(spec, Line, Options with { SelectedRange = (3, 1) }); // either order

        var band = All<RectNode>(Group(layout.Scene, "selection:range")!).Single();
        Assert.Equal(layout.XScale!.Map(1), band.Bounds.X, 1);
        Assert.Equal(layout.XScale.Map(3), band.Bounds.Right, 1);
        Assert.Equal(layout.Plot.Y, band.Bounds.Y);
        Assert.Equal(layout.Plot.Height, band.Bounds.Height);
    }

    [Fact]
    public void Selected_range_outside_the_view_or_without_data_draws_nothing()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "a" }] };

        var zoomed = ChartLayout.Compute(spec, Line, Options with { XRange = (0, 1), SelectedRange = (3, 4) });
        var empty = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["a"] = SeriesData.From(ReadOnlySpan<double>.Empty, ReadOnlySpan<double>.Empty) }, Options with { SelectedRange = (0, 1) });
        var nan = ChartLayout.Compute(spec, Line, Options with { SelectedRange = (double.NaN, 1) });

        Assert.Null(Group(zoomed.Scene, "selection:range"));
        Assert.Null(Group(empty.Scene, "selection:range"));
        Assert.Null(Group(nan.Scene, "selection:range"));
    }

    [Fact]
    public void Selected_points_get_markers_except_gaps_and_unknown_indexes()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "a" }] };
        var selected = new Dictionary<string, IReadOnlyCollection<int>> { ["a"] = [4, 1, 2, 99, -1, 1], ["nope"] = [0] };

        var layout = ChartLayout.Compute(spec, Line, Options with { SelectedPoints = selected });

        var marks = All<PathNode>(Group(layout.Scene, "selection:points")!);
        Assert.Equal(2, marks.Count); // 1 and 4; 2 is a gap
        Assert.All(marks, m => Assert.Equal("rio-selected-point", m.Class));
    }

    [Fact]
    public void Selected_points_of_hidden_series_are_not_drawn()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "a" }] };
        var selected = new Dictionary<string, IReadOnlyCollection<int>> { ["a"] = [0] };

        var layout = ChartLayout.Compute(spec, Line, Options with { SelectedPoints = selected, HiddenSeries = new HashSet<string> { "a" } });

        Assert.Null(Group(layout.Scene, "selection:points"));
    }

    [Fact]
    public void Selected_stacked_bar_is_outlined_around_its_own_segment()
    {
        var spec = new ChartDefinition
        {
            Series =
            [
                new SeriesOptions { Id = "a", Type = SeriesType.Bar, Stack = "s" },
                new SeriesOptions { Id = "b", Type = SeriesType.Bar, Stack = "s" },
            ],
        };
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.FromCategories(["x", "y"], [10.0, 20]),
            ["b"] = SeriesData.FromCategories(["x", "y"], [5.0, 5]),
        };

        var layout = ChartLayout.Compute(spec, data, Options with { SelectedPoints = new Dictionary<string, IReadOnlyCollection<int>> { ["b"] = [0] } });

        var outline = All<RectNode>(Group(layout.Scene, "selection:points")!).Single();
        var y = layout.YScale;
        // From the top of "a" (10) to the top of the stack (15), plus the outline's margin.
        Assert.Equal(y.Map(15) - 1.5, outline.Bounds.Y, 1);
        Assert.Equal(y.Map(10) + 1.5, outline.Bounds.Y + outline.Bounds.Height, 1);
    }

    private static Dictionary<string, IReadOnlyCollection<int>> Pick(string id, params int[] indexes) => new() { [id] = indexes };

    private static readonly string[] Products = ["Trail shoe", "Rain jacket", "Headlamp"];

    [Fact]
    public void Selected_horizontal_bar_is_outlined_around_its_bar()
    {
        var spec = new ChartDefinition { Horizontal = true, Series = [new SeriesOptions { Id = "r", Type = SeriesType.Bar }] };
        var layout = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["r"] = SeriesData.FromCategories(Products, [120, 90, 60]) },
            Options with { SelectedPoints = Pick("r", 1) });

        var bar = All<RectNode>(Group(layout.Scene, "series:r")!)[1];
        var outline = All<RectNode>(Group(layout.Scene, "selection:points")!).Single();
        Assert.Equal(bar.Bounds.X - 1.5, outline.Bounds.X, 1);
        Assert.Equal(bar.Bounds.Right + 1.5, outline.Bounds.Right, 1);
        Assert.Equal(bar.Bounds.Y - 1.5, outline.Bounds.Y, 1);
        Assert.Equal(bar.Bounds.Bottom + 1.5, outline.Bounds.Bottom, 1);
    }

    [Fact]
    public void Selected_horizontal_waterfall_step_is_outlined()
    {
        var spec = new ChartDefinition { Horizontal = true, Series = [new SeriesOptions { Id = "w", Type = SeriesType.Waterfall }] };
        var data = SeriesData.FromCategories(["Revenue", "Costs", "Result"], [420.0, -180, double.NaN]).WithTotals([false, false, true]);

        var layout = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["w"] = data }, Options with { SelectedPoints = Pick("w", 1) });

        var step = All<RectNode>(Group(layout.Scene, "series:w")!).Where(n => n.Class == "rio-waterfall").ElementAt(1);
        var outline = All<RectNode>(Group(layout.Scene, "selection:points")!).Single();
        Assert.Equal(step.Bounds.X - 1.5, outline.Bounds.X, 1);
        Assert.Equal(step.Bounds.Right + 1.5, outline.Bounds.Right, 1);
    }

    [Fact]
    public void Selected_heatmap_cell_is_outlined_inside_the_cell()
    {
        var spec = new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "Mon", Type = SeriesType.Heatmap }, new SeriesOptions { Id = "Tue", Type = SeriesType.Heatmap }],
        };
        var data = new Dictionary<string, SeriesData>
        {
            ["Mon"] = SeriesData.FromCategories(["08", "12", "16"], [10, 20, 30]),
            ["Tue"] = SeriesData.FromCategories(["08", "12", "16"], [40, 50, 60]),
        };

        var layout = ChartLayout.Compute(spec, data, Options with { SelectedPoints = Pick("Tue", 2) });

        var cell = All<RectNode>(Group(layout.Scene, "series:heatmap")!)[5]; // Tue 16
        var outline = All<RectNode>(Group(layout.Scene, "selection:points")!).Single();
        Assert.True(outline.Bounds.X > cell.Bounds.X && outline.Bounds.Right < cell.Bounds.Right);
        Assert.True(outline.Bounds.Y > cell.Bounds.Y && outline.Bounds.Bottom < cell.Bounds.Bottom);
        Assert.Null(Group(ChartLayout.Compute(spec, data, Options).Scene, "selection:points"));
    }

    [Fact]
    public void Selected_slices_are_outlined_but_not_the_other_slice()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "p", Type = SeriesType.Pie, GroupSmallSlices = 0.1 }] };
        var data = new Dictionary<string, SeriesData> { ["p"] = SeriesData.FromCategories(["Web", "App", "Store", "Phone"], [60, 30, 8, 2]) };

        var layout = ChartLayout.Compute(spec, data, Options with { SelectedPoints = Pick("p", 1, 3) }); // App, and Phone (in "Other" with Store)

        var outline = Assert.Single(All<PathNode>(Group(layout.Scene, "selection:points")!));
        Assert.Equal("rio-selected-slice", outline.Class);
        Assert.Equal("none", layout.Scene.Styles.Get(outline.Style).Fill);
        var onlyOther = ChartLayout.Compute(spec, data, Options with { SelectedPoints = Pick("p", 2, 3) }); // both in "Other"
        Assert.Null(Group(onlyOther.Scene, "selection:points"));
    }

    [Fact]
    public void Selected_radial_bar_is_outlined()
    {
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "g", Type = SeriesType.RadialBar, Max = 100 }] };
        var data = new Dictionary<string, SeriesData> { ["g"] = SeriesData.FromCategories(["Done", "Planned"], [70, 40]) };

        var layout = ChartLayout.Compute(spec, data, Options with { SelectedPoints = Pick("g", 0) });

        Assert.Single(All<PathNode>(Group(layout.Scene, "selection:points")!));
    }
}
