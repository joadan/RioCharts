using System.Globalization;
using System.Text.RegularExpressions;
using RioCharts.Core.Scene;
using SkiaSharp;

namespace RioCharts.Skia;

/// <summary>Draws a scene on a Skia canvas: the same nodes the SVG renderer writes.</summary>
public sealed partial class SkiaRenderer(ExportTheme theme, SkiaFonts fonts)
{
    /// <summary>Draws the static, series and overlay layers in chart units (scale the canvas first for higher density).</summary>
    public void Draw(SKCanvas canvas, Scene scene)
    {
        Node(canvas, scene.Static, scene.Styles);
        Node(canvas, scene.Series, scene.Styles);
        Node(canvas, scene.Overlay, scene.Styles);
    }

    /// <summary>Renders the scene to an encoded image (PNG by default).</summary>
    public byte[] Encode(Scene scene, float scale, string? background, SKEncodedImageFormat format = SKEncodedImageFormat.Png, int quality = 100)
    {
        var width = Math.Max(1, (int)Math.Ceiling(scene.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(scene.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Could not create a Skia surface.");
        var canvas = surface.Canvas;
        canvas.Clear(background is null ? SKColors.Transparent : ParseColor(theme.Resolve(background)));
        canvas.Scale(scale);
        Draw(canvas, scene);
        canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    private void Node(SKCanvas canvas, Node node, StyleTable styles)
    {
        switch (node)
        {
            case GroupNode g:
                var faded = g.Class?.Contains("rio-legend-off", StringComparison.Ordinal) == true; // the component's CSS fades hidden series
                if (faded)
                {
                    canvas.SaveLayer(new SKPaint { Color = SKColors.Black.WithAlpha(102) });
                }
                else
                {
                    canvas.Save();
                }

                if (g.Clip is { } clip)
                {
                    canvas.ClipRect(new SKRect(clip.X, clip.Y, clip.Right, clip.Bottom), antialias: true);
                }

                if (g.Transform is { } m)
                {
                    var matrix = new SKMatrix(m.A, m.C, m.E, m.B, m.D, m.F, 0, 0, 1);
                    canvas.Concat(in matrix);
                }

                foreach (var child in g.Children)
                {
                    Node(canvas, child, styles);
                }

                canvas.Restore();
                break;
            case PathNode p when !p.Path.IsEmpty:
                using (var path = ToSkPath(p.Path))
                {
                    Paint(canvas, styles.Get(p.Style), paint => canvas.DrawPath(path, paint));
                }

                break;
            case RectNode r:
                var rect = new SKRect(r.Bounds.X, r.Bounds.Y, r.Bounds.Right, r.Bounds.Bottom);
                Paint(canvas, styles.Get(r.Style), paint =>
                {
                    if (r.CornerRadius > 0)
                    {
                        canvas.DrawRoundRect(rect, r.CornerRadius, r.CornerRadius, paint);
                    }
                    else
                    {
                        canvas.DrawRect(rect, paint);
                    }
                });
                break;
            case TextNode t:
                Text(canvas, t, styles.Get(t.Style));
                break;
        }
    }

    private void Paint(SKCanvas canvas, Style style, Action<SKPaint> draw)
    {
        var opacity = style.Opacity ?? 1;
        if (style.Fill is { } fill && Visible(fill))
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = WithAlpha(ParseColor(theme.Resolve(fill)), opacity * (style.FillOpacity ?? 1)),
            };
            draw(paint);
        }

        if (style.Stroke is { } stroke && Visible(stroke))
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                Color = WithAlpha(ParseColor(theme.Resolve(stroke)), opacity),
                StrokeWidth = style.StrokeWidth ?? 1,
                StrokeJoin = SKStrokeJoin.Round,
                StrokeCap = SKStrokeCap.Round,
            };
            if (Dashes(style.DashArray) is { } dashes)
            {
                paint.PathEffect = SKPathEffect.CreateDash(dashes, 0);
            }

            draw(paint);
        }
    }

