using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;
using RioCharts.Core.Series;
using RioCharts.Core.Text;

namespace RioCharts.Core.Layout;

public static partial class ChartLayout
{
    private sealed class ResolvedAnnotation
    {
        public required AnnotationSpec Spec { get; init; }
        public required string Id { get; init; }
        public required int Index { get; init; }
        public required bool IsX { get; init; }
        public required bool IsBand { get; init; }
        public YAxisLayout? Axis { get; init; }

        /// <summary>The line's value, or the band's lower end (data units; category index on category axes).</summary>
        public required double A { get; init; }

        /// <summary>The band's upper end; NaN for lines.</summary>
        public required double B { get; init; }

        public required bool ExpandsDomain { get; init; }

        public double[] Values => IsBand ? [A, B] : [A];
    }

    private static List<ResolvedAnnotation> ResolveAnnotations(ChartSpec spec, List<YAxisLayout> axes, XKind xKind, IReadOnlyList<string> categories,
        List<string> warnings)
    {
        var result = new List<ResolvedAnnotation>();
        for (var i = 0; i < spec.Annotations.Count; i++)
        {
            var a = spec.Annotations[i];
            var isX = a.Axis == AnnotationAxis.X;
            var kind = isX ? xKind : XKind.Numeric;
            var axis = isX ? null : FindAxis(axes, a.YAxisId, $"annotations[{i}].yAxisId", warnings) ?? axes[0];
            var isBand = a.Type == AnnotationType.Band;
            double first, second;
            if (isBand)
            {
                (first, second) = (AxisValue.ToDouble(a.From, kind, categories), AxisValue.ToDouble(a.To, kind, categories));
                if (!double.IsFinite(first) || !double.IsFinite(second))
                {
                    warnings.Add($"annotations[{i}]: from '{a.From}' and to '{a.To}' must both be valid {Describe(kind)} values; the band is skipped.");
                    continue;
                }

                (first, second) = (Math.Min(first, second), Math.Max(first, second));
            }
            else
            {
                (first, second) = (AxisValue.ToDouble(a.Value, kind, categories), double.NaN);
                if (!double.IsFinite(first))
                {
                    warnings.Add($"annotations[{i}].value: '{a.Value}' is not a valid {Describe(kind)} value; the line is skipped.");
                    continue;
                }
            }

            result.Add(new ResolvedAnnotation
            {
                Spec = a,
                Id = a.Id ?? $"annotation-{i + 1}",
                Index = i,
                IsX = isX,
                IsBand = isBand,
                Axis = axis,
                A = first,
                B = second,
                ExpandsDomain = (a.ExpandsDomain ?? !isBand) && !(isX && xKind == XKind.Category),
            });
        }

        return result;

        static string Describe(XKind kind) => kind switch
        {
            XKind.Time => "time (e.g. 2026-03-01T12:00)",
            XKind.Category => "category",
            _ => "number",
        };
    }

