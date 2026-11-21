using System.Globalization;
using System.Text;
using RioCharts.Core.Scene;

namespace RioCharts.Blazor.Rendering;

/// <summary>Converts scene values to SVG attribute strings (always invariant culture).</summary>
internal static class SvgWriter
{
    /// <summary>A number for an SVG attribute: at most two decimals, "." as separator, no negative zero.</summary>
    public static string N(double value)
    {
        if (!double.IsFinite(value))
        {
            return "0";
        }

        var rounded = Math.Round(value, 2);
        return (rounded == 0 ? 0 : rounded).ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>Theme tokens become CSS variables with the built-in default as fallback.</summary>
    public static string Color(string? color)
    {
        if (string.IsNullOrEmpty(color))
        {
            return "none";
        }

        return ThemeTokens.IsToken(color) ? $"var({color}, var(--_{color[2..]}))" : color;
    }

    /// <summary>The SVG "d" attribute for a path.</summary>
    public static string PathD(PathData path)
    {
        var sb = new StringBuilder(path.Commands.Count * 14);
        var c = 0;
        var hasCurrent = false;
        foreach (var command in path.Commands)
        {
            var k = path.Coords;
            switch (command)
            {
                case PathCommand.MoveTo:
                    Append(sb, 'M', k[c], k[c + 1]);
                    hasCurrent = true;
                    break;
                case PathCommand.LineTo:
                    Append(sb, 'L', k[c], k[c + 1]);
                    hasCurrent = true;
                    break;
                case PathCommand.CubicTo:
                    sb.Append(sb.Length > 0 ? " C" : "C");
                    for (var i = 0; i < 6; i += 2)
                    {
                        if (i > 0)
                        {
                            sb.Append(' ');
                        }

                        sb.Append(N(k[c + i])).Append(' ').Append(N(k[c + i + 1]));
                    }

                    hasCurrent = true;
                    break;
                case PathCommand.Arc:
                    AppendArc(sb, k[c], k[c + 1], k[c + 2], k[c + 3], k[c + 4], hasCurrent);
                    hasCurrent = true;
                    break;
                case PathCommand.Close:
                    sb.Append(sb.Length > 0 ? " Z" : "Z");
                    break;
            }

            c += PathData.CoordCount(command);
        }

        return sb.ToString();
    }

    private static void Append(StringBuilder sb, char command, double x, double y)
    {
        if (sb.Length > 0)
        {
            sb.Append(' ');
        }

        sb.Append(command).Append(N(x)).Append(' ').Append(N(y));
    }

    /// <summary>Center/angle arc to SVG endpoint parameters. Angles are clockwise from 12 o'clock.</summary>
    private static void AppendArc(StringBuilder sb, double cx, double cy, double r, double start, double end, bool hasCurrent)
    {
        var sweep = end - start;
        var x0 = cx + Math.Sin(start) * r;
        var y0 = cy - Math.Cos(start) * r;
        Append(sb, hasCurrent ? 'L' : 'M', x0, y0);
        if (r <= 0 || sweep == 0)
        {
            return;
        }

        // A single SVG arc cannot draw a full circle: split it in two.
        if (Math.Abs(sweep) >= Math.PI * 2 - 1e-6)
        {
            var mid = start + sweep / 2;
            AppendArcSegment(sb, cx, cy, r, mid, sweep / 2);
            AppendArcSegment(sb, cx, cy, r, start + sweep, sweep / 2);
            return;
        }

        AppendArcSegment(sb, cx, cy, r, end, sweep);
    }

    private static void AppendArcSegment(StringBuilder sb, double cx, double cy, double r, double end, double sweep)
    {
        var large = Math.Abs(sweep) > Math.PI ? 1 : 0;
        var flag = sweep > 0 ? 1 : 0;
        sb.Append(" A").Append(N(r)).Append(' ').Append(N(r)).Append(" 0 ").Append(large).Append(' ').Append(flag).Append(' ')
            .Append(N(cx + Math.Sin(end) * r)).Append(' ').Append(N(cy - Math.Cos(end) * r));
    }

    public static string? Transform(Matrix? m) => m is not { } t
        ? null
        : $"matrix({N(t.A)} {N(t.B)} {N(t.C)} {N(t.D)} {N(t.E)} {N(t.F)})";
}
