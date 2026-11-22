using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;
using RioCharts.Core.Specs;

namespace RioCharts.Tests;

public class TreemapTests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 400, Culture = CultureInfo.InvariantCulture };

    // Population in millions per continent and country (rounded).
    private static readonly string[] Paths =
    [
        "Europe/Germany", "Europe/France", "Europe/Italy", "Europe/Spain",
        "Asia/India", "Asia/China", "Asia/Japan",
        "Americas/USA", "Americas/Brazil",
    ];

    private static readonly double[] People = [84, 68, 59, 48, 1430, 1410, 124, 335, 216];

    private static ChartSpec Spec(SeriesSpec? series = null) => new() { Series = [series ?? Tree()] };

    private static SeriesSpec Tree(double? groupSmall = null) => new() { Id = "p", Name = "Population", Type = SeriesType.Treemap, Format = "0 M", GroupSmallSlices = groupSmall };

    private static Dictionary<string, SeriesData> Data(string[]? paths = null, double[]? values = null) => new()
    {
        ["p"] = SeriesData.FromCategories(paths ?? Paths, values ?? People),
    };

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    private static List<RectNode> Cells(ChartLayoutResult result) =>
        All<RectNode>(result.Scene.Series).Where(r => r.Class?.Contains("rio-treemap-cell", StringComparison.Ordinal) == true).ToList();

    [Fact]
    public void Squarify_fills_the_rectangle_with_areas_in_proportion()
    {
        var rect = new RectF(10, 20, 600, 400);
        double[] values = [6, 6, 4, 3, 2, 2, 1];

        var rects = ChartLayout.Squarify(values, rect);

        var area = rect.Width * rect.Height;
        for (var i = 0; i < values.Length; i++)
        {
            Assert.Equal(values[i] / values.Sum() * area, rects[i].Width * rects[i].Height, area * 1e-4);
            Assert.True(rects[i].X >= rect.X - 0.01 && rects[i].Right <= rect.Right + 0.01 && rects[i].Y >= rect.Y - 0.01 && rects[i].Bottom <= rect.Bottom + 0.01);
            Assert.True(Math.Max(rects[i].Width / rects[i].Height, rects[i].Height / rects[i].Width) < 4); // squarish
        }
    }

    [Fact]
    public void Paths_become_groups_with_headers_and_a_legend()
    {
        var result = ChartLayout.Compute(Spec(), Data(), Options);

        Assert.Equal(9, Cells(result).Count);
        Assert.Equal(3, All<RectNode>(result.Scene.Series).Count(r => r.Class == "rio-treemap-group"));
        Assert.Contains(All<TextNode>(result.Scene.Series), t => t.Text == "Asia" && t.Class == "rio-treemap-header");
        Assert.Equal(["Europe", "Asia", "Americas"], result.Legend.Select(l => l.SeriesId));
        Assert.Contains("India\t1430 M", result.Legend[1].Tooltip, StringComparison.Ordinal);
        // Each group is keyed like its legend item, so hovering that item fades the others.
        var branches = All<GroupNode>(result.Scene.Series).Where(g => g.Key?.StartsWith("series:", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(["series:Americas", "series:Asia", "series:Europe"], branches.Select(g => g.Key!).Order());
        Assert.Equal(4, branches.Single(g => g.Key == "series:Europe").Children.OfType<RectNode>().Count(r => r.Class == "rio-treemap-cell"));
        Assert.Contains(All<GroupNode>(result.Scene.Series), g => g.Key == "labels:Asia");
        Assert.Contains("rio-swatch", All<GroupNode>(result.Scene.Static).SelectMany(g => g.Children).Select(c => c.Class ?? ""));
    }

    [Fact]
    public void The_tooltip_shows_the_path_and_the_shares()
    {
        var result = ChartLayout.Compute(Spec(), Data(), Options);

        var tip = Assert.Single(result.Tooltip.Series);
        var india = Array.IndexOf(tip.XLabels, "Asia › India");
        Assert.True(india >= 0);
        Assert.Equal(4, tip.Index[india]); // the data index of the item
        Assert.Equal("1430 M", tip.Values[india]);
        Assert.Equal(["48%", "38%"], tip.PartValues![india]); // of Asia, of the total
        Assert.Contains("Asia", tip.XLabels); // groups too, after the items
        Assert.Equal(-1, tip.Index[Array.IndexOf(tip.XLabels, "Asia")]);
        Assert.NotNull(tip.CellHeights);
        Assert.Equal(tip.PointColors![india], tip.PointColors[Array.IndexOf(tip.XLabels, "Asia › China")]); // colored by group
        Assert.NotEqual(tip.PointColors[india], tip.PointColors[Array.IndexOf(tip.XLabels, "Europe › Spain")]);
        Assert.Contains("Treemap", result.Scene.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Areas_follow_the_values()
    {
        var result = ChartLayout.Compute(Spec(), Data(), Options);

        var tip = result.Tooltip.Series[0];
        double Area(string label)
        {
            var i = Array.IndexOf(tip.XLabels, label);
            return tip.CellWidths![i] * tip.CellHeights![i];
        }

        Assert.Equal(1430.0 / 1410, Area("Asia › India") / Area("Asia › China"), 0.02);
        Assert.True(Area("Asia") > Area("Europe"));
    }

    [Fact]
    public void Zero_and_negative_values_are_left_out_and_noted()
    {
        var result = ChartLayout.Compute(Spec(), Data(["A/x", "A/y", "B/z", "B/w"], [5, 0, -3, 2]), Options);

        Assert.Equal(2, Cells(result).Count);
        Assert.Contains(result.Warnings, w => w.Contains("left out", StringComparison.Ordinal));
        Assert.Contains("2 items with zero or negative values left out", result.Scene.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Hiding_a_group_leaves_it_out()
    {
        var some = ChartLayout.Compute(Spec(), Data(), Options with { HiddenSeries = new HashSet<string> { "Asia" } });
        var all = ChartLayout.Compute(Spec(), Data(), Options with { HiddenSeries = new HashSet<string> { "Asia", "Europe", "Americas" } });

        Assert.Equal(6, Cells(some).Count);
        Assert.False(some.Legend[1].Visible);
        Assert.DoesNotContain(some.Tooltip.Series[0].XLabels, l => l.StartsWith("Asia", StringComparison.Ordinal));
        Assert.False(all.HasData);
        Assert.Contains(All<TextNode>(all.Scene.Overlay), t => t.Text == "All slices are hidden");
    }

    [Fact]
    public void A_flat_list_is_one_level_without_a_legend()
    {
        var result = ChartLayout.Compute(Spec(), Data(["Web", "App", "Store", "Phone"], [50, 30, 15, 5]), Options);

        Assert.Equal(4, Cells(result).Count);
        Assert.DoesNotContain(All<RectNode>(result.Scene.Series), r => r.Class == "rio-treemap-group");
        Assert.DoesNotContain(All<GroupNode>(result.Scene.Static), g => g.Key?.StartsWith("legend:", StringComparison.Ordinal) == true);
        Assert.Equal(4, result.Tooltip.Series[0].PointColors!.Distinct().Count());
        Assert.Contains(All<TextNode>(result.Scene.Series), t => t.Text == "Web");
    }

    [Fact]
    public void Small_items_are_grouped_per_group()
    {
        var result = ChartLayout.Compute(Spec(Tree(groupSmall: 0.2)), Data(["A/big", "A/s1", "A/s2", "A/s3", "B/only"], [90, 4, 3, 3, 50]), Options);

        Assert.Single(Cells(result), c => c.Class!.Contains("rio-treemap-other", StringComparison.Ordinal));
        Assert.Contains("A › Other", result.Tooltip.Series[0].XLabels);
        Assert.Equal(3, Cells(result).Count); // big, Other, only
    }

    [Fact]
    public void Odd_data_renders_a_clean_state()
    {
        var empty = ChartLayout.Compute(Spec(), new Dictionary<string, SeriesData>(), Options);
        var single = ChartLayout.Compute(Spec(), Data(["Only"], [3]), Options);
        var repeated = ChartLayout.Compute(Spec(), Data(["A/x", "A/x", "A"], [1, 2, 7]), Options);
        var mixed = ChartLayout.Compute(new ChartSpec { Series = [Tree(), new SeriesSpec { Id = "l", Type = SeriesType.Line }] }, Data(), Options);
        var tiny = ChartLayout.Compute(Spec(), Data(), Options with { Width = 60, Height = 50 });

        Assert.False(empty.HasData);
        Assert.Single(All<TextNode>(empty.Scene.Overlay));
        Assert.Single(Cells(single));
        Assert.Equal("3 M", single.Tooltip.Series[0].Values[0]);
        Assert.Equal("3 M", repeated.Tooltip.Series[0].Values[0]); // duplicates added, the group's own value ignored
        Assert.Contains(repeated.Warnings, w => w.Contains("more than once", StringComparison.Ordinal));
        Assert.Contains(repeated.Warnings, w => w.Contains("value of their own", StringComparison.Ordinal));
        Assert.Contains(mixed.Warnings, w => w.Contains("'l'", StringComparison.Ordinal));
        Assert.True(tiny.HasData);
    }

    [Fact]
    public void Drilling_into_a_group_shows_only_it_with_a_path_bar()
    {
        var result = ChartLayout.Compute(Spec(), Data(), Options with { TreemapPath = "Asia", InteractiveTreemap = true });

        Assert.Equal("Asia", result.TreemapPath);
        Assert.Equal(3, Cells(result).Count); // India, China, Japan
        Assert.DoesNotContain(All<GroupNode>(result.Scene.Static), g => g.Key?.StartsWith("legend:", StringComparison.Ordinal) == true);
        var crumbs = All<GroupNode>(result.Scene.Static).Single(g => g.Class == "rio-treemap-path");
        Assert.Equal(["All", "\u203a", "Asia"], All<TextNode>(crumbs).Select(t => t.Text));
        var back = All<GroupNode>(crumbs).Single(g => g.Interaction is not null);
        Assert.Equal(("treemap", ""), (back.Interaction!.Kind, back.Interaction.Id));
        var colors = result.Tooltip.Series[0].PointColors!;
        Assert.Equal(3, colors.Distinct().Count()); // the group's children pick the colors now
        Assert.Equal("1430 M", result.Tooltip.Series[0].Values[0]);
        Assert.Contains("Treemap, Asia", result.Scene.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Groups_are_buttons_that_drill_in_when_interactive()
    {
        var plain = ChartLayout.Compute(Spec(), Data(), Options);
        var live = ChartLayout.Compute(Spec(), Data(), Options with { InteractiveTreemap = true });

        Assert.DoesNotContain(All<GroupNode>(plain.Scene.Series), g => g.Interaction is not null);
        var drill = All<GroupNode>(live.Scene.Series).Where(g => g.Interaction?.Kind == "treemap").ToList();
        Assert.Equal(["Americas", "Asia", "Europe"], drill.Select(g => g.Interaction!.Id).Order());
        Assert.Equal("Show Asia", drill.Single(g => g.Interaction!.Id == "Asia").AriaLabel);
    }

    [Fact]
    public void When_items_do_not_click_the_whole_group_drills()
    {
        var result = ChartLayout.Compute(Spec(), Data(), Options with { InteractiveTreemap = true, TreemapDrillOnItems = true });

        var drill = All<GroupNode>(result.Scene.Series).Where(g => g.Interaction?.Kind == "treemap").ToList();
        Assert.Equal(3, drill.Count);
        var asia = drill.Single(g => g.Interaction!.Id == "Asia");
        Assert.Equal("series:Asia", asia.Key); // the branch itself: its tint and all its items
        Assert.Equal(3, asia.Children.OfType<RectNode>().Count(r => r.Class == "rio-treemap-cell"));
        Assert.StartsWith("Show Asia", asia.AriaLabel, StringComparison.Ordinal);
        Assert.DoesNotContain(All<GroupNode>(asia).Skip(1), g => g.Interaction is not null); // no nested buttons
    }

    [Fact]
    public void VisibleLevels_draws_deeper_groups_as_one_cell()
    {
        string[] paths = ["Home/Photos/2024", "Home/Photos/2025", "Home/Music", "Work/Projects/Web", "Work/Projects/App", "Work/Mail"];
        double[] sizes = [96, 132, 48, 40, 34, 18];
        var spec = Spec(Tree() with { VisibleLevels = 2 });

        var top = ChartLayout.Compute(spec, Data(paths, sizes), Options with { InteractiveTreemap = true });
        var drilled = ChartLayout.Compute(spec, Data(paths, sizes), Options with { TreemapPath = "Home", InteractiveTreemap = true });
        var all = ChartLayout.Compute(Spec(), Data(paths, sizes), Options);

        // Two levels: Home and Work, then Photos and Projects as cells (with their totals), Music and Mail as items.
        Assert.Equal(4, Cells(top).Count);
        var collapsed = Cells(top).Where(c => c.Class!.Contains("rio-treemap-collapsed", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, collapsed.Count);
        var tip = top.Tooltip.Series[0];
        var photos = Array.IndexOf(tip.XLabels, "Home › Photos");
        Assert.Equal(("228 M", -1), (tip.Values[photos], tip.Index[photos])); // a group: not an item to click
        Assert.DoesNotContain("Home › Photos › 2025", tip.XLabels);
        // With items clickable (no DrillOnItems) the collapsed cell drills into itself.
        Assert.Contains(All<GroupNode>(top.Scene.Series), g => g.Interaction?.Id == "Home/Photos");
        // Drilled into Home, two levels go one deeper: Photos is a group again, its years items.
        Assert.Contains("Home › Photos › 2025", drilled.Tooltip.Series[0].XLabels);
        Assert.Equal(6, Cells(all).Count); // no limit: every item
    }

    [Fact]
    public void A_json_treemap_takes_visibleLevels()
    {
        var parsed = ChartSpecJson.Parse("""{ "dataset": "d", "series": [ { "type": "treemap", "x": "path", "y": "size", "visibleLevels": 1 } ] }""");

        Assert.Equal(1, parsed.Spec!.Series[0].VisibleLevels);
        Assert.Contains("VisibleLevels=\"1\"", ChartRazor.FromSpec(parsed.Spec!, "Row", "rows"), StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_groups_and_unknown_paths()
    {
        string[] paths = ["Home/Photos/2024", "Home/Photos/2025", "Home/Music", "Work/Projects"];
        double[] sizes = [96, 132, 48, 74];

        var nested = ChartLayout.Compute(Spec(), Data(paths, sizes), Options with { TreemapPath = "Home/Photos", InteractiveTreemap = true });
        var unknown = ChartLayout.Compute(Spec(), Data(paths, sizes), Options with { TreemapPath = "Home/Videos" });
        var leaf = ChartLayout.Compute(Spec(), Data(paths, sizes), Options with { TreemapPath = "Work/Projects" });
        var hidden = ChartLayout.Compute(Spec(), Data(paths, sizes), Options with { TreemapPath = "Work", HiddenSeries = new HashSet<string> { "Work" } });

        Assert.Equal("Home/Photos", nested.TreemapPath);
        Assert.Equal(2, Cells(nested).Count);
        var crumbs = All<GroupNode>(nested.Scene.Static).Single(g => g.Class == "rio-treemap-path");
        Assert.Equal(["", "Home"], All<GroupNode>(crumbs).Where(g => g.Interaction is not null).Select(g => g.Interaction!.Id));
        Assert.Null(unknown.TreemapPath);
        Assert.Equal(4, Cells(unknown).Count);
        Assert.Null(leaf.TreemapPath); // an item, not a group
        Assert.Null(hidden.TreemapPath);
    }

    [Fact]
    public void A_custom_separator_splits_the_paths()
    {
        var spec = Spec(Tree() with { PathSeparator = " > " });

        var result = ChartLayout.Compute(spec, Data(["Europe > Sweden", "Europe > Norway"], [10, 5]), Options);

        Assert.Contains("Europe › Sweden", result.Tooltip.Series[0].XLabels);
    }

    [Fact]
    public void A_json_treemap_joins_its_path_fields()
    {
        var rows = new[] { ("Europe", "Germany", 84.0), ("Europe", "France", 68.0), ("Asia", "India", 1430.0) }
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["continent"] = r.Item1, ["country"] = r.Item2, ["people"] = r.Item3 });
        var parsed = ChartSpecJson.Parse("""{ "dataset": "d", "series": [ { "type": "treemap", "path": ["continent", "country"], "y": "people" } ] }""");

        var bound = ChartSpecBinder.Bind(parsed.Spec!, new Dictionary<string, ChartDataset> { ["d"] = ChartDataset.FromRows("d", rows) });

        Assert.Empty(bound.Errors);
        Assert.Equal(SeriesType.Treemap, bound.Spec.Series.Single().Type);
        var result = ChartLayout.Compute(bound.Spec, bound.Data, Options);
        Assert.Contains("Europe › France", result.Tooltip.Series[0].XLabels);
        var razor = ChartRazor.FromSpec(parsed.Spec!, "Row", "rows");
        Assert.Contains("<TreemapSeries", razor, StringComparison.Ordinal);
        Assert.Contains("XValue=\"r => $\"{r.Continent}/{r.Country}\"\"", razor, StringComparison.Ordinal);
    }

    [Fact]
    public void A_json_treemap_with_a_missing_path_field_says_which()
    {
        var rows = new[] { (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["country"] = "Germany", ["people"] = 84.0 } };
        var parsed = ChartSpecJson.Parse("""{ "dataset": "d", "series": [ { "type": "treemap", "path": ["continent", "country"], "y": "people" } ] }""");

        var bound = ChartSpecBinder.Bind(parsed.Spec!, new Dictionary<string, ChartDataset> { ["d"] = ChartDataset.FromRows("d", rows) });

        Assert.Equal("series[0].path[0]", Assert.Single(bound.Errors).Path);
    }
}
