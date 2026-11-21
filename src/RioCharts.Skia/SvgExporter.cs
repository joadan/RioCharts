using System.Globalization;
using System.Security;
using System.Text;
using RioCharts.Core.Scene;

namespace RioCharts.Skia;

/// <summary>
/// Writes a scene as a standalone SVG document: concrete colors from an <see cref="ExportTheme"/>
/// (or CSS variables with those colors as fallback), invariant number formatting, no scripts.
/// </summary>
public static class SvgExporter
{
    /// <summary>Writes <paramref name="scene"/> as an SVG document.</summary>
    /// <param name="scene">The laid-out chart.</param>
    /// <param name="theme">Colors for the theme tokens.</param>
    /// <param name="fontFamily">Written as the SVG's font-family.</param>
    /// <param name="background">Background color or token; null for transparent.</param>
    /// <param name="cssVariables">Write theme colors as CSS variables with the theme's colors as fallback.</param>
    /// <returns>The SVG markup.</returns>
    public static string Write(Scene scene, ExportTheme theme, string fontFamily, string? background, bool cssVariables = false)
    {
        var writer = new Writer(scene.Styles, theme, cssVariables);
        var sb = writer.Output;
        var (w, h) = (N(scene.Width), N(scene.Height));
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(w).Append("\" height=\"").Append(h)
            .Append("\" viewBox=\"0 0 ").Append(w).Append(' ').Append(h).Append("\" font-family=\"").Append(Escape(fontFamily))
            .Append("\" style=\"font-variant-numeric: tabular-nums\" role=\"img\">");
        if (!string.IsNullOrEmpty(scene.Description))
        {
            sb.Append("<desc>").Append(Escape(scene.Description)).Append("</desc>");
        }

        if (background is not null)
        {
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"").Append(writer.Color(background)).Append("\"/>");
        }

