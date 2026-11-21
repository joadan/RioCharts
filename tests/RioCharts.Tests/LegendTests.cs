using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class LegendTests
{
    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static readonly LayoutOptions Options = new() { Width = 640, Height = 360, Culture = CultureInfo.InvariantCulture };

    private static readonly ChartSpec Pie = new()
    {
        Series = [new SeriesSpec { Id = "p", Type = SeriesType.Pie, GroupSmallSlices = 0.03, Format = "0 MWh" }],
    };

    private static readonly Dictionary<string, SeriesData> Energy = new()
    {
        ["p"] = SeriesData.FromCategories(["Heating", "Ventilation", "Cooling", "Elevators", "Controls"], [500, 300, 185, 9, 6]),
    };

    [Fact]
    public void Grouped_slices_are_one_Other_item_in_the_legend()
    {
        var layout = ChartLayout.Compute(Pie, Energy, Options);

        Assert.Equal(["Heating", "Ventilation", "Cooling", "Other"], layout.Legend.Select(e => e.Label));
        var other = layout.Legend[^1];
        Assert.Equal(ChartLayout.OtherSliceKey, other.SeriesId);
        Assert.Equal(layout.Tooltip.Slices.Single(s => s.Label == "Other").Color, other.Color); // same color as the slice
        Assert.Equal("Other\t15 MWh (2%)\n• Elevators\t9 MWh (1%)\n• Controls\t6 MWh (1%)", other.Tooltip); // total, then its parts
    }

    [Fact]
    public void The_Other_slice_tooltip_lists_its_parts()
    {
        var layout = ChartLayout.Compute(Pie, Energy, Options);

        var other = layout.Tooltip.Slices.Single(s => s.Index < 0);
        Assert.Equal("15 MWh (2%)", other.Value);
        Assert.Equal([new TooltipPart("Elevators", "9 MWh (1%)"), new TooltipPart("Controls", "6 MWh (1%)")], other.Parts);
        Assert.All(layout.Tooltip.Slices.Where(s => s.Index >= 0), s => Assert.Empty(s.Parts));
    }

    [Fact]
    public void Other_can_be_hidden_from_the_legend_like_any_slice()
    {
        var layout = ChartLayout.Compute(Pie, Energy, Options with { HiddenSeries = new HashSet<string> { ChartLayout.OtherSliceKey } });

        Assert.DoesNotContain(layout.Tooltip.Slices, s => s.Label == "Other");
        var other = Assert.Single(layout.Legend, e => e.SeriesId == ChartLayout.OtherSliceKey);
        Assert.False(other.Visible); // still listed, so it can be shown again
        Assert.Equal(Math.PI * 2 * 500 / 985, layout.Tooltip.Slices[0].A1 - layout.Tooltip.Slices[0].A0, 6); // shares of the visible slices
    }

    [Fact]
    public void Without_grouping_every_slice_has_its_own_item()
    {
        var layout = ChartLayout.Compute(Pie with { Series = [Pie.Series[0] with { GroupSmallSlices = null }] }, Energy, Options);

        Assert.Equal(["Heating", "Ventilation", "Cooling", "Elevators", "Controls"], layout.Legend.Select(e => e.Label));
        Assert.Equal("Heating: 500 MWh (50%)", layout.Legend[0].Tooltip);
    }

    [Fact]
    public void Series_items_summarize_their_series()
    {
        var start = new DateTime(2026, 11, 16);
        var spec = new ChartSpec
        {
            Series = [new SeriesSpec { Id = "s", Name = "Supply", Format = "0.0 °C" }, new SeriesSpec { Id = "e", Name = "Extract" }],
        };
        var data = new Dictionary<string, SeriesData>
        {
            ["s"] = SeriesData.From(Enumerable.Range(0, 4).Select(i => start.AddHours(i)).ToArray(), [18.2, 19.5, 20.1, 20.8]),
            ["e"] = SeriesData.Empty,
        };

        var layout = ChartLayout.Compute(spec, data, Options);

        Assert.Equal("Supply: 18.2 °C to 20.8 °C, last 20.8 °C, rising", layout.Legend[0].Tooltip);
        Assert.Equal("Extract: No data available", layout.Legend[1].Tooltip);
        var item = All(layout.Scene.Static).OfType<GroupNode>().First(g => g.Key == "legend:s");
        Assert.Equal(layout.Legend[0].Tooltip, item.Title); // an SVG title in exports and static pages
    }

    [Fact]
    public void Radial_bar_items_show_the_value()
    {
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "h", Type = SeriesType.RadialBar, Max = 100, Format = "0'%'" }] };
        var data = new Dictionary<string, SeriesData> { ["h"] = SeriesData.FromCategories(["Supply fan", "Filter"], [87, 41]) };

        var layout = ChartLayout.Compute(spec, data, Options);

        Assert.Equal("Supply fan: 87%", layout.Legend[0].Tooltip);
    }
}