    /// <summary>
    /// Reference lines and bands with their labels. Shapes are clamped to the plot and labels
    /// stay inside it; label rectangles go into <paramref name="placed"/> so data labels avoid them.
    /// </summary>
    private static void BuildAnnotations(List<ResolvedAnnotation> annotations, RectF plot, XKind xKind, LinearScale? xScale, BandScale? xBand,
        CultureInfo culture, ITextMeasurer measure, double font, StyleTable styles, bool interactive,
        List<Node> below, List<Node> above, List<Node> labels, List<RectF> placed, string? xFormat)
    {
        var small = Math.Max(9, font - 1);
        var textHeight = small * 1.25;
        foreach (var a in annotations)
        {
            if (a.Axis is { Visible: false })
            {
                continue; // its axis has no visible series
            }

            var color = a.Spec.Color;
            var lineStyle = color is null ? new StyleRef("annotation-line")
                : styles.Add($"annotation-{a.Index}-line", new Style { Stroke = color, StrokeWidth = 1.25f, DashArray = "4 3" });
            var bandStyle = color is null ? new StyleRef("annotation-band")
                : styles.Add($"annotation-{a.Index}-band", new Style { Fill = color, FillOpacity = 0.12f });
            var labelStyle = color is null ? new TextStyle("annotation-label")
                : styles.Add($"annotation-{a.Index}-label", new TextStyleDef { FontSize = (float)small, Fill = color });

            var nodes = new List<Node>();
            double x1, x2, y1, y2; // the shape's extent, clamped to the plot
            if (a.IsX)
            {
                var (p1, p2) = xKind == XKind.Category
                    ? (xBand!.Center((int)a.A) - (a.IsBand ? xBand.Step / 2 : 0), xBand.Center((int)(a.IsBand ? a.B : a.A)) + (a.IsBand ? xBand.Step / 2 : 0))
                    : (xScale!.Map(a.A), a.IsBand ? xScale.Map(a.B) : xScale.Map(a.A));
                (x1, x2, y1, y2) = (Math.Max(plot.X, Math.Min(p1, p2)), Math.Min(plot.Right, Math.Max(p1, p2)), plot.Y, plot.Bottom);
                if (x2 < x1 - 0.5 || (a.IsBand && x2 - x1 < 0.5))
                {
                    continue; // outside the visible range
                }
            }
            else
            {
                var scale = a.Axis!.Scale;
                var (p1, p2) = (scale.Map(a.A), a.IsBand ? scale.Map(a.B) : scale.Map(a.A));
                (x1, x2, y1, y2) = (plot.X, plot.Right, Math.Max(plot.Y, Math.Min(p1, p2)), Math.Min(plot.Bottom, Math.Max(p1, p2)));
                if (y2 < y1 - 0.5 || (a.IsBand && y2 - y1 < 0.5))
                {
                    continue;
                }
            }

            if (a.IsBand)
            {
                nodes.Add(new RectNode(new RectF((float)x1, (float)y1, (float)(x2 - x1), (float)(y2 - y1)), bandStyle) { Class = "rio-annotation-band" });
            }
            else if (a.IsX)
            {
                nodes.Add(new PathNode(new PathBuilder().MoveTo(x1, plot.Y).LineTo(x1, plot.Bottom).Build(), lineStyle) { Class = "rio-annotation-line" });
            }
            else
            {
                nodes.Add(new PathNode(new PathBuilder().MoveTo(plot.X, y1).LineTo(plot.Right, y1).Build(), lineStyle) { Class = "rio-annotation-line" });
            }

            if (!string.IsNullOrWhiteSpace(a.Spec.Label))
            {
                var text = a.Spec.Label!;
                var w = measure.Measure(text, small);
                var position = a.Spec.LabelPosition ?? (a.IsBand ? LabelPosition.Start : LabelPosition.End);
                double lx, ly;
                Anchor anchor;
                Baseline baseline;
                if (!a.IsX)
                {
                    (lx, anchor) = position switch
                    {
                        LabelPosition.Start => (plot.X + 4, Anchor.Start),
                        LabelPosition.Middle => (plot.CenterX, Anchor.Middle),
                        _ => (plot.Right - 4, Anchor.End),
                    };
                    if (a.IsBand)
                    {
                        (ly, baseline) = (y1 + 3, Baseline.Top);
                    }
                    else
                    {
                        // Above the line, or below it when there is no room at the top.
                        (ly, baseline) = y1 - 3 - textHeight >= plot.Y ? (y1 - 3, Baseline.Bottom) : (y1 + 3, Baseline.Top);
                    }
                }
                else
                {
                    (ly, baseline) = a.IsBand || position == LabelPosition.End ? (plot.Y + 3, Baseline.Top)
                        : position == LabelPosition.Start ? (plot.Bottom - 3, Baseline.Bottom)
                        : (plot.CenterY, Baseline.Middle);
                    if (a.IsBand)
                    {
                        (lx, anchor) = position switch
                        {
                            LabelPosition.Middle => ((x1 + x2) / 2, Anchor.Middle),
                            LabelPosition.End => (x2 - 4, Anchor.End),
                            _ => (x1 + 4, Anchor.Start),
                        };
                    }
                    else
                    {
                        // Beside the line, on the side with room.
                        (lx, anchor) = x1 + 4 + w <= plot.Right ? (x1 + 4, Anchor.Start) : (x1 - 4, Anchor.End);
                    }
                }

                var rect = TextRect(lx, ly, w, textHeight, anchor, baseline);
                placed.Add(rect);
                nodes.Add(new TextNode(text, new PointF((float)lx, (float)ly), labelStyle, anchor) { Baseline = baseline, Class = "rio-annotation-label" });
            }

            var title = AnnotationTitle(a, xKind, xBand, culture, xFormat);
            var group = new GroupNode(nodes)
            {
                Key = "annotation:" + a.Id,
                Class = "rio-annotation rio-annotation-" + (a.IsBand ? "band" : "line"),
                Title = title,
                AriaLabel = title,
                Interaction = interactive ? new NodeInteraction("annotation", a.Id) : null,
            };
            ((a.Spec.Layer ?? (a.IsBand ? AnnotationLayer.Below : AnnotationLayer.Above)) == AnnotationLayer.Below ? below : above).Add(group);
        }
    }

