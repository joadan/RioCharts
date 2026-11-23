#:property PublishAot=false

// The country shapes of world maps (src/RioCharts.Core/Geo/WorldCountries.txt), from Natural
// Earth's 1:110m Admin 0 countries (public domain). Run from the repository root:
//   dotnet run tools/WorldMapData.cs
// One line per country: ISO 3166 alpha-2 code, alpha-3 code, English name, continent, then its polygons
// separated by ';', each an outer ring and its holes separated by '|', each ring "lon,lat lon,lat …"
// in degrees with two decimals.

using System.Globalization;
using System.Text;
using System.Text.Json;

const string source = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/v5.1.2/geojson/ne_110m_admin_0_countries.geojson";
var target = Path.Combine("src", "RioCharts.Core", "Geo", "WorldCountries.txt");

using var http = new HttpClient();
using var json = JsonDocument.Parse(await http.GetStringAsync(source));
var lines = new List<string>();
foreach (var feature in json.RootElement.GetProperty("features").EnumerateArray())
{
    var properties = feature.GetProperty("properties");
    string Text(string name) => properties.GetProperty(name).GetString() ?? "";
    var iso2 = Text("ISO_A2_EH");
    var iso3 = Text("ISO_A3_EH");
    var adm3 = Text("ADM0_A3");
    var geometry = feature.GetProperty("geometry");
    var polygons = geometry.GetProperty("type").GetString() == "MultiPolygon"
        ? geometry.GetProperty("coordinates").EnumerateArray().ToList()
        : [geometry.GetProperty("coordinates")];
    var shapes = string.Join(';', polygons.Select(polygon => string.Join('|', polygon.EnumerateArray().Select(Ring))));
    var continent = Text("CONTINENT");
    lines.Add(string.Join('\t', iso2 == "-99" ? "" : iso2, iso3 == "-99" ? adm3 : iso3, Text("NAME"), continent.StartsWith("Seven seas") ? "" : continent, shapes));
}

lines.Sort(StringComparer.Ordinal);
var header = "# Natural Earth 1:110m Admin 0 countries v5.1.2 (public domain), https://www.naturalearthdata.com. Made by tools/WorldMapData.cs.";
File.WriteAllText(target, header + "\n" + string.Join('\n', lines) + "\n", new UTF8Encoding(false));
Console.WriteLine($"{lines.Count} countries written to {target}");

static string Ring(JsonElement ring)
{
    var points = new List<string>();
    var last = "";
    foreach (var point in ring.EnumerateArray())
    {
        var lon = Math.Round(point[0].GetDouble(), 2).ToString(CultureInfo.InvariantCulture);
        var lat = Math.Round(point[1].GetDouble(), 2).ToString(CultureInfo.InvariantCulture);
        var text = lon + "," + lat;
        if (text != last)
        {
            points.Add(text);
            last = text;
        }
    }

    if (points.Count > 1 && points[0] == points[^1])
    {
        points.RemoveAt(points.Count - 1);
    }

    return string.Join(' ', points);
}
