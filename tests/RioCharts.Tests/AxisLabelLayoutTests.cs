using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Text;

namespace RioCharts.Tests;

/// <summary>Several y axes on one side, and category labels that do not fit side by side.</summary>
public class AxisLabelLayoutTests
{
    private static readonly LayoutOptions Options = new() { Width = 720, Height = 320, Culture = CultureInfo.InvariantCulture };

    private static readonly ApproximateTextMeasurer Measure = ApproximateTextMeasurer.Instance;

    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static List<TextNode> Texts(ChartLayoutResult r, string keyOrClass) =>
        All(r.Scene.Static).OfType<GroupNode>().Where(g => g.Key == keyOrClass || g.Class == keyOrClass)
            .SelectMany(g => g.Children.OfType<TextNode>()).ToList();

    private static List<TextNode> Titles(ChartLayoutResult r) =>
        All(r.Scene.Static).OfType<TextNode>().Where(t => t.Class == "rio-axis-title").ToList();

    /// <summary>Left and right edge of a horizontal text node.</summary>
    private static (double Left, double Right) Span(TextNode t)
    {
        var w = Measure.Measure(t.Text, 12);
        return t.Anchor switch
        {
            Anchor.Start => (t.At.X, t.At.X + w),
            Anchor.End => (t.At.X - w, t.At.X),
            _ => (t.At.X - w / 2, t.At.X + w / 2),
        };
    }

    private static ChartLayoutResult FourAxes(double width = 720)
    {
        var spec = new ChartDefinition
        {
            Y = new AxisOptions { Title = "Temperature °C" },
            YAxes =
            [
                new AxisOptions { Id = "a", Title = "Pressure kPa", Position = AxisPosition.Left },
                new AxisOptions { Id = "b", Title = "Energy kWh" },
                new AxisOptions { Id = "c", Title = "Flow l/s" },
            ],
            Series =
            [
                new SeriesOptions { Id = "s1" },
                new SeriesOptions { Id = "s2", YAxisId = "a" },
                new SeriesOptions { Id = "s3", YAxisId = "b" },
                new SeriesOptions { Id = "s4", YAxisId = "c" },
            ],
        };
        var data = new Dictionary<string, SeriesData>
        {
            ["s1"] = SeriesData.From([0.0, 1, 2], [20.0, 21, 22]),
            ["s2"] = SeriesData.From([0.0, 1, 2], [1000.0, 1200, 1300]),
            ["s3"] = SeriesData.From([0.0, 1, 2], [5.0, 8, 9]),
            ["s4"] = SeriesData.From([0.0, 1, 2], [50000.0, 80000, 90000]),
        };
        return ChartLayout.Compute(spec, data, Options with { Width = width });
    }

    [Theory]
    [InlineData(720)]
    [InlineData(400)]
    public void Axis_titles_on_the_same_side_do_not_overlap(double width)
    {
        var result = FourAxes(width);

        var titles = Titles(result).Select(Span).OrderBy(s => s.Left).ToList();
        Assert.Equal(4, titles.Count);
        for (var i = 1; i < titles.Count; i++)
        {
            Assert.True(titles[i].Left >= titles[i - 1].Right, $"title {i} starts at {titles[i].Left}, the one before ends at {titles[i - 1].Right}");
        }

        Assert.All(titles, t => Assert.True(t.Left >= 0 && t.Right <= width));
    }

    [Fact]
    public void Tick_label_columns_on_the_same_side_do_not_overlap()
    {
        var result = FourAxes();

        (double Left, double Right) Column(string key)
        {
            var spans = Texts(result, "axis:" + key).Select(Span).ToList();
            return (spans.Min(s => s.Left), spans.Max(s => s.Right));
        }

        var (y, a, b, c) = (Column("y"), Column("a"), Column("b"), Column("c"));
        Assert.True(a.Right < y.Left); // the extra left axis sits outside the primary one
        Assert.True(y.Right < result.Plot.X);
        Assert.True(b.Left > result.Plot.Right);
        Assert.True(c.Left > b.Right);
    }

