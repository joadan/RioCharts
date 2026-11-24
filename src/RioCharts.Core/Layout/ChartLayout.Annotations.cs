using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;
using RioCharts.Core.Series;
using RioCharts.Core.Text;

namespace RioCharts.Core.Layout;

/// <summary>
/// A draggable reference line: where it is and the scale to turn a dragged pixel position back
/// into a value (data units; milliseconds on time axes).
/// </summary>
/// <param name="IsX">True for a vertical line (x axis).</param>
/// <param name="Log">
/// A line on a log axis: the domain is in log10 units, so the pixel maps linearly to log10(value); the value is 10 to
/// the power of the result.
/// </param>
internal sealed record DragHandle(string Id, bool IsX, float Pixel, double DomainMin, double DomainMax, double RangeStart, double RangeEnd, bool Log = false);

internal static partial class ChartLayout
{
    private sealed class ResolvedAnnotation
    {
        public required Annotation Spec { get; init; }
        public required string Id { get; init; }
        public required int Index { get; init; }

        /// <summary>A vertical line or band (on the x axis).</summary>
        public required bool IsX { get; init; }

        public required AnnotationType Type { get; init; }
        public bool IsBand => Type == AnnotationType.Band;
        public bool IsPoint => Type is AnnotationType.Point or AnnotationType.Callout;

        /// <summary>The y axis of horizontal lines, bands and points; null for x lines and bands.</summary>
        public YAxisLayout? Axis { get; init; }

        /// <summary>The line's value, the band's lower end, or a point's x (data units; category index on category axes).</summary>
        public required double A { get; init; }

        /// <summary>The band's upper end, or a point's y; NaN for lines.</summary>
        public required double B { get; init; }

        public required bool ExpandsDomain { get; init; }

        /// <summary>Values on the x axis that the domain should include.</summary>
        public double[] XValues => IsPoint ? [A] : IsX ? (IsBand ? [A, B] : [A]) : [];

        /// <summary>Values on the annotation's y axis that the domain should include.</summary>
        public double[] YValues => IsPoint ? [B] : IsX ? [] : (IsBand ? [A, B] : [A]);

        /// <summary>The same annotation without its text (compact charts).</summary>
        public ResolvedAnnotation WithoutLabel() => new()
        {
            Spec = Spec with { Label = null }, Id = Id, Index = Index, IsX = IsX, Type = Type, Axis = Axis, A = A, B = B, ExpandsDomain = ExpandsDomain,
        };
    }

