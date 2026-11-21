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
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a" }] };
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
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a" }] };

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
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a" }] };
        var selected = new Dictionary<string, IReadOnlyCollection<int>> { ["a"] = [4, 1, 2, 99, -1, 1], ["nope"] = [0] };

        var layout = ChartLayout.Compute(spec, Line, Options with { SelectedPoints = selected });

        var marks = All<PathNode>(Group(layout.Scene, "selection:points")!);
        Assert.Equal(2, marks.Count); // 1 and 4; 2 is a gap
        Assert.All(marks, m => Assert.Equal("rio-selected-point", m.Class));
    }

    [Fact]
    public void Selected_points_of_hidden_series_are_not_drawn()
    {
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a" }] };
        var selected = new Dictionary<string, IReadOnlyCollection<int>> { ["a"] = [0] };

        var layout = ChartLayout.Compute(spec, Line, Options with { SelectedPoints = selected, HiddenSeries = new HashSet<string> { "a" } });

        Assert.Null(Group(layout.Scene, "selection:points"));
    }

    [Fact]
    public void Selected_stacked_bar_is_outlined_around_its_own_segment()
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
}
