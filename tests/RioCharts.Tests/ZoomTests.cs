using RioCharts.Layout;

namespace RioCharts.Tests;

public class ZoomTests
{
    private static readonly AxisRange Full = new(0, 100);

    [Fact]
    public void Zoom_in_keeps_anchor_in_place()
    {
        var zoomed = ZoomMath.Zoom(Full, Full, 2, anchor: 25);

        Assert.Equal(12.5, zoomed.Minimum, 9);
        Assert.Equal(62.5, zoomed.Maximum, 9);
    }

    [Fact]
    public void Zoom_out_never_exceeds_full_range()
    {
        var zoomed = ZoomMath.Zoom(new AxisRange(40, 60), Full, 0.01, anchor: 50);

        Assert.Equal(Full, zoomed);
    }

    [Fact]
    public void Pan_stops_at_the_data_edge()
    {
        var panned = ZoomMath.Pan(new AxisRange(80, 95), Full, 20);

        Assert.Equal(new AxisRange(85, 100), panned);
    }

    [Fact]
    public void Clamp_shifts_range_back_inside()
    {
        var clamped = ZoomMath.Clamp(new AxisRange(-10, 10), Full);

        Assert.Equal(new AxisRange(0, 20), clamped);
    }

    [Fact]
    public void Clamp_enforces_minimum_span()
    {
        var clamped = ZoomMath.Clamp(new AxisRange(50, 50), Full);

        Assert.True(clamped.Maximum > clamped.Minimum);
    }

    [Fact]
    public void IsFull_detects_full_range()
    {
        Assert.True(ZoomMath.IsFull(new AxisRange(0, 100), Full));
        Assert.False(ZoomMath.IsFull(new AxisRange(0, 90), Full));
    }

    [Fact]
    public void Zoomed_layout_fits_y_to_visible_points()
    {
        // Low values on the left, a big peak on the right.
        var points = Enumerable.Range(0, 100).Select(i => new XYPoint(i, i < 50 ? 1 + i % 3 : 1000)).ToList();
        var chart = new Chart { Series = [new XYSeries { Points = points }] };

        var layout = ChartLayoutEngine.Compute(chart, new AxisRange(0, 40));

        Assert.True(layout.XAxis!.IsZoomed);
        Assert.True(layout.YAxes[0].Max < 10, $"Y max was {layout.YAxes[0].Max}");
        Assert.Equal(0, layout.XAxis.FullMin);
        Assert.True(layout.XAxis.FullMax >= 99);
    }

    [Fact]
    public void Zoomed_line_keeps_one_point_beyond_each_edge()
    {
        var points = Enumerable.Range(0, 100).Select(i => new XYPoint(i, i)).ToList();
        var chart = new Chart { Series = [new XYSeries { Points = points }] };

        var layout = ChartLayoutEngine.Compute(chart, new AxisRange(10.5, 20.5));
        var indices = layout.Series[0].PointIndices;

        Assert.Equal(10, indices[0]);
        Assert.Equal(21, indices[^1]);
    }

    [Fact]
    public void Zoomed_downsampling_uses_only_visible_points()
    {
        var points = Enumerable.Range(0, 100_000).Select(i => new XYPoint(i, Math.Sin(i))).ToList();
        var chart = new Chart { DownsampleThreshold = 1000, Series = [new XYSeries { Points = points }] };

        var layout = ChartLayoutEngine.Compute(chart, new AxisRange(0, 500));

        // 500 visible points fit under the threshold, so none are dropped.
        Assert.False(layout.Series[0].IsDownsampled);
        Assert.Equal(502, layout.Series[0].PointIndices.Count);
    }

    [Fact]
    public void Invalid_zoom_range_is_ignored()
    {
        var chart = new Chart { Series = [new XYSeries { Points = [new(0, 0), new(10, 10)] }] };

        var layout = ChartLayoutEngine.Compute(chart, new AxisRange(5, 5));

        Assert.False(layout.XAxis!.IsZoomed);
        Assert.Equal(0, layout.XAxis.Min);
        Assert.Equal(10, layout.XAxis.Max);
    }
}
