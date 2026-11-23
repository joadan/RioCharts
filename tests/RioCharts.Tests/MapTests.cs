using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Definitions;
using RioCharts.Core.Geo;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public class MapTests
{
    private static readonly LayoutOptions Options = new() { Width = 640, Height = 360, Culture = CultureInfo.InvariantCulture };

    private static List<T> All<T>(Node node) where T : Node
    {
        var found = node is T match ? new List<T> { match } : [];
        if (node is GroupNode g)
        {
            found.AddRange(g.Children.SelectMany(All<T>));
        }

        return found;
    }

    private static readonly SeriesData Countries = SeriesData.FromCategories(["SE", "NOR", "Germany", "US"], [120.0, 40, 60, 30]);

    private static readonly SeriesData Cities = SeriesData.FromCategories(["Stockholm", "Gothenburg", "Oslo", "New York"], [50.0, 30, 20, 10])
        .WithLocations([59.33, 57.71, 59.91, 40.71], [18.07, 11.97, 10.75, -74.01]);

    private static ChartLayoutResult Layout(SeriesData? countries = null, SeriesData? cities = null, LayoutOptions? options = null)
    {
        var series = new List<SeriesOptions>();
        var data = new Dictionary<string, SeriesData>();
        if (countries is not null)
        {
            series.Add(new SeriesOptions { Id = "m", Name = "Sessions", Type = SeriesType.Map });
            data["m"] = countries;
        }

        if (cities is not null)
        {
            series.Add(new SeriesOptions { Id = "b", Name = "Cities", Type = SeriesType.MapBubble });
            data["b"] = cities;
        }

        return ChartLayout.Compute(new ChartDefinition { Series = series }, data, options ?? Options);
    }

    [Fact]
    public void Countries_are_found_by_alpha2_alpha3_code_or_english_name()
    {
        Assert.Equal("SE", WorldMap.Find("se")!.Code);
        Assert.Equal("SE", WorldMap.Find("SWE")!.Code);
        Assert.Equal("SE", WorldMap.Find("Sweden")!.Code);
        Assert.Equal("US", WorldMap.Find("United States")!.Code);
        Assert.Null(WorldMap.Find("Atlantis"));
        Assert.Equal("Europe", WorldMap.Find("SE")!.Continent!.Name);
    }

    [Fact]
    public void A_place_is_located_in_its_country_also_just_off_a_coarse_coast()
    {
        Assert.Equal("SE", WorldMap.Locate(59.33, 18.07)!.Code);
        Assert.Equal("SE", WorldMap.Locate(57.71, 11.97)!.Code);
        Assert.Null(WorldMap.Locate(30, -40));
    }

    [Fact]
    public void Each_country_with_a_value_is_colored_and_shown_in_the_tooltip()
    {
        var layout = Layout(Countries);

        Assert.True(layout.HasData);
        Assert.Empty(layout.Warnings);
        var tips = Assert.Single(layout.Tooltip.Series);
        Assert.Equal(SeriesType.Map, tips.Type);
        Assert.Equal(["Germany", "Norway", "Sweden", "United States of America"], tips.XLabels.Order());
        Assert.Equal(4, tips.Shapes!.Length);
        Assert.Equal(tips.X.Order(), tips.X); // keyboard navigation steps by x
        Assert.Contains("Sweden 120", layout.Scene.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_items_for_one_country_add_up_without_a_repeat_warning()
    {
        var layout = Layout(SeriesData.FromCategories(["SE", "Sweden", "NO"], [10.0, 5, 3]));

        Assert.Empty(layout.Warnings);
        var tips = layout.Tooltip.Series[0];
        var sweden = Array.IndexOf(tips.XLabels, "Sweden");
        Assert.Equal("15", tips.Values[sweden]);
        Assert.Equal(-1, tips.Index[sweden]); // the sum is no one item's value
    }

    [Fact]
    public void Unknown_countries_are_reported_and_left_out()
    {
        var layout = Layout(SeriesData.FromCategories(["SE", "Atlantis"], [10.0, 5]));

        Assert.Contains(layout.Warnings, w => w.Contains("Atlantis", StringComparison.Ordinal));
        Assert.Single(layout.Tooltip.Series[0].X);
    }

    [Fact]
    public void Bubbles_add_up_per_country_on_the_world_map_and_split_when_zoomed_in()
    {
        var world = Layout(cities: Cities);
        var bubbles = Assert.Single(world.Tooltip.Series);
        Assert.Equal(["Norway", "Sweden", "United States of America"], bubbles.XLabels.Order());
        Assert.Equal("80", bubbles.Values[Array.IndexOf(bubbles.XLabels, "Sweden")]);
        Assert.Equal(-1, bubbles.Index[Array.IndexOf(bubbles.XLabels, "Sweden")]);

        var sweden = Layout(cities: Cities, options: Options with { MapRegion = "SE" });
        Assert.Equal("SE", sweden.MapRegion);
        Assert.Contains("Stockholm", sweden.Tooltip.Series[0].XLabels);
        Assert.Contains("Gothenburg", sweden.Tooltip.Series[0].XLabels);
        Assert.DoesNotContain("New York", sweden.Tooltip.Series[0].XLabels); // outside the view
    }

    [Fact]
    public void Bubbles_kept_apart_with_group_by_country_off()
    {
        var layout = ChartLayout.Compute(new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "b", Type = SeriesType.MapBubble, GroupByCountry = false }],
        }, new Dictionary<string, SeriesData> { ["b"] = Cities }, Options);

        Assert.Equal(4, layout.Tooltip.Series[0].X.Length);
    }

    [Fact]
    public void Clicks_zoom_one_level_from_world_to_continent_to_country()
    {
        static List<GroupNode> Buttons(ChartLayoutResult layout) =>
            All<GroupNode>(layout.Scene.Series).Where(g => g.Interaction?.Kind == "map").ToList();

        var interactive = Options with { InteractiveMap = true };
        var world = Layout(Countries, options: interactive);
        Assert.Null(world.MapRegion);
        Assert.All(Buttons(world).Where(g => g.AriaLabel == "Show Europe"), g => Assert.Equal("Europe", g.Interaction!.Id));
        Assert.Contains(Buttons(world), g => g.AriaLabel == "Show North America" && g.Interaction!.Focusable);

        var europe = Layout(Countries, options: interactive with { MapRegion = "europe" });
        Assert.Equal("Europe", europe.MapRegion);
        Assert.Contains(Buttons(europe), g => g.Interaction!.Id == "SE");

        var sweden = Layout(Countries, options: interactive with { MapRegion = "SWE" });
        Assert.Equal("SE", sweden.MapRegion);
        Assert.Equal("Europe", Buttons(sweden).Single(g => g.AriaLabel == "Show Europe").Interaction!.Id); // the country shown goes back up
        var crumbs = All<GroupNode>(sweden.Scene.Static).Where(g => g.Interaction?.Kind == "map").Select(g => g.Interaction!.Id).ToList();
        Assert.Equal(["", "Europe"], crumbs);
    }

    [Fact]
    public void Only_countries_with_data_are_tab_stops_and_none_are_canvas_series()
    {
        var layout = Layout(Countries, options: Options with { InteractiveMap = true });
        var buttons = All<GroupNode>(layout.Scene.Series).Where(g => g.Interaction?.Kind == "map").ToList();

        Assert.True(buttons.Count > 100);
        Assert.Equal(4, buttons.Count(g => g.Interaction!.Focusable));
        Assert.DoesNotContain(All<GroupNode>(layout.Scene.Series), g => g.Key?.StartsWith("series:", StringComparison.Ordinal) == true && g.Class!.Contains("rio-series-map", StringComparison.Ordinal));
    }

    [Fact]
    public void Bubble_series_get_a_legend_next_to_the_color_scale()
    {
        var layout = Layout(Countries, Cities);

        Assert.Contains(All<GroupNode>(layout.Scene.Static), g => g.Class == "rio-legend");
        Assert.Contains(All<GroupNode>(layout.Scene.Static), g => g.Class == "rio-legend rio-heat-legend");
        Assert.Equal(["m", "b"], layout.Legend.Select(e => e.SeriesId));
    }

    [Fact]
    public void An_empty_map_draws_the_world_without_throwing()
    {
        var layout = Layout(SeriesData.Empty);

        Assert.False(layout.HasData);
        Assert.NotEmpty(All<PathNode>(layout.Scene.Series));
    }

    // ---------- JSON definitions ----------

    private static Dictionary<string, ChartDataset> Places() => new()
    {
        ["places"] = ChartDataset.FromRows("places", new (string City, string Country, double Lat, double Lon, double Users)[]
            {
                ("Stockholm", "SE", 59.33, 18.07, 50), ("Gothenburg", "SE", 57.71, 11.97, 30), ("Oslo", "NO", 59.91, 10.75, 20),
            }
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["city"] = r.City, ["country"] = r.Country, ["lat"] = r.Lat, ["lon"] = r.Lon, ["users"] = r.Users,
            })),
    };

    private static DefinitionBindResult Bind(string json)
    {
        var parsed = ChartDefinitionJson.Parse(json);
        Assert.True(parsed.Success, string.Join("; ", parsed.Errors.Select(e => e.Message)));
        return ChartDefinitionBinder.Bind(parsed.Definition!, Places());
    }

    [Fact]
    public void A_json_map_sums_per_country_and_map_bubbles_read_latitude_and_longitude()
    {
        var bound = Bind("""
            { "dataset": "places", "series": [
              { "type": "choropleth", "x": "country", "y": "users" },
              { "type": "bubbleMap", "x": "city", "y": "users", "latitude": "lat", "longitude": "lon" } ] }
            """);

        Assert.True(bound.Success, string.Join("; ", bound.Errors.Select(e => e.Message)));
        Assert.Equal([SeriesType.Map, SeriesType.MapBubble], bound.Definition.Series.Select(s => s.Type));
        Assert.Equal([80.0, 20], bound.Data[bound.Definition.Series[0].Id].Y);
        var places = bound.Data[bound.Definition.Series[1].Id];
        Assert.Equal([59.33, 57.71, 59.91], places.Latitudes!);
        Assert.Equal([18.07, 11.97, 10.75], places.Longitudes!);
        Assert.DoesNotContain(bound.Warnings, w => w.StartsWith("series[1]", StringComparison.Ordinal)); // one bubble per row, not summed per name
    }

    [Fact]
    public void A_json_map_bubble_without_a_place_is_an_error()
    {
        var bound = Bind("""{ "dataset": "places", "series": [ { "type": "mapBubble", "x": "city", "y": "users", "latitude": "lat" } ] }""");

        Assert.Contains(bound.Errors, e => e.Path == "series[0].longitude");
    }

    [Fact]
    public void Razor_for_map_series_uses_the_map_components()
    {
        var razor = ChartRazor.FromDefinition(ChartDefinitionJson.Parse("""
            { "dataset": "places", "series": [
              { "type": "map", "x": "country", "y": "users" },
              { "type": "mapBubble", "x": "city", "y": "users", "latitude": "lat", "longitude": "lon", "groupByCountry": false } ] }
            """).Definition!);

        Assert.Contains("<MapSeries XValue=\"r => r.Country\"", razor, StringComparison.Ordinal);
        Assert.Contains("<MapBubbleSeries XValue=\"r => r.City\"", razor, StringComparison.Ordinal);
        Assert.Contains(" Latitude=\"r => r.Lat\" Longitude=\"r => r.Lon\"", razor, StringComparison.Ordinal);
        Assert.Contains(" GroupByCountry=\"false\"", razor, StringComparison.Ordinal);
    }
}