        writer.Node(scene.Static);
        writer.Node(scene.Series);
        writer.Node(scene.Overlay);
        sb.Append("</svg>");
        return sb.ToString();
    }

    private sealed class Writer(StyleTable styles, ExportTheme theme, bool cssVariables)
    {
        private int _clips;

        public StringBuilder Output { get; } = new();

        public string Color(string? color)
        {
            if (string.IsNullOrEmpty(color) || color == "none")
            {
                return "none";
            }

            var resolved = theme.Resolve(color);
            return cssVariables && ThemeTokens.IsToken(color) ? $"var({color}, {resolved})" : Escape(resolved);
        }

        public void Node(Node node)
        {
            var sb = Output;
            switch (node)
            {
                case GroupNode g:
                    string? clip = null;
                    if (g.Clip is { } c)
                    {
                        clip = $"rio-clip-{_clips++}";
                        sb.Append("<clipPath id=\"").Append(clip).Append("\"><rect x=\"").Append(N(c.X)).Append("\" y=\"").Append(N(c.Y))
                            .Append("\" width=\"").Append(N(c.Width)).Append("\" height=\"").Append(N(c.Height)).Append("\"/></clipPath>");
                    }

                    sb.Append("<g");
                    Class(g.Class);
                    if (clip is not null)
                    {
                        sb.Append(" clip-path=\"url(#").Append(clip).Append(")\"");
                    }

                    if (g.Transform is { } tm)
                    {
                        sb.Append(" transform=\"matrix(").Append(N(tm.A)).Append(' ').Append(N(tm.B)).Append(' ').Append(N(tm.C)).Append(' ')
                            .Append(N(tm.D)).Append(' ').Append(N(tm.E)).Append(' ').Append(N(tm.F)).Append(")\"");
                    }

                    if (g.Class?.Contains("rio-legend-off", StringComparison.Ordinal) == true)
                    {
                        sb.Append(" opacity=\"0.4\""); // what the component's CSS does for hidden series
                    }

                    sb.Append('>');
                    if (g.Title is not null)
                    {
                        sb.Append("<title>").Append(Escape(g.Title)).Append("</title>");
                    }

                    foreach (var child in g.Children)
                    {
                        Node(child);
                    }

                    sb.Append("</g>");
                    break;
                case PathNode p:
                    if (p.Path.IsEmpty)
                    {
                        break;
                    }

                    sb.Append("<path");
                    Class(p.Class);
                    sb.Append(" d=\"").Append(PathData(p.Path)).Append('"');
                    Paint(styles.Get(p.Style));
                    sb.Append("/>");
                    break;
                case RectNode r:
                    sb.Append("<rect");
                    Class(r.Class);
                    sb.Append(" x=\"").Append(N(r.Bounds.X)).Append("\" y=\"").Append(N(r.Bounds.Y)).Append("\" width=\"").Append(N(r.Bounds.Width))
                        .Append("\" height=\"").Append(N(r.Bounds.Height)).Append('"');
                    if (r.CornerRadius > 0)
                    {
                        sb.Append(" rx=\"").Append(N(r.CornerRadius)).Append('"');
                    }

                    Paint(styles.Get(r.Style));
                    sb.Append("/>");
                    break;
                case TextNode t:
                    var style = styles.Get(t.Style);
                    sb.Append("<text");
                    Class(t.Class);
                    sb.Append(" x=\"").Append(N(t.At.X)).Append("\" y=\"").Append(N(t.At.Y)).Append("\" text-anchor=\"")
                        .Append(t.Anchor switch { Anchor.Start => "start", Anchor.End => "end", _ => "middle" })
                        .Append("\" dominant-baseline=\"").Append(t.Baseline switch { Baseline.Top => "hanging", Baseline.Bottom => "alphabetic", _ => "central" })
                        .Append("\" font-size=\"").Append(N(style.FontSize)).Append('"');
                    if (style.FontWeight != 400)
                    {
                        sb.Append(" font-weight=\"").Append(style.FontWeight).Append('"');
                    }

                    sb.Append(" fill=\"").Append(Color(style.Fill ?? ThemeTokens.Text)).Append('"');
                    if (t.Rotation != 0)
                    {
                        sb.Append(" transform=\"rotate(").Append(N(t.Rotation)).Append(' ').Append(N(t.At.X)).Append(' ').Append(N(t.At.Y)).Append(")\"");
                    }

                    sb.Append('>').Append(Escape(t.Text)).Append("</text>");
                    break;
            }
        }

        private void Class(string? cssClass)
        {
            if (cssClass is not null)
            {
                Output.Append(" class=\"").Append(Escape(cssClass)).Append('"');
            }
        }

        private void Paint(Style s)
        {
            var sb = Output;
            sb.Append(" fill=\"").Append(s.Fill is null ? "none" : Color(s.Fill)).Append('"');
            if (s.FillOpacity is { } fo)
            {
                sb.Append(" fill-opacity=\"").Append(N(fo)).Append('"');
            }

            if (s.Stroke is not null)
            {
                sb.Append(" stroke=\"").Append(Color(s.Stroke)).Append("\" stroke-width=\"").Append(N(s.StrokeWidth ?? 1))
                    .Append("\" stroke-linejoin=\"round\" stroke-linecap=\"round\"");
            }

            if (s.DashArray is not null)
            {
                sb.Append(" stroke-dasharray=\"").Append(Escape(s.DashArray)).Append('"');
            }

            if (s.Opacity is { } o)
            {
                sb.Append(" opacity=\"").Append(N(o)).Append('"');
            }
        }
    }

    /// <summary>SVG path data; arcs (center and angles clockwise from 12 o'clock) become endpoint arcs.</summary>
    internal static string PathData(PathData path)
    {
        var sb = new StringBuilder(path.Commands.Count * 14);
        var k = path.Coords;
        var c = 0;
        var hasCurrent = false;
        foreach (var command in path.Commands)
        {
            switch (command)
            {
                case PathCommand.MoveTo:
                    Point(sb, 'M', k[c], k[c + 1]);
                    hasCurrent = true;
                    break;
                case PathCommand.LineTo:
                    Point(sb, 'L', k[c], k[c + 1]);
                    hasCurrent = true;
                    break;
                case PathCommand.CubicTo:
                    sb.Append(sb.Length > 0 ? " C" : "C").Append(N(k[c])).Append(' ').Append(N(k[c + 1])).Append(' ').Append(N(k[c + 2])).Append(' ')
                        .Append(N(k[c + 3])).Append(' ').Append(N(k[c + 4])).Append(' ').Append(N(k[c + 5]));
                    hasCurrent = true;
                    break;
                case PathCommand.Arc:
                    Arc(sb, k[c], k[c + 1], k[c + 2], k[c + 3], k[c + 4], hasCurrent);
                    hasCurrent = true;
                    break;
                case PathCommand.Close:
                    sb.Append(sb.Length > 0 ? " Z" : "Z");
                    break;
            }

            c += Core.Scene.PathData.CoordCount(command);
        }

        return sb.ToString();
    }

    private static void Point(StringBuilder sb, char command, double x, double y)
    {
        if (sb.Length > 0)
        {
            sb.Append(' ');
        }

        sb.Append(command).Append(N(x)).Append(' ').Append(N(y));
    }

    private static void Arc(StringBuilder sb, double cx, double cy, double r, double start, double end, bool hasCurrent)
    {
        var sweep = end - start;
        Point(sb, hasCurrent ? 'L' : 'M', cx + Math.Sin(start) * r, cy - Math.Cos(start) * r);
        if (r <= 0 || sweep == 0)
        {
            return;
        }

        // A single SVG arc cannot draw a full circle: split it in two.
        if (Math.Abs(sweep) >= Math.PI * 2 - 1e-6)
        {
            ArcSegment(sb, cx, cy, r, start + sweep / 2, sweep / 2);
            ArcSegment(sb, cx, cy, r, start + sweep, sweep / 2);
            return;
        }

        ArcSegment(sb, cx, cy, r, end, sweep);
    }

    private static void ArcSegment(StringBuilder sb, double cx, double cy, double r, double end, double sweep) =>
        sb.Append(" A").Append(N(r)).Append(' ').Append(N(r)).Append(" 0 ").Append(Math.Abs(sweep) > Math.PI ? 1 : 0).Append(' ')
            .Append(sweep > 0 ? 1 : 0).Append(' ').Append(N(cx + Math.Sin(end) * r)).Append(' ').Append(N(cy - Math.Cos(end) * r));

    /// <summary>At most two decimals, invariant, no negative zero.</summary>
    internal static string N(double value)
    {
        if (!double.IsFinite(value))
        {
            return "0";
        }

        var rounded = Math.Round(value, 2);
        return (rounded == 0 ? 0 : rounded).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string Escape(string text) => SecurityElement.Escape(text) ?? "";
}