    private void Text(SKCanvas canvas, TextNode t, TextStyleDef style)
    {
        if (string.IsNullOrEmpty(t.Text))
        {
            return;
        }

        using var font = new SKFont(fonts.Typeface(style.FontWeight), style.FontSize) { Subpixel = true, Edging = SKFontEdging.Antialias };
        using var paint = new SKPaint { IsAntialias = true, Color = ParseColor(theme.Resolve(style.Fill ?? ThemeTokens.Text)) };
        var width = font.MeasureText(t.Text);
        var metrics = font.Metrics;
        var x = t.Anchor switch { Anchor.Start => t.At.X, Anchor.End => t.At.X - width, _ => t.At.X - width / 2 };
        var y = t.Baseline switch
        {
            Baseline.Top => t.At.Y - metrics.Ascent * 0.92f, // like SVG "hanging"
            Baseline.Bottom => t.At.Y,
            _ => t.At.Y - (metrics.Ascent + metrics.Descent) / 2, // like SVG "central"
        };

        canvas.Save();
        if (t.Rotation != 0)
        {
            canvas.RotateDegrees(t.Rotation, t.At.X, t.At.Y);
        }

        canvas.DrawText(t.Text, x, y, SKTextAlign.Left, font, paint);
        canvas.Restore();
    }

    /// <summary>Path commands to a Skia path. Arcs use center and angles clockwise from 12 o'clock.</summary>
    public static SKPath ToSkPath(PathData data)
    {
        using var path = new SKPathBuilder();
        var k = data.Coords;
        var c = 0;
        var hasCurrent = false;
        foreach (var command in data.Commands)
        {
            switch (command)
            {
                case PathCommand.MoveTo:
                    path.MoveTo(k[c], k[c + 1]);
                    hasCurrent = true;
                    break;
                case PathCommand.LineTo:
                    path.LineTo(k[c], k[c + 1]);
                    hasCurrent = true;
                    break;
                case PathCommand.CubicTo:
                    path.CubicTo(k[c], k[c + 1], k[c + 2], k[c + 3], k[c + 4], k[c + 5]);
                    hasCurrent = true;
                    break;
                case PathCommand.Arc:
                    var (cx, cy, r) = (k[c], k[c + 1], k[c + 2]);
                    var start = k[c + 3] * 180 / MathF.PI - 90; // Skia: degrees clockwise from 3 o'clock
                    var sweep = (k[c + 4] - k[c + 3]) * 180 / MathF.PI;
                    var oval = new SKRect(cx - r, cy - r, cx + r, cy + r);
                    if (Math.Abs(sweep) >= 359.99f)
                    {
                        // A full circle in two halves (one 360° arc can collapse).
                        path.ArcTo(oval, start, sweep / 2, !hasCurrent);
                        path.ArcTo(oval, start + sweep / 2, sweep / 2, false);
                    }
                    else
                    {
                        path.ArcTo(oval, start, sweep, !hasCurrent);
                    }

                    hasCurrent = true;
                    break;
                case PathCommand.Close:
                    path.Close();
                    break;
            }

            c += PathData.CoordCount(command);
        }

        return path.Detach();
    }

    private static bool Visible(string color) => color is not ("none" or "transparent" or "");

    private static SKColor WithAlpha(SKColor color, float opacity) =>
        opacity >= 1 ? color : color.WithAlpha((byte)Math.Round(color.Alpha * Math.Clamp(opacity, 0, 1)));

    private static float[]? Dashes(string? dashArray)
    {
        if (string.IsNullOrWhiteSpace(dashArray))
        {
            return null;
        }

        var values = dashArray.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(v => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0)
            .ToArray();
        return values.Length == 0 || values.All(v => v <= 0) ? null : values.Length % 2 == 1 ? [.. values, .. values] : values;
    }

    /// <summary>CSS colors: #rgb, #rrggbb, #rrggbbaa, rgb()/rgba() and a few names.</summary>
    public static SKColor ParseColor(string color)
    {
        color = color.Trim();
        if (SKColor.TryParse(color, out var parsed))
        {
            return parsed;
        }

        var match = RgbPattern().Match(color);
        if (match.Success)
        {
            static byte Channel(string v) => (byte)Math.Clamp(double.Parse(v, CultureInfo.InvariantCulture), 0, 255);
            var alpha = match.Groups[4].Success ? Math.Clamp(double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture), 0, 1) : 1;
            return new SKColor(Channel(match.Groups[1].Value), Channel(match.Groups[2].Value), Channel(match.Groups[3].Value), (byte)Math.Round(alpha * 255));
        }

        return color.ToLowerInvariant() switch
        {
            "white" => SKColors.White,
            "black" => SKColors.Black,
            "red" => SKColors.Red,
            "green" => SKColors.Green,
            "blue" => SKColors.Blue,
            "orange" => SKColors.Orange,
            "gray" or "grey" => SKColors.Gray,
            "transparent" or "none" => SKColors.Transparent,
            _ => SKColors.Gray,
        };
    }

    [GeneratedRegex(@"^rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)(?:[\s,/]+([\d.]+))?\s*\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RgbPattern();
}
