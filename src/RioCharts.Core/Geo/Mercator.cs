namespace RioCharts.Core.Geo;

/// <summary>
/// The Mercator projection, as on web maps: shapes and directions are true locally, but areas grow
/// toward the poles (Greenland looks as large as Africa). Latitudes are cut at ±85.05°, like web maps.
/// </summary>
public static class Mercator
{
    /// <summary>The latitude, in degrees, at which the map is cut (where it becomes square, as on web maps).</summary>
    public const double MaxLatitude = 85.0511287798066;

    /// <summary>
    /// Projects a point given in degrees, with <paramref name="centralLongitude"/> in the middle.
    /// The result is in projection units, x to the east and y to the south (screen orientation):
    /// the world spans ±π in x and in y.
    /// </summary>
    /// <param name="longitude">Degrees east, relative to the central meridian after wrapping to ±180.</param>
    /// <param name="latitude">Degrees north, cut at ±<see cref="MaxLatitude"/>.</param>
    /// <param name="centralLongitude">The meridian in the middle of the map.</param>
    public static (double X, double Y) Project(double longitude, double latitude, double centralLongitude = 0) =>
        ProjectRelative(EqualEarth.Wrap(longitude - centralLongitude), latitude);

    /// <summary>Projects a point whose longitude is already relative to the central meridian (−180 to 180, not wrapped).</summary>
    internal static (double X, double Y) ProjectRelative(double longitude, double latitude)
    {
        var phi = Math.Clamp(latitude, -MaxLatitude, MaxLatitude) * Math.PI / 180;
        return (longitude * Math.PI / 180, -Math.Log(Math.Tan(Math.PI / 4 + phi / 2)));
    }
}
