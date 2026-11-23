using System.Globalization;

namespace RioCharts.Core.Geo;

/// <summary>A country on a world map: its codes, name and outline.</summary>
public sealed class GeoRegion
{
    internal GeoRegion(string alpha2, string alpha3, string name, string continent, IReadOnlyList<GeoPolygon> polygons)
    {
        Alpha2 = alpha2;
        Alpha3 = alpha3;
        Name = name;
        Continent = WorldMap.FindContinent(continent);
        Polygons = polygons;
        var largest = polygons.MaxBy(p => p.Area)!;
        MainPolygons = [.. polygons.Where(p => p == largest || p.Area >= largest.Area * 0.05
            && Math.Abs(p.CenterLongitude - largest.CenterLongitude) <= 30 && Math.Abs(p.CenterLatitude - largest.CenterLatitude) <= 20)];
    }

    /// <summary>ISO 3166-1 alpha-2 code, e.g. "SE"; empty for the few regions without one.</summary>
    public string Alpha2 { get; }

    /// <summary>ISO 3166-1 alpha-3 code, e.g. "SWE" (Natural Earth's own code for regions without one).</summary>
    public string Alpha3 { get; }

    /// <summary>The region's id on a map: <see cref="Alpha2"/>, or <see cref="Alpha3"/> when it has none.</summary>
    public string Code => Alpha2.Length > 0 ? Alpha2 : Alpha3;

    /// <summary>English name, e.g. "Sweden".</summary>
    public string Name { get; }

    /// <summary>The continent a map zooms to before the country, e.g. Europe; null for Antarctica and remote islands.</summary>
    public GeoContinent? Continent { get; }

    /// <summary>The outline: one polygon per landmass.</summary>
    public IReadOnlyList<GeoPolygon> Polygons { get; }

    /// <summary>
    /// The polygons a zoomed map fits: the largest and those of a similar size near it, so France
    /// zooms to Europe without French Guiana and the United States to the lower 48.
    /// </summary>
    public IReadOnlyList<GeoPolygon> MainPolygons { get; }
}

/// <summary>A landmass: an outer ring and its holes, each ring as longitude, latitude pairs in degrees.</summary>
public sealed class GeoPolygon
{
    internal GeoPolygon(IReadOnlyList<double[]> rings)
    {
        Rings = rings;
        var outer = rings[0];
        double minLon = double.MaxValue, maxLon = double.MinValue, minLat = double.MaxValue, maxLat = double.MinValue, area = 0;
        for (var i = 0; i < outer.Length; i += 2)
        {
            minLon = Math.Min(minLon, outer[i]);
            maxLon = Math.Max(maxLon, outer[i]);
            minLat = Math.Min(minLat, outer[i + 1]);
            maxLat = Math.Max(maxLat, outer[i + 1]);
            var j = (i + 2) % outer.Length;
            area += outer[i] * outer[j + 1] - outer[j] * outer[i + 1];
        }

        West = minLon;
        East = maxLon;
        South = minLat;
        North = maxLat;
        CenterLongitude = (minLon + maxLon) / 2;
        CenterLatitude = (minLat + maxLat) / 2;
        Area = Math.Abs(area / 2) * Math.Cos(CenterLatitude * Math.PI / 180);
    }

    /// <summary>The rings, the outer one first: longitude, latitude, longitude, latitude, … in degrees.</summary>
    public IReadOnlyList<double[]> Rings { get; }

    /// <summary>The outer ring's bounds, in degrees.</summary>
    public double West { get; }

    /// <summary>The outer ring's bounds, in degrees.</summary>
    public double East { get; }

    /// <summary>The outer ring's bounds, in degrees.</summary>
    public double South { get; }

    /// <summary>The outer ring's bounds, in degrees.</summary>
    public double North { get; }

    /// <summary>The middle of the outer ring's bounds, in degrees.</summary>
    public double CenterLongitude { get; }

    /// <summary>The middle of the outer ring's bounds, in degrees.</summary>
    public double CenterLatitude { get; }

    /// <summary>Approximate area in square degrees at the equator, for comparing landmasses.</summary>
    public double Area { get; }
}

/// <summary>
/// A continent as a map shows it: its name and the area a zoomed map fits, in degrees. The areas are
/// drawn for the map, not borders: Europe ends at the Urals, though Natural Earth counts all of
/// Russia as European.
/// </summary>
/// <param name="Name">English name, e.g. "Europe".</param>
public sealed record GeoContinent(string Name, double West, double South, double East, double North)
{
    /// <summary>The meridian in the middle of the area.</summary>
    public double CenterLongitude => (West + East) / 2;
}