    private static List<ResolvedAnnotation> ResolveAnnotations(ChartDefinition spec, List<YAxisLayout> axes, XKind xKind, IReadOnlyList<string> categories,
        List<string> warnings, TimeConversion? time = null)
    {
        var result = new List<ResolvedAnnotation>();

        // With a display zone, times are instants like the data.
        double ToValue(object? v, XKind k) => time is not null && k == XKind.Time ? time.ToInstant(v) : AxisValue.ToDouble(v, k, categories);
        for (var i = 0; i < spec.Annotations.Count; i++)
        {
            var a = spec.Annotations[i];
            var isPoint = a.Type is AnnotationType.Point or AnnotationType.Callout;
            var isX = !isPoint && a.Axis == AnnotationAxis.X;
            var kind = isX ? xKind : XKind.Numeric;
            var axis = isX ? null : FindAxis(axes, a.YAxisId, $"annotations[{i}].yAxisId", warnings) ?? axes[0];
            double first, second;
            if (isPoint)
            {
                (first, second) = (ToValue(a.X, xKind), AxisValue.ToDouble(a.Y, XKind.Numeric));
                if (!double.IsFinite(first) || !double.IsFinite(second))
                {
                    warnings.Add($"annotations[{i}]: x '{a.X}' must be a valid {Describe(xKind)} value and y '{a.Y}' a number; the {a.Type.ToString().ToLowerInvariant()} is skipped.");
                    continue;
                }
            }
            else if (a.Type == AnnotationType.Band)
            {
                (first, second) = (ToValue(a.From, kind), ToValue(a.To, kind));
                if (!double.IsFinite(first) || !double.IsFinite(second))
                {
                    warnings.Add($"annotations[{i}]: from '{a.From}' and to '{a.To}' must both be valid {Describe(kind)} values; the band is skipped.");
                    continue;
                }

                (first, second) = (Math.Min(first, second), Math.Max(first, second));
            }
            else
            {
                (first, second) = (ToValue(a.Value, kind), double.NaN);
                if (!double.IsFinite(first))
                {
                    warnings.Add($"annotations[{i}].value: '{a.Value}' is not a valid {Describe(kind)} value; the line is skipped.");
                    continue;
                }
            }

            // Lines and points stay in view by default; bands only mark what is already there.
            var expands = (a.ExpandsDomain ?? a.Type != AnnotationType.Band) && !(isX && xKind == XKind.Category);
            result.Add(new ResolvedAnnotation
            {
                Spec = a,
                Id = a.Id ?? $"annotation-{i + 1}",
                Index = i,
                IsX = isX,
                Type = a.Type,
                Axis = axis,
                A = first,
                B = second,
                ExpandsDomain = expands,
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
    /// Reference lines, bands, points and callouts with their labels. Shapes are clamped to the
    /// plot and labels stay inside it; label rectangles go into <paramref name="placed"/> so data
    /// labels avoid them. Draggable lines are reported in <paramref name="handles"/>.
    /// </summary>
    private static void BuildAnnotations(List<ResolvedAnnotation> annotations, RectF plot, XKind xKind, ContinuousScale? xScale, BandScale? xBand,
        CultureInfo culture, ITextMeasurer measure, double font, StyleTable styles, bool interactive,
        List<Node> below, List<Node> above, List<RectF> placed, List<DragHandle> handles, string? xFormat, TimeConversion? time)
    {
        var small = Math.Max(9, font - 1);
        var textHeight = small * 1.25;
        foreach (var a in annotations)
        {
            if (a.Axis is { Visible: false })
            {
                continue; // its axis has no visible series
            }

            if (!a.IsX && a.Axis is { Log: true } && ((a.IsPoint && !(a.B > 0)) || (!a.IsPoint && !a.IsBand && !(a.A > 0))))
            {
                continue; // zero or below is not on a log axis (warned in LeaveOutNonPositive)
            }

            if ((a.IsX || a.IsPoint) && xScale is { IsLog: true } && (!(a.A > 0) || (a.IsBand && !(a.B > 0))))
            {
                continue; // nor on a log x axis
            }

            var color = a.Spec.Color;
            var lineStyle = color is null ? new StyleRef("annotation-line")
                : styles.Add($"annotation-{a.Index}-line", new Style { Stroke = color, StrokeWidth = 1.25f, DashArray = "4 3" });
            var labelStyle = color is null ? new TextStyle("annotation-label")
                : styles.Add($"annotation-{a.Index}-label", new TextStyleDef { FontSize = (float)small, Fill = color });

            double MapX(double v) => xKind == XKind.Category ? xBand!.Center((int)v) : xScale!.Map(v);

            var nodes = new List<Node>();
            if (a.IsPoint)
            {
                if (!BuildPoint(a, MapX(a.A), a.Axis!.Scale.Map(a.B), plot, measure, small, textHeight, color, labelStyle, styles, nodes, placed, interactive))
                {
                    continue;
                }
            }
            else
            {
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
                    var bandStyle = color is null ? new StyleRef("annotation-band")
                        : styles.Add($"annotation-{a.Index}-band", new Style { Fill = color, FillOpacity = 0.12f });
                    nodes.Add(new RectNode(new RectF((float)x1, (float)y1, (float)(x2 - x1), (float)(y2 - y1)), bandStyle) { Class = "rio-annotation-band" });
                }
                else
                {
                    var line = a.IsX
                        ? new PathBuilder().MoveTo(x1, plot.Y).LineTo(x1, plot.Bottom).Build()
                        : new PathBuilder().MoveTo(plot.X, y1).LineTo(plot.Right, y1).Build();
                    nodes.Add(new PathNode(line, lineStyle) { Class = "rio-annotation-line" });
                    if (a.Spec.Draggable && !(a.IsX && xKind == XKind.Category))
                    {
                        // A wide transparent stroke makes a thin line easy to grab.
                        var hit = a.IsX
                            ? new PathBuilder().MoveTo(x1, plot.Y).LineTo(x1, plot.Bottom).Build()
                            : new PathBuilder().MoveTo(plot.X, y1).LineTo(plot.Right, y1).Build();
                        nodes.Add(new PathNode(hit, new StyleRef("annotation-grip")) { Class = "rio-annotation-grip" });
                        var s = a.IsX ? xScale! : a.Axis!.Scale;
                        handles.Add(s.IsLog
                            ? new DragHandle(a.Id, a.IsX, (float)(a.IsX ? x1 : y1), Math.Log10(s.DomainMin), Math.Log10(s.DomainMax), s.RangeStart, s.RangeEnd, Log: true)
                            : new DragHandle(a.Id, a.IsX, (float)(a.IsX ? x1 : y1), s.DomainMin, s.DomainMax, s.RangeStart, s.RangeEnd));
                    }
                }

                if (!string.IsNullOrWhiteSpace(a.Spec.Label))
                {
                    var text = a.Spec.Label!;
                    var w = measure.Measure(text, small);
                    var (lx, ly, anchor, baseline) = LineLabelPosition(a, plot, x1, x2, y1, w, textHeight);
                    placed.Add(TextRect(lx, ly, w, textHeight, anchor, baseline));
                    nodes.Add(new TextNode(text, new PointF((float)lx, (float)ly), labelStyle, anchor) { Baseline = baseline, Class = "rio-annotation-label" });
                }
            }

            var title = AnnotationTitle(a, xKind, xBand, culture, xFormat, time);
            var group = new GroupNode(nodes)
            {
                Key = "annotation:" + a.Id,
                Class = "rio-annotation rio-annotation-" + a.Type.ToString().ToLowerInvariant() + (a.Spec.Draggable && !a.IsBand && !a.IsPoint ? (a.IsX ? " rio-draggable rio-draggable-x" : " rio-draggable rio-draggable-y") : ""),
                Title = title,
                AriaLabel = title,
                Interaction = interactive ? new NodeInteraction("annotation", a.Id) : null,
            };
            ((a.Spec.Layer ?? (a.IsBand ? AnnotationLayer.Below : AnnotationLayer.Above)) == AnnotationLayer.Below ? below : above).Add(group);
        }
    }

    private static (double X, double Y, Anchor Anchor, Baseline Baseline) LineLabelPosition(ResolvedAnnotation a, RectF plot,
        double x1, double x2, double y1, double w, double textHeight)
    {
        var position = a.Spec.LabelPosition ?? (a.IsBand ? LabelPosition.Start : LabelPosition.End);
        if (!a.IsX)
        {
            var (lx, anchor) = position switch
            {
                LabelPosition.Start => (plot.X + 4.0, Anchor.Start),
                LabelPosition.Middle => (plot.CenterX, Anchor.Middle),
                _ => (plot.Right - 4.0, Anchor.End),
            };

            // Bands: inside at the top. Lines: above the line, or below it when there is no room.
            var (ly, baseline) = a.IsBand ? (y1 + 3, Baseline.Top)
                : y1 - 3 - textHeight >= plot.Y ? (y1 - 3, Baseline.Bottom) : (y1 + 3, Baseline.Top);
            return (lx, ly, anchor, baseline);
        }

        var (y, b) = a.IsBand || position == LabelPosition.End ? (plot.Y + 3.0, Baseline.Top)
            : position == LabelPosition.Start ? (plot.Bottom - 3.0, Baseline.Bottom)
            : (plot.CenterY, Baseline.Middle);
        if (a.IsBand)
        {
            return position switch
            {
                LabelPosition.Middle => ((x1 + x2) / 2, y, Anchor.Middle, b),
                LabelPosition.End => (x2 - 4, y, Anchor.End, b),
                _ => (x1 + 4, y, Anchor.Start, b),
            };
        }

        // Beside the line, on the side with room.
        return x1 + 4 + w <= plot.Right ? (x1 + 4, y, Anchor.Start, b) : (x1 - 4, y, Anchor.End, b);
    }

    /// <summary>
    /// A point annotation: a marker with its label above (or below near the top). A callout puts
    /// the label in a box at an offset, with a connector line to the point.
    /// </summary>
    private static bool BuildPoint(ResolvedAnnotation a, double px, double py, RectF plot, ITextMeasurer measure, double small, double textHeight,
        string? color, TextStyle labelStyle, StyleTable styles, List<Node> nodes, List<RectF> placed, bool interactive)
    {
        if (!plot.Contains((float)px, (float)py))
        {
            return false; // outside the visible range
        }

        var fill = color ?? ThemeTokens.Annotation;
        var markerStyle = styles.Add($"annotation-{a.Index}-marker", new Style { Fill = fill, Stroke = ThemeTokens.Surface, StrokeWidth = 2 });
        var label = a.Spec.Label;
        if (a.Type == AnnotationType.Callout && !string.IsNullOrWhiteSpace(label))
        {
            var w = measure.Measure(label, small);
            var (boxW, boxH) = (w + 14, textHeight + 8);
            var bx = Math.Clamp(px + (a.Spec.OffsetX ?? 24) - (a.Spec.OffsetX < 0 ? boxW : 0), plot.X, plot.Right - boxW);
            var by = Math.Clamp(py + (a.Spec.OffsetY ?? -32) - boxH / 2, plot.Y, plot.Bottom - boxH);
            var box = new RectF((float)bx, (float)by, (float)boxW, (float)boxH);

            // Connector from the point to the nearest point on the box edge.
            var cx = Math.Clamp(px, bx, bx + boxW);
            var cy = Math.Clamp(py, by, by + boxH);
            var connectorStyle = styles.Add($"annotation-{a.Index}-connector", new Style { Stroke = fill, StrokeWidth = 1 });
            nodes.Add(new PathNode(new PathBuilder().MoveTo(px, py).LineTo(cx, cy).Build(), connectorStyle) { Class = "rio-callout-connector" });
            var boxStyle = styles.Add($"annotation-{a.Index}-box", new Style { Fill = ThemeTokens.Surface, Stroke = fill, StrokeWidth = 1 });
            nodes.Add(new RectNode(box, boxStyle) { CornerRadius = 4, Class = "rio-callout-box" });
            nodes.Add(new TextNode(label, new PointF((float)(bx + 7), box.CenterY), new TextStyle("callout-label"), Anchor.Start) { Class = "rio-callout-label" });
            placed.Add(box);
        }
        else if (!string.IsNullOrWhiteSpace(label))
        {
            var w = measure.Measure(label, small);
            var (ly, baseline) = py - 8 - textHeight >= plot.Y ? (py - 8, Baseline.Bottom) : (py + 8, Baseline.Top);
            var (lx, anchor) = px - w / 2 < plot.X ? (px - 4, Anchor.Start) : px + w / 2 > plot.Right ? (px + 4, Anchor.End) : (px, Anchor.Middle);
            placed.Add(TextRect(lx, ly, w, textHeight, anchor, baseline));
            nodes.Add(new TextNode(label, new PointF((float)lx, (float)ly), labelStyle, anchor) { Baseline = baseline, Class = "rio-annotation-label" });
        }

        nodes.Add(new PathNode(new PathBuilder().MoveTo(px, py - 4.5).Arc(px, py, 4.5, 0, Math.PI * 2).Close().Build(), markerStyle) { Class = "rio-annotation-marker" });
        if (interactive)
        {
            // Clickable: a transparent ring around the small marker makes it easy to hit (12 px from its center).
            nodes.Add(new PathNode(new PathBuilder().MoveTo(px, py - 6).Arc(px, py, 6, 0, Math.PI * 2).Close().Build(), new StyleRef("annotation-grip"))
            {
                Class = "rio-annotation-grip",
            });
        }
        placed.Add(new RectF((float)(px - 5), (float)(py - 5), 10, 10));
        return true;
    }

    private static string AnnotationTitle(ResolvedAnnotation a, XKind xKind, BandScale? xBand, CultureInfo culture, string? xFormat, TimeConversion? time)
    {
        string FormatX(double v)
        {
            if (xKind == XKind.Category)
            {
                var i = (int)v;
                return xBand is not null && i >= 0 && i < xBand.Categories.Count ? xBand.Categories[i] : "";
            }

            return TimeFormat(xKind, [v], culture, xFormat, time, null)(v);
        }

        string FormatY(double v) => ValueFormatter.Format(v, culture, a.Axis!.Format);

        var value = a.IsPoint ? $"{FormatX(a.A)}, {FormatY(a.B)}"
            : a.IsX ? (a.IsBand ? $"{FormatX(a.A)} – {FormatX(a.B)}" : FormatX(a.A))
            : a.IsBand ? $"{FormatY(a.A)} – {FormatY(a.B)}" : FormatY(a.A);
        return string.IsNullOrWhiteSpace(a.Spec.Label) ? value : $"{a.Spec.Label}: {value}";
    }

    // ---------- Data labels ----------

    private const int MaxDataLabels = 1000;

    /// <summary>The height of a data label's box at the chart's font size.</summary>
    private static double DataLabelHeight(double font) => Math.Max(9, font - 1) * 1.25;

    /// <summary>Pixels between a bar's end and its label.</summary>
    private const double DataLabelGap = 3;

    /// <summary>Pixels a data label may reach past the plot's top or bottom.</summary>
    private const double DataLabelOverhang = 2;

    /// <summary>
    /// Value labels for series that ask for them. Bars get labels above (below for negative
    /// values), or inside stacked segments when they fit. Points get labels above, else below.
    /// A label that would overlap an earlier one is left out.
    /// </summary>
    private static List<Node> BuildDataLabels(List<(Resolved Series, List<LabelAnchor> Anchors)> series, RectF plot, CultureInfo culture,
        ITextMeasurer measure, double font, StyleTable styles, List<RectF> placed, List<string> warnings)
    {
        var small = Math.Max(9, font - 1);
        var h = DataLabelHeight(font);
        var groups = new List<Node>();
        foreach (var (r, anchors) in series)
        {
            if (anchors.Count > MaxDataLabels)
            {
                warnings.Add($"series '{r.Spec.Id}': too many points for data labels ({anchors.Count}); labels are not shown.");
                continue;
            }

            var format = ValueFormat.Of(r.Spec.Format, r.Axis.Spec);
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
                        candidates.Add((a.Y - DataLabelGap, Baseline.Bottom, false));
                        if (fitsInside)
                        {
                            candidates.Add((a.Y + DataLabelGap, Baseline.Top, true));
                        }
                    }
                    else
                    {
                        candidates.Add((a.Y + DataLabelGap, Baseline.Top, false));
                        if (fitsInside)
                        {
                            candidates.Add((a.Y - DataLabelGap, Baseline.Bottom, true));
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
                    if (rect.Y < plot.Y - DataLabelOverhang || rect.Bottom > plot.Bottom + DataLabelOverhang || rect.X < plot.X - 4 || rect.Right > plot.Right + 4
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
