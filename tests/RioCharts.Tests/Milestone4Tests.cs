using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;

namespace RioCharts.Tests;

public class Milestone4Tests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };

    // ---------- Zoom ----------

    [Fact]
    public void Visible_range_sets_the_x_domain_and_the_y_axis_fits_the_visible_data()
    {
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From([0.0, 1, 2, 3, 4, 5], [1.0, 2, 3, 100, 200, 300]) };
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a" }] };

        var all = ChartLayout.Compute(spec, data, Options);
        var zoomed = ChartLayout.Compute(spec, data, Options with { XRange = (0, 2) });

        Assert.False(all.Zoomed);
        Assert.True(zoomed.Zoomed);
        Assert.Equal((0, 2), (zoomed.XScale!.DomainMin, zoomed.XScale.DomainMax));
        Assert.Equal(all.XExtent, zoomed.XExtent); // the full range stays known for reset and clamping
        Assert.True(zoomed.YScale.DomainMax < 10);
        Assert.True(all.YScale.DomainMax >= 300);
    }

    [Fact]
    public void Zooming_into_large_data_downsamples_only_the_visible_points()
    {
        const int n = 100_000;
        var x = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
        var y = x.Select(v => Math.Sin(v / 50)).ToArray();
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a" }] };
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From(x, y) };

        var zoomed = ChartLayout.Compute(spec, data, Options with { XRange = (1000, 1400) });
        var hits = zoomed.HitTest.Series[0];

        // 401 visible points fit under the threshold: all of them are drawn, none outside.
        Assert.InRange(hits.X.Length, 399, 402);
        Assert.All(hits.X, px => Assert.InRange(px, zoomed.Plot.X - 1, zoomed.Plot.Right + 1));
    }

    [Fact]
    public void Visible_range_is_ignored_on_category_axes()
    {
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a", Type = SeriesType.Bar }] };
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.FromCategories(["A", "B"], [1.0, 2]) };

        var result = ChartLayout.Compute(spec, data, Options with { XRange = (0, 1) });

        Assert.False(result.Zoomed);
        Assert.Null(result.XExtent);
    }
}