/// <summary>
/// The countries of the world, from Natural Earth's 1:110m Admin 0 countries (public domain).
/// Small countries such as Singapore and Malta are not in it at this scale.
/// </summary>
public static class WorldMap
{
    private static readonly Lazy<IReadOnlyList<GeoRegion>> AllCountries = new(Load);
    private static readonly Lazy<Dictionary<string, GeoRegion>> Index = new(BuildIndex);

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UK"] = "GB",
        ["Great Britain"] = "GB",
        ["United States"] = "US",
        ["USA"] = "US",
        ["Czech Republic"] = "CZ",
        ["Ivory Coast"] = "CI",
        ["Côte d'Ivoire"] = "CI",
        ["Russian Federation"] = "RU",
        ["South Korea"] = "KR",
        ["Korea, Republic of"] = "KR",
        ["North Korea"] = "KP",
        ["Republic of the Congo"] = "CG",
        ["Democratic Republic of the Congo"] = "CD",
        ["Bosnia and Herzegovina"] = "BA",
        ["North Macedonia"] = "MK",
        ["Eswatini"] = "SZ",
        ["Swaziland"] = "SZ",
        ["Türkiye"] = "TR",
        ["Turkey"] = "TR",
    };

    /// <summary>Every country, sorted by code.</summary>
    public static IReadOnlyList<GeoRegion> Countries => AllCountries.Value;

    /// <summary>The continents a map zooms to: Africa, Asia, Europe, North America, Oceania and South America.</summary>
    public static IReadOnlyList<GeoContinent> Continents { get; } =
    [
        new("Africa", -19, -36, 53, 38),
        new("Asia", 25, -11, 150, 56),
        new("Europe", -25, 34, 45, 71),
        new("North America", -170, 6, -50, 75),
        new("Oceania", 110, -48, 180, 0),
        new("South America", -83, -56, -33, 13),
    ];

    /// <summary>The continent with this English name (ignoring case), e.g. "Europe"; null when there is none.</summary>
    public static GeoContinent? FindContinent(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : Continents.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The country with this ISO alpha-2 or alpha-3 code or English name (ignoring case), e.g. "SE",
    /// "SWE" or "Sweden"; null when there is none.
    /// </summary>
    public static GeoRegion? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        key = key.Trim();
        return Index.Value.GetValueOrDefault(key) ?? (Aliases.TryGetValue(key, out var code) ? Index.Value.GetValueOrDefault(code) : null);
    }

    /// <summary>
    /// The country a place is in, by latitude and longitude in degrees; at this scale coasts are
    /// coarse, so a place just off them (a harbor city) goes to the nearest country within about
    /// 150 km. Null at sea or out of reach.
    /// </summary>
    public static GeoRegion? Locate(double latitude, double longitude)
    {
        GeoRegion? nearest = null;
        var best = 1.5;
        var k = Math.Max(0.1, Math.Cos(latitude * Math.PI / 180));
        foreach (var region in Countries)
        {
            foreach (var polygon in region.Polygons)
            {
                if (latitude < polygon.South - best || latitude > polygon.North + best || longitude < polygon.West - best / k || longitude > polygon.East + best / k)
                {
                    continue;
                }

                if (Contains(polygon, longitude, latitude))
                {
                    return region;
                }

                var outer = polygon.Rings[0];
                for (var i = 0; i < outer.Length; i += 2)
                {
                    var d = Math.Sqrt(Math.Pow((outer[i] - longitude) * k, 2) + Math.Pow(outer[i + 1] - latitude, 2));
                    if (d < best)
                    {
                        best = d;
                        nearest = region;
                    }
                }
            }
        }

        return nearest;
    }

    private static bool Contains(GeoPolygon polygon, double x, double y)
    {
        var inside = false;
        foreach (var ring in polygon.Rings)
        {
            for (int k = 0, j = ring.Length - 2; k < ring.Length; j = k, k += 2)
            {
                if ((ring[k + 1] > y) != (ring[j + 1] > y) && x < (ring[j] - ring[k]) * (y - ring[k + 1]) / (ring[j + 1] - ring[k + 1]) + ring[k])
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    private static Dictionary<string, GeoRegion> BuildIndex()
    {
        var index = new Dictionary<string, GeoRegion>(StringComparer.OrdinalIgnoreCase);
        foreach (var region in Countries)
        {
            if (region.Alpha2.Length > 0)
            {
                index.TryAdd(region.Alpha2, region);
            }

            index.TryAdd(region.Alpha3, region);
            index.TryAdd(region.Name, region);
        }

        return index;
    }

    private static IReadOnlyList<GeoRegion> Load()
    {
        using var stream = typeof(WorldMap).Assembly.GetManifestResourceStream("RioCharts.Core.Geo.WorldCountries.txt")
            ?? throw new InvalidOperationException("The world map data is missing from RioCharts.Core.");
        using var reader = new StreamReader(stream);
        var regions = new List<GeoRegion>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var fields = line.Split('\t');
            var polygons = fields[4].Split(';').Select(polygon => new GeoPolygon([.. polygon.Split('|').Select(ParseRing)])).ToList();
            regions.Add(new GeoRegion(fields[0], fields[1], fields[2], fields[3], polygons));
        }

        return regions;
    }

    private static double[] ParseRing(string ring)
    {
        var points = ring.Split(' ');
        var coords = new double[points.Length * 2];
        for (var i = 0; i < points.Length; i++)
        {
            var comma = points[i].IndexOf(',');
            coords[i * 2] = double.Parse(points[i].AsSpan(0, comma), CultureInfo.InvariantCulture);
            coords[i * 2 + 1] = double.Parse(points[i].AsSpan(comma + 1), CultureInfo.InvariantCulture);
        }

        return coords;
    }
}
