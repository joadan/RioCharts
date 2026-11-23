using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class GaugeTests
{
    private static readonly LayoutOptions Options = new() { Width = 400, Height = 300, Culture = CultureInfo.InvariantCulture };

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    private static readonly ColorBand[] Bands =
    [
        new() { Above = 60, Color = "--rio-series-2" },
        new() { Above = 85, Color = "--rio-negative" },
    ];

    private static ChartLayoutResult Gauge(SeriesOptions series, SeriesData data, LayoutOptions? options = null) =>
        ChartLayout.Compute(new ChartDefinition { Series = [series with { Id = "g", Type = SeriesType.Gauge }] },
            new Dictionary<string, SeriesData> { ["g"] = data }, options ?? Options);

    private static SeriesData One(double value) => SeriesData.FromCategories([""], [value]);

    private static List<PathNode> Paths(ChartLayoutResult layout, string cls) =>
        All<PathNode>(layout.Scene.Series).Where(p => p.Class == cls).ToList();

    private static List<string> Texts(ChartLayoutResult layout, string cls) =>
        All<TextNode>(layout.Scene.Series).Where(t => t.Class == cls).Select(t => t.Text).ToList();

    [Fact]
    public void A_gauge_shows_its_value_on_a_track_from_min_to_max()
    {
        var layout = Gauge(new SeriesOptions { Name = "Web server", Max = 100, Format = "0'%'" }, One(64));

        Assert.True(layout.HasData);
        Assert.Empty(layout.Warnings);
        Assert.Single(Paths(layout, "rio-track"));
        Assert.Single(Paths(layout, "rio-gauge-value"));
        Assert.Equal(["64%"], Texts(layout, "rio-gauge-value-text"));
        Assert.Equal(["Web server"], Texts(layout, "rio-gauge-label"));
        Assert.Equal(["0%", "100%"], Texts(layout, "rio-gauge-end"));
        Assert.Empty(layout.Legend); // gauges label themselves
        Assert.Equal("Gauge with 1 item: Web server 64% (0%–100%).", layout.Scene.Description);

        var tip = Assert.Single(layout.Tooltip.Slices);
        Assert.Equal(("Web server", "64%"), (tip.Label, tip.Value));
        Assert.Equal(0, tip.R0); // hovering anywhere on the gauge, the middle too
    }

    [Fact]
    public void Color_bands_color_the_value_and_show_as_a_scale_ring()
    {
        var layout = Gauge(new SeriesOptions { Max = 100, ColorBands = Bands }, One(64));

        Assert.Equal(2, Paths(layout, "rio-gauge-band").Count); // 60–85 and 85–100; below 60 has no band
        Assert.Equal("--rio-series-2", layout.Scene.Styles.Get(Paths(layout, "rio-gauge-value")[0].Style).Fill);
        Assert.Equal("--rio-series-2", layout.Tooltip.Slices[0].Color);

        var high = Gauge(new SeriesOptions { Max = 100, ColorBands = Bands }, One(91));
        Assert.Equal("--rio-negative", high.Tooltip.Slices[0].Color);
    }

    [Fact]
    public void A_target_is_marked_and_in_the_tooltip_and_description()
    {
        var layout = Gauge(new SeriesOptions { Name = "CPU", Max = 100, Target = 70 }, One(64));

        Assert.Single(Paths(layout, "rio-gauge-target"));
        var part = Assert.Single(layout.Tooltip.Slices[0].Parts);
        Assert.Equal(("Target", "70"), (part.Label, part.Value));
        Assert.Contains("CPU 64 (0–100, Target 70)", layout.Scene.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void The_scale_defaults_to_zero_and_a_nice_value_above_the_largest()
    {
        var layout = Gauge(new SeriesOptions(), One(72));
        Assert.Equal(["0", "80"], Texts(layout, "rio-gauge-end"));

        var withTarget = Gauge(new SeriesOptions { Target = 130 }, One(72));
        Assert.Equal("150", Texts(withTarget, "rio-gauge-end")[1]); // the target fits on the scale

        var negative = Gauge(new SeriesOptions { Max = 40 }, One(-12));
        Assert.Equal(["-15", "40"], Texts(negative, "rio-gauge-end"));
    }

    [Fact]
    public void A_value_off_the_scale_fills_or_empties_the_track_but_keeps_its_text()
    {
        var over = Gauge(new SeriesOptions { Max = 100 }, One(130));
        Assert.Single(Paths(over, "rio-gauge-value"));
        Assert.Equal(["130"], Texts(over, "rio-gauge-value-text"));

        var under = Gauge(new SeriesOptions { Min = 10, Max = 100 }, One(4));
        Assert.Empty(Paths(under, "rio-gauge-value"));
        Assert.Equal(["4"], Texts(under, "rio-gauge-value-text"));
    }

    [Fact]
    public void Each_point_is_a_gauge_laid_out_in_a_grid()
    {
        var data = SeriesData.FromCategories(["System", "Data", "Backup"], [42.0, 77, 93]);

        var wide = Gauge(new SeriesOptions { Max = 100 }, data, Options with { Width = 900, Height = 220 });
        Assert.Equal(["System", "Data", "Backup"], Texts(wide, "rio-gauge-label"));
        var centers = wide.Tooltip.Slices.Select(s => (s.Cx, s.Cy)).ToList();
        Assert.Single(centers.Select(c => c.Cy).Distinct()); // one row
        Assert.True(centers[0].Cx < centers[1].Cx && centers[1].Cx < centers[2].Cx);

        var tall = Gauge(new SeriesOptions { Max = 100 }, data, Options with { Width = 220, Height = 700 });
        Assert.Single(tall.Tooltip.Slices.Select(s => s.Cx).Distinct()); // one column
    }

    [Fact]
    public void Several_gauge_series_each_have_their_own_scale_and_color()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Series =
            [
                new SeriesOptions { Id = "cpu", Name = "CPU", Type = SeriesType.Gauge, Max = 100 },
                new SeriesOptions { Id = "mem", Name = "Memory", Type = SeriesType.Gauge, Max = 32, Format = "0 GB" },
            ],
        }, new Dictionary<string, SeriesData> { ["cpu"] = One(64), ["mem"] = One(21) }, Options);

        Assert.Equal(["CPU", "Memory"], Texts(layout, "rio-gauge-label"));
        Assert.Equal(["0", "100", "0 GB", "32 GB"], Texts(layout, "rio-gauge-end"));
        Assert.NotEqual(layout.Tooltip.Slices[0].Color, layout.Tooltip.Slices[1].Color);
    }

    [Fact]
    public void A_half_circle_puts_the_label_under_the_gauge()
    {
        var layout = Gauge(new SeriesOptions { Name = "Disk", Max = 100, StartAngle = -90, EndAngle = 90 }, One(42));

        var slice = layout.Tooltip.Slices[0];
        var label = All<TextNode>(layout.Scene.Series).Single(t => t.Class == "rio-gauge-label");
        Assert.True(label.At.Y > slice.Cy);
        Assert.True(slice.Cy - slice.R1 >= layout.Plot.Y - 0.5 && slice.Cy <= layout.Plot.Bottom);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void No_value_is_an_empty_state(double value)
    {
        var layout = Gauge(new SeriesOptions { Max = 100 }, One(value));

        Assert.False(layout.HasData);
        Assert.Contains("No data available", All<TextNode>(layout.Scene.Overlay).Select(t => t.Text));
        Assert.Equal("Gauge, no data.", layout.Scene.Description);
    }

    [Fact]
    public void Odd_settings_draw_a_clean_gauge_with_warnings()
    {
        var flat = Gauge(new SeriesOptions { Min = 50, Max = 50 }, One(50));
        Assert.Contains(flat.Warnings, w => w.Contains("must be above min", StringComparison.Ordinal));
        Assert.True(flat.HasData);

        var angles = Gauge(new SeriesOptions { Max = 100, StartAngle = 10, EndAngle = 10 }, One(50));
        Assert.Contains(angles.Warnings, w => w.Contains("too close", StringComparison.Ordinal));

        var tiny = Gauge(new SeriesOptions { Max = 100, Name = "A very long name for a small gauge" }, One(50), Options with { Width = 60, Height = 40 });
        Assert.True(tiny.HasData);
    }

    [Fact]
    public void Compact_draws_only_the_gauge()
    {
        var layout = Gauge(new SeriesOptions { Name = "CPU", Max = 100, ColorBands = Bands }, One(64), Options with { Width = 48, Height = 32 });

        Assert.Empty(Texts(layout, "rio-gauge-label"));
        Assert.Empty(Texts(layout, "rio-gauge-end"));
        Assert.Empty(Paths(layout, "rio-gauge-band"));
        Assert.Single(Paths(layout, "rio-gauge-value"));
    }

    [Fact]
    public void Gauges_cannot_be_mixed_with_other_polar_types()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Series =
            [
                new SeriesOptions { Id = "g", Type = SeriesType.Gauge, Max = 100 },
                new SeriesOptions { Id = "p", Type = SeriesType.Pie },
            ],
        }, new Dictionary<string, SeriesData> { ["g"] = One(64), ["p"] = SeriesData.FromCategories(["A", "B"], [1.0, 2]) }, Options);

        Assert.Contains(layout.Warnings, w => w.Contains("cannot be mixed", StringComparison.Ordinal));
        Assert.Single(layout.Tooltip.Slices);
    }

    [Fact]
    public void Texts_follow_the_chart_language()
    {
        var layout = Gauge(new SeriesOptions { Name = "CPU", Max = 100, Target = 70 }, One(64), Options with { Culture = CultureInfo.GetCultureInfo("sv-SE") });

        Assert.Equal("Mål", layout.Tooltip.Slices[0].Parts[0].Label);
        Assert.StartsWith("Mätare med 1 post: CPU 64 (0–100, Mål 70)", layout.Scene.Description, StringComparison.Ordinal);
    }

    // ---------- JSON definitions ----------

    private static Dictionary<string, ChartDataset> Servers() => new()
    {
        ["servers"] = ChartDataset.FromRows("servers", new (string Server, double Cpu)[] { ("web", 52), ("db", 81), ("web", 64), ("db", 77) }
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["server"] = r.Server, ["cpu"] = r.Cpu })),
    };

    private static DefinitionBindResult Bind(string json)
    {
        var parsed = ChartDefinitionJson.Parse(json);
        Assert.True(parsed.Success, string.Join("; ", parsed.Errors.Select(e => e.Message)));
        return ChartDefinitionBinder.Bind(parsed.Definition!, Servers());
    }

    [Fact]
    public void A_json_gauge_shows_one_value_from_the_rows()
    {
        var last = Bind("""{ "dataset": "servers", "series": [ { "type": "gauge", "y": "cpu", "max": 100, "target": 70 } ] }""");
        Assert.True(last.Success, string.Join("; ", last.Errors.Select(e => e.Message)));
        var series = Assert.Single(last.Definition.Series);
        Assert.Equal((SeriesType.Gauge, "Cpu", 70.0), (series.Type, series.Name, series.Target));
        Assert.Equal(77, last.Data[series.Id].Y[0]); // the last row
        Assert.Contains(last.Warnings, w => w.Contains("the last of 4 rows", StringComparison.Ordinal));

        var average = Bind("""{ "dataset": "servers", "series": [ { "type": "meter", "y": "cpu", "aggregate": "average", "filter": [ { "field": "server", "op": "eq", "value": "web" } ] } ] }""");
        Assert.True(average.Success);
        Assert.Equal(58, average.Data[average.Definition.Series[0].Id].Y[0]);

        var inferred = Bind("""{ "dataset": "servers", "series": [ { "type": "gauge" } ] }""");
        Assert.True(inferred.Success);
        Assert.Contains(inferred.Warnings, w => w.Contains("using 'cpu'", StringComparison.Ordinal));

        var layout = ChartLayout.Compute(last.Definition, last.Data, Options);
        Assert.True(layout.HasData);
    }

    [Fact]
    public void A_json_gauge_with_x_or_group_by_is_one_gauge_per_value()
    {
        var perServer = Bind("""{ "dataset": "servers", "series": [ { "type": "gauge", "groupBy": "server", "y": "cpu", "max": 100 } ] }""");
        Assert.True(perServer.Success, string.Join("; ", perServer.Errors.Select(e => e.Message)));
        var layout = ChartLayout.Compute(perServer.Definition, perServer.Data, Options);
        Assert.Equal([("web", "64"), ("db", "77")], layout.Tooltip.Slices.Select(s => (s.Label, s.Value)));

        var byX = Bind("""{ "dataset": "servers", "series": [ { "type": "gauge", "x": "server", "y": "cpu", "aggregate": "max" } ] }""");
        Assert.Equal([64.0, 81], byX.Data[byX.Definition.Series[0].Id].Y);
    }

    [Fact]
    public void Razor_for_a_gauge_gives_its_value_and_scale()
    {
        var single = ChartRazor.FromDefinition(ChartDefinitionJson.Parse(
            """{ "dataset": "servers", "series": [ { "type": "gauge", "y": "cpu", "min": 0, "max": 100, "target": 70, "startAngle": -90, "endAngle": 90 } ] }""").Definition!);
        Assert.Contains("<GaugeSeries Value=\"@value\"", single, StringComparison.Ordinal);
        Assert.Contains(" Min=\"0\" Max=\"100\" Target=\"70\" StartAngle=\"-90\" EndAngle=\"90\"", single, StringComparison.Ordinal);
        Assert.Contains("value: the last of cpu", single, StringComparison.Ordinal);
        Assert.DoesNotContain("YValue", single, StringComparison.Ordinal);

        var perServer = ChartRazor.FromDefinition(ChartDefinitionJson.Parse(
            """{ "dataset": "servers", "series": [ { "type": "gauge", "x": "server", "y": "cpu" } ] }""").Definition!);
        Assert.Contains("<GaugeSeries XValue=\"r => r.Server\"", perServer, StringComparison.Ordinal);
        Assert.Contains("YValue=\"r => r.Cpu\"", perServer, StringComparison.Ordinal);
    }
}