    private static string AnnotationTitle(ResolvedAnnotation a, XKind xKind, BandScale? xBand, CultureInfo culture, string? xFormat)
    {
        string Format(double v)
        {
            if (!a.IsX)
            {
                return ValueFormatter.Format(v, culture, a.Axis!.Spec.Format);
            }

            if (xKind == XKind.Category)
            {
                var i = (int)v;
                return xBand is not null && i >= 0 && i < xBand.Categories.Count ? xBand.Categories[i] : "";
            }

            return ValueFormatter.ForX(xKind, a.Values, culture, xFormat)(v);
        }

        var value = a.IsBand ? $"{Format(a.A)} – {Format(a.B)}" : Format(a.A);
        return string.IsNullOrWhiteSpace(a.Spec.Label) ? value : $"{a.Spec.Label}: {value}";
    }

    // ---------- Data labels ----------

    private const int MaxDataLabels = 1000;

    /// <summary>
    /// Value labels for series that ask for them. Bars get labels above (below for negative
    /// values), or inside stacked segments when they fit. Points get labels above, else below.
    /// A label that would overlap an earlier one is left out.
    /// </summary>
    private static List<Node> BuildDataLabels(List<(Resolved Series, List<LabelAnchor> Anchors)> series, RectF plot, CultureInfo culture,
        ITextMeasurer measure, double font, StyleTable styles, List<RectF> placed, List<string> warnings)
    {
        var small = Math.Max(9, font - 1);
        var h = small * 1.25;
        var groups = new List<Node>();
        foreach (var (r, anchors) in series)
        {
            if (anchors.Count > MaxDataLabels)
            {
                warnings.Add($"series '{r.Spec.Id}': too many points for data labels ({anchors.Count}); labels are not shown.");
                continue;
            }

            var format = r.Spec.Format ?? r.Axis.Spec.Format;
            var nodes = new List<Node>();
            foreach (var a in anchors)
            {
                var text = ValueFormatter.Format(a.Value, culture, format, compactFrom: 1e4);
                var w = measure.Measure(text, small);
                var candidates = new List<(double Y, Baseline Baseline, bool Inverse)>();
                if (a.Width > 0)
                {
                    var barHeight = Math.Abs(a.Y - a.Y0);
                    var fitsInside = barHeight >= h + 4 && a.Width >= w + 4;
                    if (a.Stacked)
                    {
                        if (fitsInside)
                        {
                            candidates.Add(((a.Y + a.Y0) / 2, Baseline.Middle, true));
                        }
                    }
                    else if (a.Value >= 0)
                    {
                        candidates.Add((a.Y - 3, Baseline.Bottom, false));
                        if (fitsInside)
                        {
                            candidates.Add((a.Y + 3, Baseline.Top, true));
                        }
                    }
                    else
                    {
                        candidates.Add((a.Y + 3, Baseline.Top, false));
                        if (fitsInside)
                        {
                            candidates.Add((a.Y - 3, Baseline.Bottom, true));
                        }
                    }
                }
                else
                {
                    candidates.Add((a.Y - 6, Baseline.Bottom, false));
                    candidates.Add((a.Y + 6, Baseline.Top, false));
                }

                foreach (var (y, baseline, inverse) in candidates)
                {
                    var rect = TextRect(a.X, y, w, h, Anchor.Middle, baseline);
                    if (rect.Y < plot.Y - 2 || rect.Bottom > plot.Bottom + 2 || rect.X < plot.X - 4 || rect.Right > plot.Right + 4
                        || placed.Any(p => Overlaps(p, rect)))
                    {
                        continue;
                    }

                    placed.Add(rect);
                    nodes.Add(new TextNode(text, new PointF((float)a.X, (float)y), new TextStyle(inverse ? "data-label-inverse" : "data-label"), Anchor.Middle)
                    {
                        Baseline = baseline,
                    });
                    break;
                }
            }

            groups.Add(new GroupNode(nodes) { Key = "labels:" + r.Spec.Id, Class = "rio-data-labels" });
        }

        return groups;
    }

    private static RectF TextRect(double x, double y, double w, double h, Anchor anchor, Baseline baseline)
    {
        var left = anchor switch { Anchor.Start => x, Anchor.End => x - w, _ => x - w / 2 };
        var top = baseline switch { Baseline.Top => y, Baseline.Bottom => y - h, _ => y - h / 2 };
        return new RectF((float)left, (float)top, (float)w, (float)h);
    }

    private static bool Overlaps(RectF a, RectF b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
}