    [Fact]
    public void A_single_axis_per_side_keeps_the_width_of_its_labels()
    {
        // The title of the axis next to the plot runs on over the plot, as before: no extra margin.
        var spec = new ChartDefinition { Y = new AxisOptions { Title = "A rather long axis title" }, Series = [new SeriesOptions { Id = "s" }] };
        var result = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["s"] = SeriesData.From([0.0, 1, 2], [1.0, 2, 3]) }, Options);

        Assert.True(result.Plot.X < 40);
    }

    private static readonly string[] Countries =
        ["Sweden", "Norway", "Denmark", "Finland", "Germany", "Netherlands", "Belgium", "France", "Spain", "Portugal",
         "Italy", "Switzerland", "Austria", "Poland", "Czech Republic", "United Kingdom", "Ireland", "Estonia"];

    private static ChartLayoutResult Bars(string[] names, double? rotation = null)
    {
        var spec = new ChartDefinition
        {
            X = new AxisOptions { LabelRotation = rotation },
            Series = [new SeriesOptions { Id = "s", Type = SeriesType.Bar }],
        };
        var data = new Dictionary<string, SeriesData> { ["s"] = SeriesData.FromCategories(names, names.Select((_, i) => 10.0 + i).ToArray()) };
        return ChartLayout.Compute(spec, data, Options with { Width = 640 });
    }

    [Fact]
    public void Category_labels_that_fit_stay_horizontal()
    {
        var labels = Texts(Bars(["North", "South", "East", "West"]), "rio-axis-x");

        Assert.Equal(4, labels.Count);
        Assert.All(labels, l => Assert.Equal(0, l.Rotation));
    }

    [Fact]
    public void Category_labels_that_do_not_fit_slope_and_all_show()
    {
        var result = Bars(Countries);

        var labels = Texts(result, "rio-axis-x");
        Assert.Equal(Countries.Length, labels.Count);
        Assert.All(labels, l => Assert.Equal(-45, l.Rotation));
        Assert.All(labels, l => Assert.True(l.At.Y > result.Plot.Bottom));

        // The plot gives up height for the label row, and the labels stay inside the chart.
        Assert.True(result.Plot.Bottom < 320 - 60);
        foreach (var l in labels)
        {
            var w = Measure.Measure(l.Text, 12);
            Assert.True(l.At.X - w * Math.Cos(Math.PI / 4) >= 0);
            Assert.True(l.At.Y + w * Math.Sin(Math.PI / 4) <= 320);
        }
    }

    [Fact]
    public void A_rotation_of_zero_keeps_horizontal_labels_and_thins_them()
    {
        var labels = Texts(Bars(Countries, rotation: 0), "rio-axis-x");

        Assert.InRange(labels.Count, 2, Countries.Length - 1);
        Assert.All(labels, l => Assert.Equal(0, l.Rotation));
    }

    [Fact]
    public void A_set_rotation_applies_even_when_labels_fit()
    {
        Assert.All(Texts(Bars(["North", "South"], rotation: 90), "rio-axis-x"), l => Assert.Equal(-90, l.Rotation));
    }

    [Fact]
    public void Rotated_labels_still_thin_out_when_even_they_overlap()
    {
        var names = Enumerable.Range(1, 200).Select(i => $"Item {i}").ToArray();

        var labels = Texts(Bars(names), "rio-axis-x");

        Assert.InRange(labels.Count, 2, 199);
        Assert.All(labels, l => Assert.Equal(-45, l.Rotation));
    }

    [Fact]
    public void Long_rotated_labels_are_shortened()
    {
        var names = Countries.Select(c => c + " and a very long description of the region that never ends").ToArray();

        var labels = Texts(Bars(names, rotation: 45), "rio-axis-x");

        Assert.All(labels, l => Assert.EndsWith("…", l.Text, StringComparison.Ordinal));
    }
}
