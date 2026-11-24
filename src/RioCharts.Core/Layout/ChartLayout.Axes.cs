using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;
using RioCharts.Core.Text;

namespace RioCharts.Core.Layout;

public static partial class ChartLayout
{
    /// <summary>A vertical axis during layout: its series, extent, ticks and label column.</summary>
    private sealed class YAxisLayout
    {
        public required AxisOptions Spec { get; init; }
        public required string Id { get; init; }
        public required bool Right { get; init; }
        public List<Resolved> Series { get; } = [];

        /// <summary>A logarithmic axis (<see cref="ScaleType.Log"/>).</summary>
        public bool Log => Spec.Scale == ScaleType.Log;

        /// <summary>Series stacked to 100 % use the axis (<see cref="StackMode.Percent"/>): shares from -1 to 1, labeled as percentages.</summary>
        public bool Percent { get; set; }

        /// <summary>The label format: the axis' own function or format string, or percentages on a percent axis.</summary>
        public ValueFormat Format => ValueFormat.Of(Spec, Percent ? "P0" : null);

        /// <summary>True when at least one drawable series uses the axis.</summary>
        public bool Visible { get; set; }

        public double ExtentMin { get; set; }
        public double ExtentMax { get; set; }

        /// <summary>
        /// The highest positive and lowest negative value of bars with data labels (NaN when none): their labels go beyond
        /// the bar's end, so the domain leaves room for them.
        /// </summary>
        public double LabelMax { get; set; } = double.NaN;
        public double LabelMin { get; set; } = double.NaN;
        public double DomainMin { get; set; }
        public double DomainMax { get; set; } = 1;
        public List<double> Ticks { get; set; } = [];
        public List<string> Labels { get; set; } = [];
        public double LabelWidth { get; set; }

        /// <summary>
        /// Width of the axis' column: its widest tick label, or its title when that is wider and the axis is not the one
        /// next to the plot (titles sit in one row above the labels, so an outer axis' title must not run into the next one).
        /// </summary>
        public double ColumnWidth { get; set; }

        /// <summary>The title as drawn (shortened when it is capped); null when the axis shows none.</summary>
        public string? ShownTitle { get; set; }

        /// <summary>X of the label column's edge next to the plot.</summary>
        public double LabelX { get; set; }

        public ContinuousScale Scale { get; set; } = new(0, 1, 1, 0);
    }

    private static List<YAxisLayout> ResolveAxes(ChartDefinition spec, List<string> warnings)
    {
        var axes = new List<YAxisLayout>
        {
            new() { Spec = spec.Y, Id = spec.Y.Id ?? "y", Right = spec.Y.Position == AxisPosition.Right },
        };
        for (var i = 0; i < spec.YAxes.Count; i++)
        {
            var a = spec.YAxes[i];
            var id = a.Id ?? $"y{i + 2}";
            if (axes.Any(x => x.Id == id))
            {
                warnings.Add($"yAxes[{i}].id: duplicate axis id '{id}'; the axis is skipped.");
                continue;
            }

            axes.Add(new YAxisLayout { Spec = a, Id = id, Right = a.Position != AxisPosition.Left });
        }

        return axes;
    }

    private static YAxisLayout? FindAxis(List<YAxisLayout> axes, string? id, string path, List<string> warnings)
    {
        if (id is null)
        {
            return null;
        }

        var axis = axes.FirstOrDefault(a => a.Id == id);
        if (axis is null)
        {
            warnings.Add($"{path}: axis '{id}' not found; available: {string.Join(", ", axes.Select(a => a.Id))}. The primary axis is used.");
        }

        return axis;
    }

