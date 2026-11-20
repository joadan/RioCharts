using System.Globalization;
using RioCharts.Layout;
using RioCharts.Rendering;

namespace RioCharts.Tests;

public class HelperTests
{
    [Fact]
    public void Lttb_keeps_first_last_and_threshold_count()
    {
        var points = Enumerable.Range(0, 1000).Select(i => new XYPoint(i, i % 7)).ToList();
        var indices = Enumerable.Range(0, 1000).ToList();

        var sampled = Lttb.Downsample(points, indices, 100);

        Assert.Equal(100, sampled.Count);
        Assert.Equal(0, sampled[0]);
        Assert.Equal(999, sampled[^1]);
        Assert.Equal(sampled.Order(), sampled); // stays ordered
    }

    [Fact]
    public void Lttb_keeps_spike()
    {
        var points = Enumerable.Range(0, 1000).Select(i => new XYPoint(i, i == 500 ? 100 : 0)).ToList();

        var sampled = Lttb.Downsample(points, Enumerable.Range(0, 1000).ToList(), 50);

        Assert.Contains(500, sampled);
    }

    [Fact]
    public void Lttb_returns_input_when_under_threshold()
    {
        var points = Enumerable.Range(0, 10).Select(i => new XYPoint(i, i)).ToList();
        var indices = Enumerable.Range(0, 10).ToList();

        Assert.Same(indices, Lttb.Downsample(points, indices, 100));
    }

    [Fact]
    public void Svg_numbers_are_culture_invariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");

            Assert.Equal("1.5", Svg.N(1.5));
            Assert.Equal("M0 0 L10.25 3.33", Svg.LinePath([new(0, 0), new(10.25, 3.333)]));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Svg_number_has_no_negative_zero()
    {
        Assert.Equal("0", Svg.N(-0.001));
    }

    [Fact]
    public void Full_circle_arc_is_two_halves()
    {
        var path = Svg.ArcPath(new(100, 100), 50, 0, 0, Math.PI * 2);

        Assert.Equal(2, path.Split('A').Length - 1);
    }

    [Theory]
    [InlineData("#000000", "white")]
    [InlineData("#ffffff", "#222222")]
    [InlineData("#EDC948", "#222222")]
    [InlineData("#4E79A7", "white")]
    [InlineData("rgb(0,0,0)", "white")]
    public void ContrastText_picks_readable_color(string background, string expected)
    {
        Assert.Equal(expected, Svg.ContrastText(background));
    }

    [Theory]
    [InlineData(AxisLabelFormat.Auto, 2.5, 0.5, "2.5")]
    [InlineData(AxisLabelFormat.Number, 1234, 200, "1,234")]
    [InlineData(AxisLabelFormat.Percentage, 0.25, 0.05, "25%")]
    [InlineData(AxisLabelFormat.Currency, 12, 2, "¤12")]
    public void Axis_labels_follow_format(AxisLabelFormat format, double value, double step, string expected)
    {
        var axis = new Axis { LabelFormat = format };

        var text = AxisLabelFormatter.Format(axis, value, step, CultureInfo.InvariantCulture);

        Assert.Equal(expected, text.Replace(" ", "", StringComparison.Ordinal));
    }

    [Fact]
    public void Date_labels_use_oa_dates()
    {
        var axis = new Axis { LabelFormat = AxisLabelFormat.Custom, CustomFormatString = "yyyy-MM-dd" };

        var text = AxisLabelFormatter.Format(axis, new DateTime(2026, 3, 1).ToOADate(), 1, CultureInfo.InvariantCulture);

        Assert.Equal("2026-03-01", text);
    }

    [Theory]
    [InlineData(0.1, 1)]
    [InlineData(0.25, 2)]
    [InlineData(2.5, 1)]
    [InlineData(10, 0)]
    [InlineData(1, 0)]
    public void Decimals_match_step(double step, int expected)
    {
        Assert.Equal(expected, AxisLabelFormatter.Decimals(step));
    }

    [Fact]
    public void Trend_detection()
    {
        var up = Enumerable.Range(0, 20).Select(i => ((double)i, (double)i * 2)).ToList();
        var down = up.Select(p => (p.Item1, -p.Item2)).ToList();
        var flat = Enumerable.Range(0, 20).Select(i => ((double)i, i % 2 == 0 ? 1.0 : -1.0)).ToList();

        Assert.Equal(Trend.Increasing, ChartSummary.ComputeTrend(up));
        Assert.Equal(Trend.Decreasing, ChartSummary.ComputeTrend(down));
        Assert.Equal(Trend.Stable, ChartSummary.ComputeTrend(flat));
    }

    [Fact]
    public void Summary_mentions_series_and_trend()
    {
        var chart = new Chart
        {
            Title = "Sales",
            Series = [new XYSeries { Name = "Revenue", Points = [new(0, 1), new(1, 2), new(2, 3)] }],
        };

        var summary = ChartSummary.Describe(ChartLayoutEngine.Compute(chart), CultureInfo.InvariantCulture);

        Assert.Contains("Sales", summary);
        Assert.Contains("Revenue: 3 data points", summary);
        Assert.Contains("increasing", summary);
    }

    [Fact]
    public void Summary_for_empty_chart_says_no_data()
    {
        var summary = ChartSummary.Describe(ChartLayoutEngine.Compute(new Chart()), CultureInfo.InvariantCulture);

        Assert.Contains("No data available", summary);
    }
}
