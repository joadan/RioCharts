using System.Globalization;
using System.Text;
using RioCharts.Layout;

namespace RioCharts.Rendering;

/// <summary>
/// Builds SVG attribute values. All numbers use the invariant culture so output is
/// valid regardless of the current culture (e.g. "1.5", never "1,5").
/// </summary>
public static class Svg
{
    /// <summary>Formats a number for an SVG attribute (max two decimals, invariant culture).</summary>
    public static string N(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "0";
        }

        var rounded = Math.Round(value, 2);
        return (rounded == 0 ? 0 : rounded).ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>A polyline path through the points: "M x y L x y ...".</summary>
    public static string LinePath(IReadOnlyList<ScreenPoint> points)
    {
        if (points.Count == 0)
        {
            return "";
        }

        var sb = new StringBuilder(points.Count * 16);
        sb.Append('M').Append(N(points[0].X)).Append(' ').Append(N(points[0].Y));
        for (var i = 1; i < points.Count; i++)
        {
            sb.Append(" L").Append(N(points[i].X)).Append(' ').Append(N(points[i].Y));
        }

        return sb.ToString();
    }

    /// <summary>A closed area between the line and a horizontal baseline.</summary>
    public static string AreaPath(IReadOnlyList<ScreenPoint> points, double baselineY)
    {
        if (points.Count == 0)
        {
            return "";
        }

        var sb = new StringBuilder(LinePath(points));
        sb.Append(" L").Append(N(points[^1].X)).Append(' ').Append(N(baselineY));
        sb.Append(" L").Append(N(points[0].X)).Append(' ').Append(N(baselineY));
        sb.Append(" Z");
        return sb.ToString();
    }

    /// <summary>A closed polygon path.</summary>
    public static string PolygonPath(IReadOnlyList<ScreenPoint> points)
    {
        if (points.Count == 0)
        {
            return "";
        }

        return LinePath(points) + " Z";
    }

    /// <summary>
    /// A pie or donut slice. Angles are radians clockwise from 12 o'clock.
    /// A full circle is drawn as two half arcs (a single arc cannot start and end at the same point).
    /// </summary>
    public static string ArcPath(ScreenPoint center, double outerRadius, double innerRadius, double startAngle, double endAngle)
    {
        var sweep = endAngle - startAngle;
        if (sweep <= 0 || outerRadius <= 0)
        {
            return "";
        }

        if (sweep >= Math.PI * 2 - 1e-9)
        {
            var mid = startAngle + Math.PI;
            return ArcPath(center, outerRadius, innerRadius, startAngle, mid) + " " +
                   ArcPath(center, outerRadius, innerRadius, mid, startAngle + Math.PI * 2);
        }

        var largeArc = sweep > Math.PI ? 1 : 0;
        var o1 = Polar(center, outerRadius, startAngle);
        var o2 = Polar(center, outerRadius, endAngle);
        var sb = new StringBuilder();
        if (innerRadius > 0)
        {
            var i1 = Polar(center, innerRadius, endAngle);
            var i2 = Polar(center, innerRadius, startAngle);
            sb.Append('M').Append(N(o1.X)).Append(' ').Append(N(o1.Y));
            sb.Append(" A").Append(N(outerRadius)).Append(' ').Append(N(outerRadius)).Append(" 0 ").Append(largeArc).Append(" 1 ")
              .Append(N(o2.X)).Append(' ').Append(N(o2.Y));
            sb.Append(" L").Append(N(i1.X)).Append(' ').Append(N(i1.Y));
            sb.Append(" A").Append(N(innerRadius)).Append(' ').Append(N(innerRadius)).Append(" 0 ").Append(largeArc).Append(" 0 ")
              .Append(N(i2.X)).Append(' ').Append(N(i2.Y));
            sb.Append(" Z");
        }
        else
        {
            sb.Append('M').Append(N(center.X)).Append(' ').Append(N(center.Y));
            sb.Append(" L").Append(N(o1.X)).Append(' ').Append(N(o1.Y));
            sb.Append(" A").Append(N(outerRadius)).Append(' ').Append(N(outerRadius)).Append(" 0 ").Append(largeArc).Append(" 1 ")
              .Append(N(o2.X)).Append(' ').Append(N(o2.Y));
            sb.Append(" Z");
        }

        return sb.ToString();
    }

    public static ScreenPoint Polar(ScreenPoint center, double radius, double angle) =>
        new(center.X + Math.Sin(angle) * radius, center.Y - Math.Cos(angle) * radius);

    /// <summary>Path for a marker centered at (x, y). Circles are drawn with &lt;circle&gt; instead.</summary>
    public static string MarkerPath(MarkerStyle style, double x, double y, double size)
    {
        var r = size / 2;
        return style switch
        {
            MarkerStyle.Square => $"M{N(x - r)} {N(y - r)} H{N(x + r)} V{N(y + r)} H{N(x - r)} Z",
            MarkerStyle.Triangle => $"M{N(x)} {N(y - r)} L{N(x + r)} {N(y + r)} L{N(x - r)} {N(y + r)} Z",
            MarkerStyle.Diamond => $"M{N(x)} {N(y - r)} L{N(x + r)} {N(y)} L{N(x)} {N(y + r)} L{N(x - r)} {N(y)} Z",
            MarkerStyle.Cross => $"M{N(x - r)} {N(y - r)} L{N(x + r)} {N(y + r)} M{N(x + r)} {N(y - r)} L{N(x - r)} {N(y + r)}",
            MarkerStyle.Plus => $"M{N(x - r)} {N(y)} H{N(x + r)} M{N(x)} {N(y - r)} V{N(y + r)}",
            _ => "",
        };
    }

    /// <summary>
    /// Text color with good contrast on the given background. Understands #rgb and #rrggbb;
    /// other color syntaxes fall back to white.
    /// </summary>
    public static string ContrastText(string? background)
    {
        if (background is null || !background.StartsWith('#'))
        {
            return "white";
        }

        var hex = background[1..];
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        }

        if (hex.Length < 6 || !int.TryParse(hex[..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return "white";
        }

        static double Channel(int v)
        {
            var c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var luminance = 0.2126 * Channel((rgb >> 16) & 0xFF) + 0.7152 * Channel((rgb >> 8) & 0xFF) + 0.0722 * Channel(rgb & 0xFF);

        // Pick whichever of white/near-black gives the higher WCAG contrast ratio.
        var contrastWhite = 1.05 / (luminance + 0.05);
        var contrastDark = (luminance + 0.05) / (0.0130 + 0.05); // #222222
        return contrastWhite >= contrastDark ? "white" : "#222222";
    }

    /// <summary>True for marker shapes drawn as strokes only (no fill).</summary>
    public static bool IsStrokeMarker(MarkerStyle style) => style is MarkerStyle.Cross or MarkerStyle.Plus;
}
