using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class BulletTests
{
    private static readonly LayoutOptions Options = new() { Width = 500, Height = 240, Culture = CultureInfo.InvariantCulture };

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    private static ChartLayoutResult Bullets(params (SeriesOptions Spec, SeriesData Data)[] series) => Bullets(Options, series);

    private static ChartLayoutResult Bullets(LayoutOptions options, params (SeriesOptions Spec, SeriesData Data)[] series) =>
        ChartLayout.Compute(new ChartDefinition { Series = [.. series.Select(s => s.Spec with { Type = SeriesType.Bullet })] },
            series.ToDictionary(s => s.Spec.Id, s => s.Data), options);

    private static SeriesData One(double value) => SeriesData.FromCategories([""], [value]);

    private static readonly SeriesData Regions = SeriesData.FromCategories(["North", "South", "East"], [182.0, 96, 141]).WithTargets([160.0, 130, double.NaN]);

    private static List<RectNode> Rects(ChartLayoutResult layout, string cls) => All<RectNode>(layout.Scene.Series).Where(r => r.Class == cls).ToList();

    private static List<string> Texts(ChartLayoutResult layout, string cls) =>
        All<TextNode>(layout.Scene.Static).Concat(All<TextNode>(layout.Scene.Series)).Where(t => t.Class == cls).Select(t => t.Text).ToList();

    [Fact]
    public void Each_point_is_a_row_with_a_track_a_bar_and_its_target()
    {
        var layout = Bullets((new SeriesOptions { Id = "s", Name = "Sales", Format = "0 k€" }, Regions));

        Assert.True(layout.HasData);
        Assert.Empty(layout.Warnings);
        Assert.Equal(["North", "South", "East"], Texts(layout, "rio-row-label"));
        Assert.Equal(3, Rects(layout, "rio-bullet-range").Count);
        Assert.Equal(3, Rects(layout, "rio-bullet-bar").Count);
        Assert.Equal(2, All<PathNode>(layout.Scene.Series).Count(p => p.Class == "rio-bullet-target")); // East has no target
        Assert.Empty(layout.Legend);

        var bars = Rects(layout, "rio-bullet-bar");
        Assert.True(bars[0].Bounds.Y < bars[1].Bounds.Y && bars[1].Bounds.Y < bars[2].Bounds.Y); // top to bottom, in input order
        Assert.True(bars[0].Bounds.Width > bars[2].Bounds.Width && bars[2].Bounds.Width > bars[1].Bounds.Width);

        var tip = Assert.Single(layout.Tooltip.Series);
        Assert.Equal(["North", "South", "East"], tip.XLabels);
        Assert.Equal(["182 k€ (Target 160 k€)", "96 k€ (Target 130 k€)", "141 k€"], tip.Values);
        Assert.Equal(layout.Plot.Width, tip.CellWidths![0]); // the whole row is hoverable
        Assert.Equal("Bullet chart with 3 items: North 182 k€ (0 k€–200 k€, Target 160 k€), South 96 k€ (0 k€–200 k€, Target 130 k€), East 141 k€ (0 k€–200 k€).",
            layout.Scene.Description);
    }

    [Fact]
    public void Each_series_has_its_own_scale_with_ticks_under_it()
    {
        var layout = Bullets(
            (new SeriesOptions { Id = "revenue", Name = "Revenue", Max = 300, Target = 250 }, One(268)),
            (new SeriesOptions { Id = "rating", Name = "Rating", Min = 1, Max = 5, Format = "0.0" }, One(4.4)));

        Assert.Equal(["Revenue", "Rating"], Texts(layout, "rio-row-label"));
        var ticks = Texts(layout, "rio-tick-label");
        Assert.Contains("300", ticks);
        Assert.Contains("1.0", ticks);
        Assert.Contains("5.0", ticks);

        // Revenue 268 of 300 and Rating 4.4 of 1–5 fill about the same share of the track.
        var bars = Rects(layout, "rio-bullet-bar");
        Assert.Equal(268 / 300.0, bars[0].Bounds.Width / layout.Plot.Width, 2);
        Assert.Equal(3.4 / 4, bars[1].Bounds.Width / layout.Plot.Width, 2);
        Assert.Equal("Revenue", layout.Tooltip.Series[0].XLabels[0]); // a series' single value is labeled by its name
    }

    [Fact]
    public void Series_share_one_color_unless_they_set_their_own()
    {
        var layout = Bullets(
            (new SeriesOptions { Id = "a", Name = "A", Max = 10 }, One(5)),
            (new SeriesOptions { Id = "b", Name = "B", Max = 10 }, One(6)),
            (new SeriesOptions { Id = "c", Name = "C", Max = 10, Color = "#123456" }, One(7)));

        Assert.Equal(["--rio-series-1", "--rio-series-1", "#123456"], layout.Tooltip.Series.Select(s => s.Color));
    }

    [Fact]
    public void Color_bands_tint_the_ranges_and_color_the_bar()
    {
        var bands = new[] { new ColorBand { Below = 150, Color = "--rio-negative" }, new ColorBand { Above = 225, Color = "--rio-positive" } };
        var layout = Bullets((new SeriesOptions { Id = "s", Max = 300, ColorBands = bands }, SeriesData.FromCategories(["Low", "Mid", "High"], [120.0, 180, 260])));

        var ranges = Rects(layout, "rio-bullet-band");
        Assert.Equal(6, ranges.Count); // two per row; 150–225 has no band
        Assert.Equal(0.3f, layout.Scene.Styles.Get(ranges[0].Style).FillOpacity);
        Assert.Equal([0, 150 / 300.0 * layout.Plot.Width], [ranges[0].Bounds.X - layout.Plot.X, ranges[0].Bounds.Width], new ToleranceComparer(0.5));
        Assert.Equal(["--rio-negative", "--rio-series-1", "--rio-positive"], layout.Tooltip.Series[0].PointColors!);
    }

    [Fact]
    public void Scales_default_to_zero_and_a_nice_value_above_values_and_targets()
    {
        var layout = Bullets((new SeriesOptions { Id = "s" }, SeriesData.FromCategories(["A"], [72.0]).WithTargets([130.0])));
        var ticks = Texts(layout, "rio-tick-label");
        Assert.Equal("0", ticks[0]);
        Assert.Equal("150", ticks[^1]);

        var negative = Bullets((new SeriesOptions { Id = "s", Max = 40 }, SeriesData.FromCategories(["A", "B"], [-12.0, 30])));
        var bars = Rects(negative, "rio-bullet-bar");
        Assert.Equal(bars[1].Bounds.X, bars[0].Bounds.Right, 0.5); // a negative bar runs left from 0, a positive one right
        Assert.True(bars[0].Bounds.X > negative.Plot.X - 0.5 && bars[1].Bounds.X > negative.Plot.X + 10);
        Assert.StartsWith("-", Texts(negative, "rio-tick-label")[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Off_scale_values_and_targets_are_clipped()
    {
        var layout = Bullets((new SeriesOptions { Id = "s", Max = 100, Target = 140 }, One(130)));

        Assert.Equal(layout.Plot.Width, Rects(layout, "rio-bullet-bar")[0].Bounds.Width, 0.5);
        Assert.DoesNotContain(All<PathNode>(layout.Scene.Series), p => p.Class == "rio-bullet-target");
        Assert.Equal("130 (Target 140)", layout.Tooltip.Series[0].Values[0]);
    }

    [Fact]
    public void Data_labels_write_the_value_after_the_bar()
    {
        var layout = Bullets((new SeriesOptions { Id = "s", Max = 300, DataLabels = true, Format = "0 k€" }, One(120)));

        var label = All<TextNode>(layout.Scene.Series).Single(t => t.Class == "rio-data-label");
        Assert.Equal("120 k€", label.Text);
        Assert.True(label.At.X > Rects(layout, "rio-bullet-bar")[0].Bounds.Right);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void No_value_is_an_empty_state(double value)
    {
        var layout = Bullets((new SeriesOptions { Id = "s", Max = 10 }, One(value)));

        Assert.False(layout.HasData);
        Assert.Contains("No data available", All<TextNode>(layout.Scene.Overlay).Select(t => t.Text));
        Assert.Equal("Bullet chart, no data.", layout.Scene.Description);
    }

    [Fact]
    public void Odd_settings_and_sizes_draw_cleanly()
    {
        var flat = Bullets((new SeriesOptions { Id = "s", Min = 5, Max = 5 }, One(5)));
        Assert.Contains(flat.Warnings, w => w.Contains("must be above min", StringComparison.Ordinal));
        Assert.True(flat.HasData);

        var many = Bullets((new SeriesOptions { Id = "s" }, SeriesData.FromCategories([.. Enumerable.Range(0, 60).Select(i => $"R{i}")], [.. Enumerable.Range(0, 60).Select(i => (double)i)])));
        Assert.Contains(many.Warnings, w => w.Contains("the first 40", StringComparison.Ordinal));
        Assert.Equal(40, Rects(many, "rio-bullet-range").Count);

        var tiny = Bullets(Options with { Width = 60, Height = 30 }, (new SeriesOptions { Id = "s", Name = "A long name", Max = 10, Target = 8 }, One(5)));
        Assert.True(tiny.HasData);
        Assert.Empty(Texts(tiny, "rio-row-label")); // compact: the bars only
        Assert.Empty(Texts(tiny, "rio-tick-label"));
    }

    [Fact]
    public void Bullets_skip_other_series_and_annotations()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Series =
            [
                new SeriesOptions { Id = "b", Type = SeriesType.Bullet, Max = 10 },
                new SeriesOptions { Id = "l", Type = SeriesType.Line },
            ],
            Annotations = [new Annotation { Value = 5 }],
        }, new Dictionary<string, SeriesData> { ["b"] = One(5), ["l"] = SeriesData.From([1.0, 2], [3.0, 4]) }, Options);

        Assert.Contains(layout.Warnings, w => w.Contains("cannot be mixed with bullet", StringComparison.Ordinal));
        Assert.Contains(layout.Warnings, w => w.Contains("no annotations", StringComparison.Ordinal));
        Assert.Single(layout.Tooltip.Series);
    }

    [Fact]
    public void Texts_follow_the_chart_language()
    {
        var layout = Bullets(Options with { Culture = CultureInfo.GetCultureInfo("sv-SE") }, (new SeriesOptions { Id = "s", Name = "Försäljning", Max = 10, Target = 8 }, One(5)));

        Assert.Equal("5 (Mål 8)", layout.Tooltip.Series[0].Values[0]);
        Assert.StartsWith("Bulletdiagram med 1 post: Försäljning 5", layout.Scene.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Targets_follow_their_points_when_sorted_again()
    {
        var times = new[] { new DateTime(2026, 3, 3), new DateTime(2026, 3, 1), new DateTime(2026, 3, 2) };
        var data = SeriesData.From(times, [3.0, 1, 2]).WithTargets([30.0, 10, 20]).WithColors([null, "#f00", null]);

        Assert.Equal([1.0, 2, 3], data.Y);
        Assert.Equal([10.0, 20, 30], data.Targets!);
    }

    // ---------- JSON definitions ----------

    private static Dictionary<string, ChartDataset> Shops() => new()
    {
        ["shops"] = ChartDataset.FromRows("shops", new (string Region, double Sales, double Goal)[] { ("North", 90, 80), ("South", 40, 70), ("North", 92, 80), ("South", 56, 60) }
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["region"] = r.Region, ["sales"] = r.Sales, ["goal"] = r.Goal })),
    };

    private static DefinitionBindResult Bind(string json)
    {
        var parsed = ChartDefinitionJson.Parse(json);
        Assert.True(parsed.Success, string.Join("; ", parsed.Errors.Select(e => e.Message)));
        return ChartDefinitionBinder.Bind(parsed.Definition!, Shops());
    }

    [Fact]
    public void A_json_bullet_per_region_aggregates_values_and_targets()
    {
        var bound = Bind("""{ "dataset": "shops", "series": [ { "type": "bulletGraph", "x": "region", "y": "sales", "targetField": "goal", "aggregate": "sum" } ] }""");
        Assert.True(bound.Success, string.Join("; ", bound.Errors.Select(e => e.Message)));
        var series = Assert.Single(bound.Definition.Series);
        Assert.Equal(SeriesType.Bullet, series.Type);
        var data = bound.Data[series.Id];
        Assert.Equal([182.0, 96], data.Y);
        Assert.Equal([160.0, 130], data.Targets!);

        var layout = ChartLayout.Compute(bound.Definition, bound.Data, Options);
        Assert.Equal(["182 (Target 160)", "96 (Target 130)"], layout.Tooltip.Series[0].Values);
    }

    [Fact]
    public void A_json_bullet_without_x_shows_one_value()
    {
        var bound = Bind("""{ "dataset": "shops", "series": [ { "type": "bullet", "y": "sales", "targetField": "goal", "aggregate": "average", "filter": [ { "field": "region", "op": "eq", "value": "South" } ] } ] }""");
        Assert.True(bound.Success, string.Join("; ", bound.Errors.Select(e => e.Message)));
        var data = bound.Data[bound.Definition.Series[0].Id];
        Assert.Equal(48, data.Y[0]);
        Assert.Equal(65, data.Targets![0]);

        var ignored = Bind("""{ "dataset": "shops", "series": [ { "type": "line", "x": "region", "y": "sales", "targetField": "goal" } ] }""");
        Assert.Contains(ignored.Warnings, w => w.Contains("only bullet series", StringComparison.Ordinal));
    }

    [Fact]
    public void Razor_for_a_bullet_gives_its_target()
    {
        var perRegion = ChartRazor.FromDefinition(ChartDefinitionJson.Parse(
            """{ "dataset": "shops", "series": [ { "type": "bullet", "x": "region", "y": "sales", "targetField": "goal", "max": 200 } ] }""").Definition!);
        Assert.Contains("<BulletSeries XValue=\"r => r.Region\"", perRegion, StringComparison.Ordinal);
        Assert.Contains(" TargetValue=\"r => r.Goal\"", perRegion, StringComparison.Ordinal);
        Assert.Contains(" Max=\"200\"", perRegion, StringComparison.Ordinal);

        var single = ChartRazor.FromDefinition(ChartDefinitionJson.Parse(
            """{ "dataset": "shops", "series": [ { "type": "bullet", "y": "sales", "target": 80 } ] }""").Definition!);
        Assert.Contains("<BulletSeries Value=\"@value\"", single, StringComparison.Ordinal);
        Assert.Contains(" Target=\"80\"", single, StringComparison.Ordinal);
    }

    private sealed class ToleranceComparer(double tolerance) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) <= tolerance;

        public int GetHashCode(double obj) => 0;
    }
}
