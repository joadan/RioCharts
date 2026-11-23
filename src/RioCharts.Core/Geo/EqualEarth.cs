namespace RioCharts.Core.Geo;

/// <summary>
/// The Equal Earth projection (Šavrič, Patterson and Jenny, 2018): equal-area, so a country's size
/// on the map compares fairly, with straight parallels and continents that keep a familiar shape.
/// </summary>
public static class EqualEarth
{
    private const double A1 = 1.340264, A2 = -0.081106, A3 = 0.000893, A4 = 0.003796;
    private static readonly double M = Math.Sqrt(3) / 2;

    /// <summary>
    /// Projects a point given in degrees, with <paramref name="centralLongitude"/> in the middle.
    /// The result is in projection units, x to the east and y to the south (screen orientation):
    /// the world spans about ±2.71 in x and ±1.32 in y.
    /// </summary>
    /// <param name="longitude">Degrees east, relative to the central meridian after wrapping to ±180.</param>
    /// <param name="latitude">Degrees north.</param>
    /// <param name="centralLongitude">The meridian in the middle of the map.</param>
    public static (double X, double Y) Project(double longitude, double latitude, double centralLongitude = 0) =>
        ProjectRelative(Wrap(longitude - centralLongitude), latitude);

    /// <summary>Projects a point whose longitude is already relative to the central meridian (−180 to 180, not wrapped).</summary>
    internal static (double X, double Y) ProjectRelative(double longitude, double latitude)
    {
        var lambda = longitude * Math.PI / 180;
        var theta = Math.Asin(M * Math.Sin(Math.Clamp(latitude, -90, 90) * Math.PI / 180));
        var t2 = theta * theta;
        var t6 = t2 * t2 * t2;
        var x = lambda * Math.Cos(theta) / (M * (A1 + 3 * A2 * t2 + t6 * (7 * A3 + 9 * A4 * t2)));
        var y = theta * (A1 + A2 * t2 + t6 * (A3 + A4 * t2));
        return (x, -y);
    }

    /// <summary>A longitude difference wrapped to −180 to 180 degrees.</summary>
    internal static double Wrap(double degrees)
    {
        var wrapped = (degrees + 180) % 360;
        return (wrapped < 0 ? wrapped + 360 : wrapped) - 180;
    }
}
