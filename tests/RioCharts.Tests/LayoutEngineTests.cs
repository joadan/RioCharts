using RioCharts.Layout;

namespace RioCharts.Tests;

public class LayoutEngineTests
{
    private static Chart LineChart(params XYPoint[] points) => new()
    {
        Width = 800,
        Height = 600,
        Series = [new XYSeries { Name = "A", Points = [.. points] }],
    };

    [Fact]
    public void Plot_area_respects_margins()
    {
        var layout = ChartLayoutEngine.Compute(LineChart(new(0, 0), new(10, 10)));

        Assert.Equal(80, layout.Plot.Left);
        Assert.Equal(60, layout.Plot.Top);
        Assert.Equal(800 - 80 - 40, layout.Plot.Width);
        Assert.Equal(600 - 60 - 60, layout.Plot.Height);
    }

    [Fact]
    public void Auto_range_rounds_out_to_nice_values()
    {
        var layout = ChartLayoutEngine.Compute(LineChart(new(1, 3), new(9, 47)));

        Assert.Equal(0, layout.XAxis!.Min);
        Assert.Equal(10, layout.XAxis.Max);
        Assert.Equal(0, layout.YAxes[0].Min);
        Assert.Equal(50, layout.YAxes[0].Max);
    }

    [Fact]
    public void Explicit_min_and_max_are_kept()
    {
        var chart = LineChart(new(0, 3), new(10, 47));
        chart.YAxes = [new Axis { Minimum = -5, Maximum = 55 }];

        var layout = ChartLayoutEngine.Compute(chart);

        Assert.Equal(-5, layout.YAxes[0].Min);
        Assert.Equal(55, layout.YAxes[0].Max);
    }

    [Fact]
    public void AutoRange_off_uses_raw_data_extent()
    {
        var chart = LineChart(new(1, 3), new(9, 47));
        chart.XAxis = new Axis { AutoRange = false };

        var layout = ChartLayoutEngine.Compute(chart);

        Assert.Equal(1, layout.XAxis!.Min);
        Assert.Equal(9, layout.XAxis.Max);
    }

