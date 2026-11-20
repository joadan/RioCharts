using RioCharts.Layout;

namespace RioCharts.Tests;

public class SeriesLayoutTests
{
    [Fact]
    public void Pie_slices_cover_full_circle_in_proportion()
    {
        var chart = new Chart
        {
            Series =
            [
                new ValueSeries
                {
                    Name = "Share",
                    Points = [new("A", 1), new("B", 1), new("C", 2)],
                },
            ],
        };

        var layout = ChartLayoutEngine.Compute(chart);
        var slices = layout.Series[0].Slices!;

        Assert.False(layout.HasCartesian);
        Assert.Null(layout.XAxis);
        Assert.Equal(3, slices.Count);
        Assert.Equal(0, slices[0].StartAngle, 9);
        Assert.Equal(Math.PI * 2, slices[^1].EndAngle, 9);
        Assert.Equal(0.5, slices[2].Fraction, 9);
    }

    [Fact]
    public void Pie_skips_zero_negative_and_hidden_values()
    {
        var chart = new Chart
        {
            Series =
            [
                new ValueSeries
                {
                    Points = [new("A", 1), new("Zero", 0), new("Negative", -3), new("Hidden", 5) { Visible = false }, new("B", 3)],
                },
            ],
        };

        var slices = ChartLayoutEngine.Compute(chart).Series[0].Slices!;

        Assert.Equal(["A", "B"], slices.Select(s => s.Point.Label));
        Assert.Equal(0.25, slices[0].Fraction, 9);
    }

    [Fact]
    public void Donut_has_inner_radius()
    {
        var chart = new Chart
        {
            Series = [new ValueSeries { Type = ValueSeriesType.Donut, InnerRadiusRatio = 0.5, Points = [new("A", 1), new("B", 1)] }],
        };

        var slice = ChartLayoutEngine.Compute(chart).Series[0].Slices![0];

        Assert.Equal(slice.OuterRadius * 0.5, slice.InnerRadius, 9);
    }

    [Fact]
    public void Exploded_slice_is_offset_from_center()
    {
        var chart = new Chart
        {
            Series = [new ValueSeries { Points = [new("A", 1) { IsExploded = true }, new("B", 1)] }],
        };

        var slices = ChartLayoutEngine.Compute(chart).Series[0].Slices!;

        Assert.NotEqual(slices[0].Center, slices[1].Center);
    }

    [Fact]
    public void Funnel_segments_shrink_with_value()
    {
        var chart = new Chart
        {
            Series = [new ValueSeries { Type = ValueSeriesType.Funnel, Points = [new("Visits", 100), new("Leads", 50), new("Sales", 10)] }],
        };

        var slices = ChartLayoutEngine.Compute(chart).Series[0].Slices!;
        double TopWidth(SliceLayout s) => s.Polygon![1].X - s.Polygon[0].X;

        Assert.Equal(3, slices.Count);
        Assert.True(TopWidth(slices[0]) > TopWidth(slices[1]));
        Assert.True(TopWidth(slices[1]) > TopWidth(slices[2]));
        Assert.True(slices[0].Polygon![0].Y < slices[1].Polygon![0].Y);
    }

    [Fact]
    public void Value_series_legend_lists_each_slice()
    {
        var chart = new Chart
        {
            Series = [new ValueSeries { Name = "Pie", Points = [new("A", 1), new("B", 2)] }],
        };

        var legend = ChartLayoutEngine.Compute(chart).Legend!;

        Assert.Equal(["A", "B"], legend.Items.Select(i => i.Label));
    }

    [Fact]
    public void Corner_legend_does_not_overlap_pie()
    {
        var chart = new Chart
        {
            Series = [new ValueSeries { Points = [new("Alpha", 1), new("Beta", 2)] }],
        };

        var layout = ChartLayoutEngine.Compute(chart);

        Assert.True(layout.Legend!.X >= layout.Plot.Right);
    }

    [Fact]
    public void Bubble_sizes_scale_with_z()
    {
        var chart = new Chart
        {
            Series =
            [
                new XYZSeries { MinBubbleSize = 5, MaxBubbleSize = 50, Points = [new(0, 0, 1), new(1, 1, 50), new(2, 2, 100)] },
            ],
        };

        var sizes = ChartLayoutEngine.Compute(chart).Series[0].BubbleSizes!;

        Assert.Equal(5, sizes[0], 9);
        Assert.Equal(50, sizes[2], 9);
        Assert.InRange(sizes[1], 5, 50);
    }

    [Fact]
    public void Bubble_axes_are_padded_beyond_data()
    {
        var chart = new Chart
        {
            Series = [new XYZSeries { Points = [new(0, 70, 1), new(10, 80, 2)] }],
        };

        var layout = ChartLayoutEngine.Compute(chart);

        Assert.True(layout.YAxes[0].Min < 70);
        Assert.True(layout.XAxis!.Min < 0);
    }

    [Fact]
    public void Heatmap_axes_fit_cells_tightly()
    {
        var points = Enumerable.Range(0, 24).Select(h => new XYZPoint(h, 0, h)).ToList();
        var chart = new Chart { Series = [new XYZSeries { Type = XYZSeriesType.Heatmap, Points = points }] };

        var layout = ChartLayoutEngine.Compute(chart);

        Assert.Equal(-0.5, layout.XAxis!.Min, 9);
        Assert.Equal(23.5, layout.XAxis.Max, 9);
    }

    [Fact]
    public void Shared_pie_labels_appear_once_in_legend()
    {
        var chart = new Chart
        {
            Series =
            [
                new ValueSeries { Points = [new("A", 1), new("B", 2)] },
                new ValueSeries { Points = [new("A", 3), new("B", 4), new("C", 5)] },
            ],
        };

        var legend = ChartLayoutEngine.Compute(chart).Legend!;

        Assert.Equal(["A", "B", "C"], legend.Items.Select(i => i.Label));
    }

    [Fact]
    public void Heatmap_cells_have_size_and_intensity()
    {
        var points = new List<XYZPoint>();
        for (var x = 0; x < 5; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                points.Add(new XYZPoint(x, y, x * y));
            }
        }

        var chart = new Chart { Series = [new XYZSeries { Type = XYZSeriesType.Heatmap, Points = points }] };
        var layout = ChartLayoutEngine.Compute(chart).Series[0];

        Assert.True(layout.CellWidth > 0);
        Assert.True(layout.CellHeight > 0);
        Assert.Equal(0, layout.Intensities!.Min());
        Assert.Equal(1, layout.Intensities!.Max());
        Assert.Null(layout.BubbleSizes);
    }
}
