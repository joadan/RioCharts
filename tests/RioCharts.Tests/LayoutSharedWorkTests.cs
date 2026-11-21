using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;

namespace RioCharts.Tests;

/// <summary>
/// Work chart layout does once and reuses (series facts for the screen reader texts, tooltip x labels
/// shared by series with the same x values, the x extent from the first and last values) gives the
/// same results as doing it per series.
/// </summary>
public class LayoutSharedWorkTests
{
    private static readonly DateTime Start = new(2026, 11, 16);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static SeriesData Hourly(int n, Func<int, double> value) =>
        SeriesData.From(Enumerable.Range(0, n).Select(i => Start.AddHours(i)).ToArray(), Enumerable.Range(0, n).Select(value).ToArray());

    private static ChartSpec TwoLines() => new()
    {
        Title = "Air handling unit",
        Series = [new SeriesSpec { Id = "s", Name = "Supply" }, new SeriesSpec { Id = "e", Name = "Extract" }],
    };

    [Fact]
    public void Series_with_the_same_x_get_the_same_tooltip_x_labels()
    {
        var data = new Dictionary<string, SeriesData> { ["s"] = Hourly(48, i => 19 + i % 5), ["e"] = Hourly(48, i => 22 - i % 3) };
        var layout = ChartLayout.Compute(TwoLines(), data, new LayoutOptions { Culture = Invariant });

        var (s, e) = (layout.Tooltip.Series[0], layout.Tooltip.Series[1]);
        Assert.Equal(48, s.XLabels.Length);
        Assert.Equal(s.XLabels, e.XLabels);
        Assert.Equal(Start.AddHours(5).ToString("g", Invariant), s.XLabels[5]);
    }

    [Fact]
    public void Series_with_different_x_get_their_own_tooltip_x_labels()
    {
        var shifted = SeriesData.From(Enumerable.Range(0, 24).Select(i => Start.AddHours(i).AddMinutes(30)).ToArray(), Enumerable.Range(0, 24).Select(i => 20.0).ToArray());
        var data = new Dictionary<string, SeriesData> { ["s"] = Hourly(24, i => 19 + i % 5), ["e"] = shifted };
        var layout = ChartLayout.Compute(TwoLines(), data, new LayoutOptions { Culture = Invariant });

        Assert.Equal(Start.AddHours(3).ToString("g", Invariant), layout.Tooltip.Series[0].XLabels[3]);
        Assert.Equal(Start.AddHours(3).AddMinutes(30).ToString("g", Invariant), layout.Tooltip.Series[1].XLabels[3]);
    }

    [Fact]
    public void The_x_extent_starts_and_ends_at_the_first_and_last_values()
    {
        // Gaps at both ends of one series, and a series with no values at all.
        var gappy = Hourly(48, i => i < 6 || i >= 40 ? double.NaN : 20);
        var data = new Dictionary<string, SeriesData> { ["s"] = gappy, ["e"] = Hourly(10, _ => double.NaN) };
        var layout = ChartLayout.Compute(TwoLines(), data, new LayoutOptions { Culture = Invariant });

        Assert.Equal((TimeValue.FromDateTime(Start.AddHours(6)), TimeValue.FromDateTime(Start.AddHours(39))), layout.XExtent);
    }

    [Fact]
    public void The_layout_describes_series_like_the_describer()
    {
        var spec = TwoLines();
        var data = new Dictionary<string, SeriesData> { ["s"] = Hourly(48, i => 19 + i * 0.1), ["e"] = Hourly(48, i => 22 - (i % 7)) };
        var layout = ChartLayout.Compute(spec, data, new LayoutOptions { Culture = Invariant });

        var series = spec.Series.Select(s => (s, data[s.Id], true)).ToList();
        Assert.Equal(ChartDescriber.Describe(spec, series, XKind.Time, Invariant), layout.Scene.Description);
        Assert.Equal(ChartDescriber.DescribeSeries(spec, spec.Series[0], data["s"], XKind.Time, Invariant), layout.Legend[0].Tooltip);
        Assert.Contains("rising", layout.Scene.Description);
    }
}