    [Fact]
    public void Invalid_axis_range_falls_back_to_0_100_with_warning()
    {
        var chart = LineChart(new(0, 3), new(10, 47));
        chart.YAxes = [new Axis { Minimum = 10, Maximum = 5 }];

        var layout = ChartLayoutEngine.Compute(chart);

        Assert.Equal(0, layout.YAxes[0].Min);
        Assert.Equal(100, layout.YAxes[0].Max);
        Assert.Contains(layout.Warnings, w => w.Contains("minimum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Flat_data_is_padded_so_range_is_not_empty()
    {
        var layout = ChartLayoutEngine.Compute(LineChart(new(0, 5), new(1, 5), new(2, 5)));

        Assert.True(layout.YAxes[0].Min < 5);
        Assert.True(layout.YAxes[0].Max > 5);
    }

    [Fact]
    public void Empty_chart_has_axes_and_no_data()
    {
        var layout = ChartLayoutEngine.Compute(new Chart());

        Assert.False(layout.HasData);
        Assert.True(layout.HasCartesian);
        Assert.NotNull(layout.XAxis);
        Assert.Single(layout.YAxes);
        Assert.Equal(0, layout.XAxis!.Min);
        Assert.Equal(100, layout.XAxis.Max);
    }

    [Fact]
    public void Series_without_points_is_empty_but_listed_in_legend()
    {
        var chart = new Chart
        {
            Series =
            [
                new XYSeries { Name = "Data", Points = [new(0, 1), new(1, 2)] },
                new XYSeries { Name = "Nothing" },
            ],
        };

        var layout = ChartLayoutEngine.Compute(chart);

        Assert.True(layout.HasData);
        Assert.True(layout.Series.Single(s => s.Series.Name == "Nothing").IsEmpty);
        var item = layout.Legend!.Items.Single(i => i.Label == "Nothing");
        Assert.True(item.IsEmpty);
    }

    [Fact]
    public void Points_map_to_plot_corners()
    {
        var chart = LineChart(new(0, 0), new(10, 50));
        chart.XAxis = new Axis { Minimum = 0, Maximum = 10 };
        chart.YAxes = [new Axis { Minimum = 0, Maximum = 50 }];

        var layout = ChartLayoutEngine.Compute(chart);
        var points = layout.Series[0].ScreenPoints;

        Assert.Equal(layout.Plot.Left, points[0].X, 6);
        Assert.Equal(layout.Plot.Bottom, points[0].Y, 6);
        Assert.Equal(layout.Plot.Right, points[1].X, 6);
        Assert.Equal(layout.Plot.Top, points[1].Y, 6);
    }

    [Fact]
    public void Non_finite_points_are_skipped()
    {
        var layout = ChartLayoutEngine.Compute(LineChart(new(0, 1), new(1, double.NaN), new(2, double.PositiveInfinity), new(3, 4)));

        Assert.Equal([0, 3], layout.Series[0].PointIndices);
    }

    [Fact]
    public void Series_map_to_their_own_y_axis()
    {
        var chart = new Chart
        {
            YAxes = [new Axis(), new Axis()],
            Series =
            [
                new XYSeries { Name = "Small", Points = [new(0, 0), new(1, 1)] },
                new XYSeries { Name = "Large", YAxisIndex = 1, Points = [new(0, 0), new(1, 1000)] },
            ],
        };

        var layout = ChartLayoutEngine.Compute(chart);

        Assert.Equal(1, layout.YAxes[0].Max);
        Assert.Equal(1000, layout.YAxes[1].Max);
        Assert.Equal(AxisSide.Left, layout.YAxes[0].Side);
        Assert.Equal(AxisSide.Right, layout.YAxes[1].Side);

        // Both series reach the top of the plot on their own axis.
        Assert.All(layout.Series, s => Assert.Equal(layout.Plot.Top, s.ScreenPoints[^1].Y, 6));
    }

    [Fact]
    public void Missing_y_axis_reference_falls_back_to_first_axis()
    {
        var chart = LineChart(new(0, 0), new(1, 1));
        ((XYSeries)chart.Series[0]).YAxisIndex = 3;

        var layout = ChartLayoutEngine.Compute(chart);

        Assert.Same(layout.YAxes[0], layout.Series[0].LayoutYAxis);
        Assert.Contains(layout.Warnings, w => w.Contains("Y axis 3", StringComparison.Ordinal));
    }

    [Fact]
    public void Area_series_includes_zero_and_has_baseline()
    {
        var chart = new Chart
        {
            Series = [new XYSeries { Name = "Area", FillColor = "auto", Points = [new(0, 40), new(1, 50)] }],
        };

        var layout = ChartLayoutEngine.Compute(chart);

        Assert.Equal(0, layout.YAxes[0].Min);
        Assert.Equal(layout.Plot.Bottom, layout.Series[0].BaselineY!.Value, 6);
    }

    [Fact]
    public void Large_series_are_downsampled()
    {
        var points = Enumerable.Range(0, 10_000).Select(i => new XYPoint(i, Math.Sin(i / 100.0))).ToArray();
        var chart = LineChart(points);
        chart.DownsampleThreshold = 500;

        var layout = ChartLayoutEngine.Compute(chart);

        Assert.True(layout.Series[0].IsDownsampled);
        Assert.Equal(500, layout.Series[0].ScreenPoints.Count);
        Assert.Equal(0, layout.Series[0].PointIndices[0]);
        Assert.Equal(9_999, layout.Series[0].PointIndices[^1]);
    }

    [Fact]
    public void Hidden_series_do_not_affect_ranges()
    {
        var chart = new Chart
        {
            Series =
            [
                new XYSeries { Name = "Shown", Points = [new(0, 0), new(1, 10)] },
                new XYSeries { Name = "Hidden", Visible = false, Points = [new(0, 0), new(1, 1000)] },
            ],
        };

        var layout = ChartLayoutEngine.Compute(chart);

        Assert.Equal(10, layout.YAxes[0].Max);
    }

    [Fact]
    public void Series_are_ordered_by_render_order()
    {
        var chart = new Chart
        {
            Series =
            [
                new XYSeries { Name = "Top", RenderOrder = 5, Points = [new(0, 0)] },
                new XYSeries { Name = "Bottom", RenderOrder = -1, Points = [new(0, 0)] },
                new XYSeries { Name = "Middle", Points = [new(0, 0)] },
            ],
        };

        var layout = ChartLayoutEngine.Compute(chart);

        Assert.Equal(["Bottom", "Middle", "Top"], layout.Series.Select(s => s.Series.Name));
    }

    [Fact]
    public void Unset_colors_come_from_palette()
    {
        var chart = new Chart
        {
            Palette = ["#111111", "#222222"],
            Series =
            [
                new XYSeries { Name = "A", Points = [new(0, 0)] },
                new XYSeries { Name = "B", StrokeColor = "red", Points = [new(0, 0)] },
                new XYSeries { Name = "C", Points = [new(0, 0)] },
            ],
        };

        var layout = ChartLayoutEngine.Compute(chart);
        var colors = layout.Series.OrderBy(s => s.SeriesIndex).Select(s => s.Color).ToList();

        Assert.Equal(["#111111", "red", "#111111"], colors);
    }

    [Fact]
    public void X_range_override_is_applied()
    {
        var layout = ChartLayoutEngine.Compute(LineChart(new(0, 0), new(100, 10)), new AxisRange(20, 40));

        Assert.Equal(20, layout.XAxis!.Min);
        Assert.Equal(40, layout.XAxis.Max);
    }
}
