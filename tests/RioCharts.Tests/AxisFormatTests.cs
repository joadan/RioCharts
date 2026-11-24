using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

/// <summary>Axis formats: ticks the format can tell apart, a label function, and the same formats in tooltips and screen reader text.</summary>
public class AxisFormatTests
{
    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static readonly LayoutOptions Options = new() { Width = 640, Height = 320, Culture = CultureInfo.InvariantCulture };

    private static readonly string[] Stores = ["Store 1", "Store 2", "Store 3"];

    private static List<string> YLabels(ChartLayoutResult r) =>
        [.. All(r.Scene.Static).Where(n => n.Class == "rio-axis-y").SelectMany(All).OfType<TextNode>().Select(t => t.Text)];

    private static ChartLayoutResult Bars(double[] values, AxisOptions y, string? seriesFormat = null) => ChartLayout.Compute(
        new ChartDefinition { Y = y, Series = [new SeriesOptions { Id = "alarms", Name = "Alarms", Type = SeriesType.Bar, Format = seriesFormat }] },
        new Dictionary<string, SeriesData> { ["alarms"] = SeriesData.FromCategories(Stores, values) }, Options);

    [Fact]
    public void Ticks_are_never_closer_than_the_format_tells_apart()
    {
        var layout = Bars([0, 1, 2], new AxisOptions { Format = "#,0" });

        var labels = YLabels(layout);
        Assert.Equal(labels.Distinct(), labels); // not 0 0 1 1 2 2
        Assert.All(labels, l => Assert.DoesNotContain(".", l));
        Assert.Contains("1", labels);
    }

    [Fact]
    public void Percent_formats_keep_whole_percents()
    {
        var layout = Bars([0, 0.01, 0.02], new AxisOptions { Format = "P0" });

        var labels = YLabels(layout);
        Assert.Equal(labels.Distinct(), labels);
    }

    [Fact]
    public void All_zero_values_get_an_axis_from_zero_up()
    {
        var layout = Bars([0, 0, 0], new AxisOptions { Format = "#,0" });

        Assert.Equal(0, layout.YScale.DomainMin);
        Assert.Equal(1, layout.YScale.DomainMax);
        Assert.Equal(["0", "1"], YLabels(layout));
    }

    private static string Bytes(double v) =>
        v >= 1e9 ? (v / 1e9).ToString("0.#", CultureInfo.InvariantCulture) + " GB"
        : v >= 1e6 ? (v / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + " MB"
        : v >= 1e3 ? (v / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + " KB"
        : v.ToString("0", CultureInfo.InvariantCulture) + " B";

    [Fact]
    public void A_label_function_writes_the_ticks_tooltips_and_description()
    {
        var layout = Bars([1_500_000, 3_000_000, 250_000], new AxisOptions { LabelFormatter = Bytes });

        Assert.Contains("1 MB", YLabels(layout));
        Assert.Contains("3 MB", YLabels(layout));
        Assert.Equal(["1.5 MB", "3 MB", "250 KB"], layout.Tooltip.Series[0].Values);
        Assert.Contains("lowest 250 KB at Store 3, highest 3 MB at Store 2", layout.Scene.Description);
    }

    [Fact]
    public void A_series_format_wins_over_the_axis_function()
    {
        var layout = Bars([1_500_000, 3_000_000, 250_000], new AxisOptions { LabelFormatter = Bytes }, seriesFormat: "#,0");

        Assert.Contains("3 MB", YLabels(layout));
        Assert.Equal(["1,500,000", "3,000,000", "250,000"], layout.Tooltip.Series[0].Values);
    }

    [Fact]
    public void The_description_uses_the_series_format_not_scientific_notation()
    {
        var layout = Bars([13_910, 9_000, 4_200], new AxisOptions(), seriesFormat: "#,0 kWh");

        Assert.Contains("highest 13,910 kWh at Store 1", layout.Scene.Description);
        Assert.DoesNotContain("E+", layout.Scene.Description);
    }

    [Fact]
    public void Without_a_format_the_description_writes_plain_numbers()
    {
        var layout = Bars([13_910, 9_000, 4_200], new AxisOptions());

        Assert.Contains("highest 13,910 at Store 1", layout.Scene.Description);
    }

    [Fact]
    public void A_format_and_a_log_axis_work_when_the_current_culture_has_another_minus_sign()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE"); // minus sign U+2212
            var layout = Bars([0, 1, 2], new AxisOptions { Format = "#,0" });
            Assert.Equal(YLabels(layout).Distinct(), YLabels(layout));

            var log = Bars([0.002, 0.05, 3], new AxisOptions { Scale = ScaleType.Log });
            Assert.Contains("0.01", YLabels(log));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
