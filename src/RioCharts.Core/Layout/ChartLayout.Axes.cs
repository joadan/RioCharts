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
        public required AxisSpec Spec { get; init; }
        public required string Id { get; init; }
        public required bool Right { get; init; }
        public List<Resolved> Series { get; } = [];

        /// <summary>True when at least one drawable series uses the axis.</summary>
        public bool Visible { get; set; }

        public double ExtentMin { get; set; }
        public double ExtentMax { get; set; }
        public double DomainMin { get; set; }
        public double DomainMax { get; set; } = 1;
        public List<double> Ticks { get; set; } = [];
        public List<string> Labels { get; set; } = [];
        public double LabelWidth { get; set; }

        /// <summary>X of the label column's edge next to the plot.</summary>
        public double LabelX { get; set; }

        public LinearScale Scale { get; set; } = new(0, 1, 1, 0);
    }

    private static List<YAxisLayout> ResolveAxes(ChartSpec spec, List<string> warnings)
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

    private static void YExtent(YAxisLayout axis, IEnumerable<ResolvedAnnotation> annotations)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        var includeZero = false;
        foreach (var r in axis.Series)
        {
            includeZero |= r.Type.IncludesZero;
            Include(r.Upper);
            if (r.Lower is not null)
            {
                Include(r.Lower);
            }
        }

        if (double.IsFinite(min))
        {
            foreach (var a in annotations)
            {
                foreach (var v in a.Values)
                {
                    min = Math.Min(min, v);
                    max = Math.Max(max, v);
                }
            }
        }

        if (!double.IsFinite(min))
        {
            (min, max) = (0, 1);
        }

        if (includeZero)
        {
            min = Math.Min(min, 0);
            max = Math.Max(max, 0);
        }

        axis.ExtentMin = axis.Spec.Min ?? min;
        axis.ExtentMax = axis.Spec.Max ?? max;

        void Include(double[] values)
        {
            foreach (var y in values)
            {
                if (double.IsFinite(y))
                {
                    min = Math.Min(min, y);
                    max = Math.Max(max, y);
                }
            }
        }
    }

    private static void ComputeYTicks(YAxisLayout axis, int target, CultureInfo culture, ITextMeasurer measure, double font, YAxisLayout? align = null)
    {
        var (min, max) = (axis.ExtentMin, axis.ExtentMax);
        if (max < min)
        {
            (min, max) = (max, min);
        }

        if (max == min)
        {
            // All values equal: pad so the axis does not collapse.
            var pad = min == 0 ? 1 : Math.Abs(min) * 0.1;
            if (axis.Spec.Min is null)
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

        var (niceMin, niceMax, step) = NiceTicks.NiceDomain(min, max, target);

        // Secondary axes put their ticks on the grid axis' lines when they can.
        if (align is { Ticks.Count: > 1 } grid && axis.Spec.Min is null && axis.Spec.Max is null
            && grid.Ticks[0] == grid.DomainMin && grid.Ticks[^1] == grid.DomainMax
            && NiceTicks.AlignedDomain(min, max, grid.Ticks.Count - 1) is { } aligned)
        {
            (niceMin, niceMax, step) = aligned;
        }

        axis.DomainMin = axis.Spec.Min ?? niceMin;
        axis.DomainMax = axis.Spec.Max ?? niceMax;
        axis.Ticks = NiceTicks.Ticks(axis.DomainMin, axis.DomainMax, step);
        var format = NumberFormatter.ForTicks(axis.Ticks, step, culture, axis.Spec.Format);
        axis.Labels = axis.Ticks.Select(format).ToList();
        axis.LabelWidth = axis.Visible ? axis.Labels.Select(l => measure.Measure(l, font)).DefaultIfEmpty(0).Max() : 0;
    }

    /// <summary>Places label columns outward from the plot edge (direction -1: left, 1: right).</summary>
    private static void PlaceAxisColumns(List<YAxisLayout> axes, double edge, int direction)
    {
        foreach (var axis in axes)
        {
            axis.LabelX = edge;
            edge += direction * (axis.LabelWidth + AxisGap);
        }
    }

    // ---------- X labels ----------

    /// <summary>Adds numeric or time tick labels, choosing the densest ticks whose labels do not overlap.</summary>
    private static List<double> AddContinuousXLabels(LinearScale scale, XKind kind, AxisSpec axis, CultureInfo culture,
        ITextMeasurer measure, double font, double plotBottom, double chartWidth, List<Node> output)
    {
        var plotWidth = scale.RangeEnd - scale.RangeStart;
        List<double> ticks = [];
        List<string> labels = [];

        if (kind == XKind.Time)
        {
            var maxCount = Math.Max(2, (int)(plotWidth / 50));
            foreach (var interval in TimeTicks.FromSmallest(scale.DomainMin, scale.DomainMax, maxCount))
            {
                ticks = TimeTicks.Generate(scale.DomainMin, scale.DomainMax, interval);
                labels = ticks.Select(t => TimeFormatter.Format(t, interval, culture, axis.Format)).ToList();
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
                var step = NiceTicks.Step(scale.DomainMax - scale.DomainMin, target);
                ticks = NiceTicks.Ticks(scale.DomainMin, scale.DomainMax, step);
                var format = NumberFormatter.ForTicks(ticks, step, culture, axis.Format);
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
    private static List<PlacedLabel> PlaceLabels(List<double> ticks, List<string> labels, LinearScale scale, ITextMeasurer measure,
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

    private static bool Fits(List<double> ticks, List<string> labels, LinearScale scale, ITextMeasurer measure, double font, double chartWidth)
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

    private static void AddCategoryLabels(BandScale band, ITextMeasurer measure, double font, double plotBottom, List<Node> output)
    {
        if (band.Categories.Count == 0)
        {
            return;
        }

        // Show every k-th label when they do not all fit.
        var widest = band.Categories.Max(c => measure.Measure(c, font));
        var every = Math.Max(1, (int)Math.Ceiling((widest + 8) / Math.Max(1, band.Step)));
        for (var i = 0; i < band.Categories.Count; i += every)
        {
            output.Add(new TextNode(band.Categories[i], new PointF((float)band.Center(i), (float)(plotBottom + 6)), new TextStyle("axis-label"), Anchor.Middle)
            {
                Baseline = Baseline.Top,
            });
        }
    }
}