    private static void YExtent(YAxisLayout axis, IEnumerable<ResolvedAnnotation> annotations, (double Min, double Max)? window, bool compact = false)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        double labelMin = double.PositiveInfinity, labelMax = double.NegativeInfinity;
        var includeZero = false;
        var log = axis.Log; // only positive values count; zero is not on the axis
        foreach (var r in axis.Series)
        {
            // Compact charts fit the data; only bars keep zero (a bar's length is its value).
            includeZero |= r.Type.IncludesZero && (!compact || r.Type.UsesSlots) && !log;
            Include(r.Upper, r.Data.X);
            if (HasErrors(r))
            {
                // The error bars fit on the axis too.
                var lows = new double[r.Data.Count];
                var highs = new double[r.Data.Count];
                for (var i = 0; i < lows.Length; i++)
                {
                    (lows[i], highs[i]) = ErrorAt(r, i);
                }

                Include(lows, r.Data.X);
                Include(highs, r.Data.X);
            }

            if (!compact && r.Spec.DataLabels == true && r.Spec.Type == SeriesType.Bar && r.Lower is null)
            {
                // Unstacked bars: the label sits above a positive bar and below a negative one.
                var (start, end) = window is { } w ? VisibleIndexes(r.Data.X, w.Min, w.Max, 0) : (0, r.Upper.Length);
                for (var i = start; i < end; i++)
                {
                    var y = r.Upper[i];
                    if (y > 0 && y > labelMax)
                    {
                        labelMax = y;
                    }
                    else if (y < 0 && y < labelMin)
                    {
                        labelMin = y;
                    }
                }
            }
            if (r.Lower is not null)
            {
                Include(r.Lower, r.Data.X);
            }

            if (r.Data.Candles is { } candles)
            {
                Include(candles.High, r.Data.X);
                Include(candles.Low, r.Data.X);
            }

            if (r.Data.Box is { } box)
            {
                // The whole box plot, outliers included.
                Include(box.Min, r.Data.X);
                Include(box.Max, r.Data.X);
            }
        }

        if (double.IsFinite(min))
        {
            foreach (var a in annotations)
            {
                foreach (var v in a.YValues)
                {
                    if (log && !(v > 0))
                    {
                        continue;
                    }

                    min = Math.Min(min, v);
                    max = Math.Max(max, v);
                }
            }
        }

        if (!double.IsFinite(min))
        {
            (min, max) = log ? (1, 10) : (0, 1);
        }

        if (includeZero)
        {
            min = Math.Min(min, 0);
            max = Math.Max(max, 0);
        }

        // Shares stay within -1 and 1, whatever an annotation or rounding says.
        axis.Percent = !log && axis.Series.Any(r => r.Percent);
        if (axis.Percent)
        {
            (min, max) = (Math.Max(min, -1), Math.Min(max, 1));
        }

        // A log axis ignores a Min or Max of zero or below (they are not on it).
        axis.ExtentMin = log && !(axis.Spec.Min > 0) ? min : axis.Spec.Min ?? min;
        axis.ExtentMax = log && !(axis.Spec.Max > 0) ? max : axis.Spec.Max ?? max;
        axis.LabelMin = double.IsFinite(labelMin) ? labelMin : double.NaN;
        axis.LabelMax = double.IsFinite(labelMax) ? labelMax : double.NaN;

        // Zoomed: only the points in the visible x range count, so the y axis fits what is shown.
        void Include(double[] values, double[] xs)
        {
            var (start, end) = window is { } w ? VisibleIndexes(xs, w.Min, w.Max, 0) : (0, values.Length);
            for (var i = start; i < end; i++)
            {
                // Plain comparisons on finite values: faster than Math.Min/Max, which handle NaN and -0.
                var y = values[i];
                if (!double.IsFinite(y) || (log && y <= 0))
                {
                    continue;
                }

                if (y < min)
                {
                    min = y;
                }

                if (y > max)
                {
                    max = y;
                }
            }
        }
    }

    /// <summary>
    /// Index range [start, end) of the sorted x values within [min, max], widened by
    /// <paramref name="margin"/> points on each side (so lines run on to the plot edge).
    /// </summary>
    internal static (int Start, int End) VisibleIndexes(double[] xs, double min, double max, int margin)
    {
        var start = LowerBound(xs, min);
        var end = LowerBound(xs, Math.BitIncrement(max));
        return (Math.Max(0, start - margin), Math.Min(xs.Length, end + margin));

        static int LowerBound(double[] xs, double value)
        {
            int lo = 0, hi = xs.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) >>> 1;
                if (xs[mid] < value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }
    }

    private static void ComputeYTicks(YAxisLayout axis, int target, CultureInfo culture, ITextMeasurer measure, double font, double plotHeight,
        YAxisLayout? align = null)
    {
        if (axis.Log)
        {
            ComputeLogTicks(axis, culture, measure, font, plotHeight);
            return;
        }

        var (min, max) = (axis.ExtentMin, axis.ExtentMax);
        if (max < min)
        {
            (min, max) = (max, min);
        }

        if (max == min)
        {
            // All values equal: pad so the axis does not collapse. All zero: 0 to 1, not -1 to 1.
            var pad = min == 0 ? 1 : Math.Abs(min) * 0.1;
            if (axis.Spec.Min is null && min != 0)
            {
                min -= pad;
            }

            if (axis.Spec.Max is null)
            {
                max += pad;
            }

            if (max <= min)
            {
                max = min + 1;
            }
        }

        // Ticks no closer than the format tells apart (with "#,0": 0, 1, 2, not 0, 0.5, 1, 1.5, 2 shown as 0 1 1 2 2).
        var minStep = NumberFormatter.MinStep(axis.Format, culture) ?? 0;
        var (niceMin, niceMax, step) = NiceTicks.NiceDomain(min, max, target, minStep, axis.Spec.TickBase ?? 0);

        // Bar data labels go beyond the bar's end and are left out when they would leave the plot: when the highest or
        // lowest bar has no room for its label, the domain gets one more step on that side (the step stays, so the ticks
        // stay round; a few rounds, as a wider domain also shrinks the pixels per unit).
        var room = DataLabelHeight(font) + DataLabelGap - DataLabelOverhang;
        for (var round = 0; round < 3 && plotHeight > room * 3; round++)
        {
            var perUnit = plotHeight / (niceMax - niceMin);
            var grow = false;
            if (axis.Spec.Max is null && axis.LabelMax is var top && double.IsFinite(top) && (niceMax - top) * perUnit < room)
            {
                niceMax = NiceTicks.Clean(niceMax + step);
                grow = true;
            }

            if (axis.Spec.Min is null && axis.LabelMin is var bottom && double.IsFinite(bottom) && (bottom - niceMin) * perUnit < room)
            {
                niceMin = NiceTicks.Clean(niceMin - step);
                grow = true;
            }

            if (!grow)
            {
                break;
            }

            (min, max) = (Math.Min(min, niceMin), Math.Max(max, niceMax));
        }

        // Secondary axes put their ticks on the grid axis' lines when they can.
        if (align is { Ticks.Count: > 1, Log: false } grid && axis.Spec.Min is null && axis.Spec.Max is null && axis.Spec.TickBase is null
            && grid.Ticks[0] == grid.DomainMin && grid.Ticks[^1] == grid.DomainMax
            && NiceTicks.AlignedDomain(min, max, grid.Ticks.Count - 1) is { } aligned && aligned.Step >= minStep)
        {
            (niceMin, niceMax, step) = aligned;
        }

        axis.DomainMin = axis.Spec.Min ?? niceMin;
        axis.DomainMax = axis.Spec.Max ?? niceMax;
        axis.Ticks = NiceTicks.Ticks(axis.DomainMin, axis.DomainMax, step);
        var format = NumberFormatter.ForTicks(axis.Ticks, step, culture, axis.Format);
        axis.Labels = axis.Ticks.Select(format).ToList();
        axis.LabelWidth = axis.Visible ? axis.Labels.Select(l => measure.Measure(l, font)).DefaultIfEmpty(0).Max() : 0;
    }

    /// <summary>
    /// Ticks of a log axis: the extent rounded out to powers of ten, unless Min or Max is set (and positive). Secondary
    /// log axes do not align to the grid axis' lines; a log grid has no even steps to share.
    /// </summary>
    private static void ComputeLogTicks(YAxisLayout axis, CultureInfo culture, ITextMeasurer measure, double font, double plotHeight)
    {
        // Every power of ten is worth a label, and they read well closer together than linear ticks (28 px, not 42).
        var target = Math.Clamp((int)(plotHeight / 28), 2, 12);
        var (min, max) = (Math.Min(axis.ExtentMin, axis.ExtentMax), Math.Max(axis.ExtentMin, axis.ExtentMax));
        var (niceMin, niceMax) = NiceTicks.LogDomain(min, max);
        var fixedMin = axis.Spec.Min is > 0 and var lo ? lo : (double?)null;
        var fixedMax = axis.Spec.Max is > 0 and var hi ? hi : (double?)null;

        // The highest bar's data label needs room above it (see ComputeYTicks): one more decade when it has none.
        var room = DataLabelHeight(font) + DataLabelGap - DataLabelOverhang;
        if (fixedMax is null && axis.LabelMax is > 0 and var top && plotHeight > room * 3
            && Math.Log10(niceMax / top) * plotHeight / Math.Log10(niceMax / (fixedMin ?? niceMin)) < room)
        {
            niceMax *= 10;
        }

        axis.DomainMin = fixedMin ?? niceMin;
        axis.DomainMax = fixedMax ?? niceMax;
        if (!(axis.DomainMax > axis.DomainMin))
        {
            axis.DomainMax = axis.DomainMin * 10;
        }

        axis.Ticks = NiceTicks.LogTicks(axis.DomainMin, axis.DomainMax, target);
        var format = NumberFormatter.ForLogTicks(culture, ValueFormat.Of(axis.Spec));
        axis.Labels = axis.Ticks.Select(format).ToList();
        axis.LabelWidth = axis.Visible ? axis.Labels.Select(l => measure.Measure(l, font)).DefaultIfEmpty(0).Max() : 0;
    }

    /// <summary>
    /// Sizes the label columns of the axes on one side, listed from the plot edge outward. The axis next to the plot
    /// keeps the width of its labels (its title may run on over the plot); the outer ones also fit their title, capped at
    /// <paramref name="maxTitle"/> and shortened with "…" beyond it, so titles on the same side never overlap.
    /// </summary>
    private static void SizeAxisColumns(List<YAxisLayout> axes, bool titles, double maxTitle, ITextMeasurer measure, double font)
    {
        for (var i = 0; i < axes.Count; i++)
        {
            var axis = axes[i];
            axis.ColumnWidth = axis.LabelWidth;
            axis.ShownTitle = titles && !string.IsNullOrWhiteSpace(axis.Spec.Title) ? axis.Spec.Title : null;
            if (i > 0 && axis.ShownTitle is { } title)
            {
                var shown = Shorten(title, Math.Max(axis.LabelWidth, maxTitle), measure, font);
                axis.ShownTitle = shown;
                axis.ColumnWidth = Math.Max(axis.LabelWidth, measure.Measure(shown, font));
            }
        }
    }

    /// <summary>Total width of the label columns on one side, with the gaps between them.</summary>
    private static double ColumnsWidth(List<YAxisLayout> axes) =>
        axes.Count == 0 ? 0 : LabelGap + axes.Sum(a => a.ColumnWidth) + (axes.Count - 1) * AxisGap;

    /// <summary>Places label columns outward from the plot edge (direction -1: left, 1: right).</summary>
    private static void PlaceAxisColumns(List<YAxisLayout> axes, double edge, int direction)
    {
        foreach (var axis in axes)
        {
            axis.LabelX = edge;
            edge += direction * (axis.ColumnWidth + AxisGap);
        }
    }

    // ---------- X labels ----------

    /// <summary>Adds numeric or time tick labels, choosing the densest ticks whose labels do not overlap.</summary>
    private static List<double> AddContinuousXLabels(ContinuousScale scale, XKind kind, AxisOptions axis, CultureInfo culture,
        ITextMeasurer measure, double font, double plotBottom, double chartWidth, List<Node> output, TimeConversion? time = null)
    {
        var plotWidth = scale.RangeEnd - scale.RangeStart;
        List<double> ticks = [];
        List<string> labels = [];

        if (kind == XKind.Time)
        {
            var maxCount = Math.Max(2, (int)(plotWidth / 50));
            foreach (var interval in TimeTicks.FromSmallest(scale.DomainMin, scale.DomainMax, maxCount))
            {
                if (time is not null)
                {
                    var (instants, wall) = ZonedTicks(scale.DomainMin, scale.DomainMax, interval, time);
                    ticks = instants;
                    labels = wall.Select(t => TimeFormatter.Format(t, interval, culture, axis.Format)).ToList();
                }
                else
                {
                    ticks = TimeTicks.Generate(scale.DomainMin, scale.DomainMax, interval);
                    labels = ticks.Select(t => TimeFormatter.Format(t, interval, culture, axis.Format)).ToList();
                }

                if (Fits(ticks, labels, scale, measure, font, chartWidth))
                {
                    break;
                }
            }
        }
        else if (scale.IsLog)
        {
            // Powers of ten (with 2 and 5 between them on short ranges), fewer until the labels fit. Like log y axes,
            // one per 28 px to start: the short labels of a log axis read well close together.
            var format = NumberFormatter.ForLogTicks(culture, ValueFormat.Of(axis));
            for (var target = Math.Clamp((int)(plotWidth / 28), 2, 12); target >= 1; target--)
            {
                ticks = NiceTicks.LogTicks(scale.DomainMin, scale.DomainMax, target);
                labels = ticks.Select(format).ToList();
                if (Fits(ticks, labels, scale, measure, font, chartWidth))
                {
                    break;
                }
            }
        }
        else
        {
            for (var target = Math.Clamp((int)(plotWidth / 70), 2, 12); target >= 1; target--)
            {
                var step = Math.Max(NiceTicks.Step(scale.DomainMax - scale.DomainMin, target, axis.TickBase ?? 0), NumberFormatter.MinStep(ValueFormat.Of(axis), culture) ?? 0);
                ticks = NiceTicks.Ticks(scale.DomainMin, scale.DomainMax, step);
                var format = NumberFormatter.ForTicks(ticks, step, culture, ValueFormat.Of(axis));
                labels = ticks.Select(format).ToList();
                if (Fits(ticks, labels, scale, measure, font, chartWidth))
                {
                    break;
                }
            }
        }

        var pixels = ticks.Select(scale.Map).ToList();
        foreach (var label in PlaceLabels(ticks, labels, scale, measure, font, chartWidth))
        {
            if (label.Skip)
            {
                continue; // no room at all
            }

            output.Add(new TextNode(label.Text, new PointF((float)label.X, (float)(plotBottom + 6)), new TextStyle("axis-label"), label.Anchor)
            {
                Baseline = Baseline.Top,
            });
        }

        return pixels;
    }

    private readonly record struct PlacedLabel(string Text, double X, Anchor Anchor, double Left, double Right, bool Skip);

    /// <summary>
    /// Positions labels under their ticks. Labels at the plot edges align to their tick instead
    /// of centering, so they stay clear of the Y labels and inside the chart.
    /// </summary>
    private static List<PlacedLabel> PlaceLabels(List<double> ticks, List<string> labels, ContinuousScale scale, ITextMeasurer measure,
        double font, double chartWidth)
    {
        var placed = new List<PlacedLabel>(ticks.Count);
        for (var i = 0; i < ticks.Count; i++)
        {
            var px = scale.Map(ticks[i]);
            var width = measure.Measure(labels[i], font);
            var anchor = Anchor.Middle;
            var (left, right) = (px - width / 2, px + width / 2);
            if (left < scale.RangeStart - 4)
            {
                (anchor, left, right) = (Anchor.Start, px, px + width);
            }
            else if (right > Math.Min(chartWidth, scale.RangeEnd + 12))
            {
                (anchor, left, right) = (Anchor.End, px - width, px);
            }

            var skip = left < scale.RangeStart - 4 || right > chartWidth;
            placed.Add(new PlacedLabel(labels[i], px, anchor, left, right, skip));
        }

        return placed;
    }

    private static bool Fits(List<double> ticks, List<string> labels, ContinuousScale scale, ITextMeasurer measure, double font, double chartWidth)
    {
        var placed = PlaceLabels(ticks, labels, scale, measure, font, chartWidth).Where(p => !p.Skip).ToList();
        for (var i = 1; i < placed.Count; i++)
        {
            if (placed[i].Left - placed[i - 1].Right < 12)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Category labels as drawn: their angle, which ones show (every n-th), their text and the height of the row.</summary>
    private readonly record struct CategoryLabelPlan(double Angle, int Every, List<string> Texts, double Height, double LineHeight);

    /// <summary>
    /// Plans the category labels. Horizontal when every label fits under its band. Otherwise (or with
    /// <paramref name="rotation"/> set) they slope down to the left at 45 degrees (or the set angle), which fits many
    /// more of them; only when even those overlap is every n-th label shown. A rotation of 0 keeps them horizontal and
    /// thins them. Rotated labels are shortened with "…" to stay within a third of the chart's height and inside its left edge.
    /// </summary>
    private static CategoryLabelPlan PlanCategoryLabels(BandScale band, double? rotation, ITextMeasurer measure, double font,
        double lineHeight, double chartHeight)
    {
        var categories = band.Categories;
        if (categories.Count == 0)
        {
            return new CategoryLabelPlan(0, 1, [], lineHeight + 6, lineHeight);
        }

        var step = Math.Max(1, band.Step);
        var widest = categories.Max(c => measure.Measure(c, font));
        var horizontalEvery = Math.Max(1, (int)Math.Ceiling((widest + 8) / step));
        var angle = rotation is { } r && double.IsFinite(r) ? Math.Clamp(Math.Abs(r), 0, 90) : horizontalEvery == 1 ? 0 : 45;
        if (angle == 0)
        {
            return new CategoryLabelPlan(0, horizontalEvery, [.. categories], lineHeight + 6, lineHeight);
        }

        var radians = angle * Math.PI / 180;
        var (sin, cos) = (Math.Sin(radians), Math.Cos(radians));

        // Rotated labels are parallel: neighbours are step × sin apart, and need a line of text between them.
        var every = Math.Max(1, (int)Math.Ceiling((lineHeight + 2) / (step * sin)));
        var maxLength = Math.Max(font * 4, chartHeight / 3 / sin);
        var texts = new List<string>(categories.Count);
        var longest = 0.0;
        for (var i = 0; i < categories.Count; i++)
        {
            // A label runs from its band's center down to the left: keep it inside the chart's left edge.
            var limit = cos > 0.01 ? Math.Min(maxLength, (band.Center(i) - 2) / cos) : maxLength;
            var text = Shorten(categories[i], Math.Max(font, limit), measure, font);
            texts.Add(text);
            if (i % every == 0)
            {
                longest = Math.Max(longest, measure.Measure(text, font));
            }
        }

        return new CategoryLabelPlan(angle, every, texts, 4 + lineHeight * cos + longest * sin + 2, lineHeight);
    }

    private static void AddCategoryLabels(CategoryLabelPlan plan, BandScale band, double plotBottom, List<Node> output)
    {
        if (plan.Angle == 0)
        {
            for (var i = 0; i < plan.Texts.Count; i += plan.Every)
            {
                output.Add(new TextNode(plan.Texts[i], new PointF((float)band.Center(i), (float)(plotBottom + 6)), new TextStyle("axis-label"), Anchor.Middle)
                {
                    Baseline = Baseline.Top,
                });
            }

            return;
        }

        // The label ends at its band's center and its middle line runs through that point, so the top edge clears the baseline.
        var y = plotBottom + 4 + plan.LineHeight / 2 * Math.Cos(plan.Angle * Math.PI / 180);
        for (var i = 0; i < plan.Texts.Count; i += plan.Every)
        {
            output.Add(new TextNode(plan.Texts[i], new PointF((float)band.Center(i), (float)y), new TextStyle("axis-label"), Anchor.End)
            {
                Rotation = (float)-plan.Angle,
            });
        }
    }
}
