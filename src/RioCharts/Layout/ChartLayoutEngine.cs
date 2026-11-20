namespace RioCharts.Layout;

/// <summary>
/// Maps a <see cref="Chart"/> to pixel positions: plot area, axis ranges and ticks,
/// series screen points, slices, bubbles and legend placement. No rendering happens here.
/// </summary>
public static class ChartLayoutEngine
{
    /// <summary>Horizontal space taken by each additional Y axis on the same side.</summary>
    public const double ExtraAxisSpacing = 60;

    /// <summary>Minimum right margin when a Y axis is drawn on the right.</summary>
    public const double RightAxisMargin = 70;

    private const double LegendInset = 10;
    private const double LegendGap = 10;
    private const double LegendItemSpacing = 16;

    public static ChartLayout Compute(Chart chart, AxisRange? xRangeOverride = null)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var warnings = new List<string>();
        var allSeries = chart.Series ?? [];

        var visibleCartesian = allSeries.Where(s => s.Visible && s is XYSeries or XYZSeries).ToList();
        var visibleValue = allSeries.Where(s => s.Visible).OfType<ValueSeries>().ToList();
        var hasCartesianSeries = allSeries.Any(s => s is XYSeries or XYZSeries);
        var hasValueSeries = allSeries.Any(s => s is ValueSeries);

        // Cartesian axes are drawn when there are XY/XYZ series, or nothing at all (empty chart).
        var hasCartesian = hasCartesianSeries || !hasValueSeries;

        var yAxes = chart.YAxes is { Count: > 0 } ? chart.YAxes : [new Axis()];
        var xAxis = chart.XAxis ?? new Axis();

        var outer = new PlotArea(0, 0, Math.Max(1, chart.Width), Math.Max(1, chart.Height));
        var margin = chart.Margin ?? new ChartMargin(60, 40, 60, 80);

        // Extra room for multiple Y axes: even indexes go left, odd indexes go right.
        var leftAxes = hasCartesian ? (yAxes.Count + 1) / 2 : 0;
        var rightAxes = hasCartesian ? yAxes.Count / 2 : 0;
        var left = margin.Left + Math.Max(0, leftAxes - 1) * ExtraAxisSpacing;
        var right = rightAxes > 0 ? Math.Max(margin.Right, RightAxisMargin) + (rightAxes - 1) * ExtraAxisSpacing : margin.Right;
        var plot = Shrink(outer, margin.Top, right, margin.Bottom, left);

        // Legend: edge positions take space from the plot; corners overlay it
        // (except for pie/donut/funnel charts, where overlaying would cover slices).
        var legendItems = BuildLegendItems(chart, allSeries);
        var legendSettings = chart.Legend ?? new LegendSettings();
        var showLegend = legendSettings.Visible && legendItems.Count > 0;
        var horizontalLegend = legendSettings.Position is LegendPosition.Top or LegendPosition.Bottom;
        var legendSize = showLegend ? MeasureLegend(legendItems, legendSettings, horizontalLegend, plot.Width) : default;
        if (showLegend)
        {
            plot = ReserveLegendSpace(plot, legendSettings.Position, legendSize, reserveCorners: !hasCartesian);
        }

        AxisLayout? xLayout = null;
        var yLayouts = new List<AxisLayout>();
        var seriesLayouts = new List<SeriesLayout>();
        var hasData = false;
        (double Min, double Max)? xFilter = null;

        if (hasCartesian)
        {
            xLayout = BuildXAxis(xAxis, visibleCartesian, plot, xRangeOverride, warnings);
            xFilter = xLayout.IsZoomed ? (xLayout.Min, xLayout.Max) : null;
            for (var i = 0; i < yAxes.Count; i++)
            {
                var axisSeries = visibleCartesian.Where(s => ResolveYAxisIndex(s, yAxes.Count) == i).ToList();
                yLayouts.Add(BuildYAxis(yAxes[i], i, axisSeries, plot, xFilter, warnings));
            }

            foreach (var s in allSeries.OfType<ISeries>().Where(s => s is XYSeries or XYZSeries))
            {
                var axisIndex = ResolveYAxisIndex(s, yAxes.Count);
                if (axisIndex != RawYAxisIndex(s))
                {
                    warnings.Add($"Series '{s.Name}' references Y axis {RawYAxisIndex(s)} which does not exist; using axis 0.");
                }
            }
        }

        // Value series share the plot area side by side.
        var valueSlots = SplitHorizontally(plot, visibleValue.Count);
        var valueSlot = 0;

        for (var i = 0; i < allSeries.Count; i++)
        {
            var series = allSeries[i];
            if (series is null)
            {
                continue;
            }

            var color = SeriesColor(chart, series, i);
            SeriesLayout layout;
            switch (series)
            {
                case XYSeries xy when hasCartesian:
                    layout = LayoutXY(chart, xy, i, color, xLayout!, yLayouts[ResolveYAxisIndex(xy, yLayouts.Count)], xFilter);
                    break;
                case XYZSeries xyz when hasCartesian:
                    layout = LayoutXYZ(xyz, i, color, xLayout!, yLayouts[ResolveYAxisIndex(xyz, yLayouts.Count)], xFilter);
                    break;
                case ValueSeries value when value.Visible:
                    layout = LayoutValue(chart, value, i, color, valueSlots[valueSlot++]);
                    break;
                default:
                    layout = new SeriesLayout { Series = series, SeriesIndex = i, Color = color, IsEmpty = true };
                    break;
            }

            if (series.Visible && !layout.IsEmpty)
            {
                hasData = true;
            }

            seriesLayouts.Add(layout);
        }

        var ordered = seriesLayouts
            .Select((layout, order) => (layout, order))
            .OrderBy(t => t.layout.Series.RenderOrder)
            .ThenBy(t => t.order)
            .Select(t => t.layout)
            .ToList();

        var emptySeries = seriesLayouts.Where(l => l.IsEmpty).Select(l => l.Series).ToHashSet();
        var legend = showLegend
            ? PlaceLegend(legendItems, emptySeries, legendSettings, legendSize, horizontalLegend, plot, outer, besidePlot: !hasCartesian)
            : null;

        return new ChartLayout
        {
            Chart = chart,
            Plot = plot,
            HasCartesian = hasCartesian,
            HasData = hasData,
            XAxis = xLayout,
            YAxes = yLayouts,
            Series = ordered,
            Legend = legend,
            Warnings = warnings,
        };
    }

    /// <summary>Color used for a series: explicit color, otherwise the palette color for its index.</summary>
    public static string SeriesColor(Chart chart, ISeries series, int index) => series switch
    {
        XYSeries { StrokeColor: { Length: > 0 } c } => c,
        XYZSeries { FillColor: { Length: > 0 } c } => c,
        _ => ChartPalette.Pick(chart.Palette, index),
    };

    private static int RawYAxisIndex(ISeries series) => series switch
    {
        XYSeries xy => xy.YAxisIndex,
        XYZSeries xyz => xyz.YAxisIndex,
        _ => 0,
    };

    private static int ResolveYAxisIndex(ISeries series, int axisCount)
    {
        var index = RawYAxisIndex(series);
        return index >= 0 && index < axisCount ? index : 0;
    }

    private static PlotArea Shrink(PlotArea area, double top, double right, double bottom, double left)
    {
        var width = Math.Max(1, area.Width - left - right);
        var height = Math.Max(1, area.Height - top - bottom);
        return new PlotArea(area.Left + left, area.Top + top, width, height);
    }

    // ---------- Axes ----------

    private static AxisLayout BuildXAxis(Axis axis, List<ISeries> series, PlotArea plot, AxisRange? overrideRange, List<string> warnings)
    {
        var extent = DataExtent(series, horizontal: true, xFilter: null);
        var (fullMin, fullMax, fullStep) = ResolveRange(axis, extent.Min, extent.Max, includeZero: false, "X", warnings,
            tight: extent.Tight || AxisLabelFormatter.IsDateFormat(axis));

        double min = fullMin, max = fullMax, step = fullStep;
        var zoomed = false;
        if (overrideRange is not null && IsFinite(overrideRange.Minimum) && IsFinite(overrideRange.Maximum)
            && overrideRange.Minimum < overrideRange.Maximum)
        {
            min = overrideRange.Minimum;
            max = overrideRange.Maximum;
            step = AxisStep(axis, max - min);
            zoomed = true;
        }

        return new AxisLayout
        {
            Axis = axis,
            Side = AxisSide.Bottom,
            Min = min,
            Max = max,
            FullMin = fullMin,
            FullMax = fullMax,
            IsZoomed = zoomed,
            TickStep = step,
            Ticks = AxisTicks(axis, min, max, step),
            PixelStart = plot.Left,
            PixelEnd = plot.Right,
            Offset = plot.Bottom,
        };
    }

    private static AxisLayout BuildYAxis(Axis axis, int index, List<ISeries> series, PlotArea plot, (double Min, double Max)? xFilter,
        List<string> warnings)
    {
        // When zoomed, Y fits the data inside the visible X range.
        var extent = DataExtent(series, horizontal: false, xFilter);
        if (xFilter is not null && extent.Min is null)
        {
            extent = DataExtent(series, horizontal: false, xFilter: null);
        }

        var (min, max, step) = ResolveRange(axis, extent.Min, extent.Max, extent.IncludeZero, $"Y[{index}]", warnings,
            tight: extent.Tight || AxisLabelFormatter.IsDateFormat(axis));

        // Even indexes stack outward on the left, odd indexes on the right.
        var side = index % 2 == 0 ? AxisSide.Left : AxisSide.Right;
        var level = index / 2;
        var offset = side == AxisSide.Left
            ? plot.Left - level * ExtraAxisSpacing
            : plot.Right + level * ExtraAxisSpacing;

        return new AxisLayout
        {
            Axis = axis,
            Side = side,
            Index = index,
            Min = min,
            Max = max,
            FullMin = min,
            FullMax = max,
            TickStep = step,
            Ticks = AxisTicks(axis, min, max, step),
            PixelStart = plot.Bottom,
            PixelEnd = plot.Top,
            Offset = offset,
        };
    }

    private static IReadOnlyList<double> AxisTicks(Axis axis, double min, double max, double step) =>
        AxisLabelFormatter.IsDateFormat(axis) ? NiceScale.DateTicks(min, max, step) : NiceScale.Ticks(min, max, step);

    /// <summary>
    /// Data range along one direction for the given series, with padding for heatmap cells
    /// (half a cell, range kept tight) and bubbles (so large bubbles are not cut off).
    /// </summary>
    private static (double? Min, double? Max, bool IncludeZero, bool Tight) DataExtent(List<ISeries> series, bool horizontal,
        (double Min, double Max)? xFilter)
    {
        bool InRange(double x) => xFilter is not { } f || (x >= f.Min && x <= f.Max);

        double? min = null, max = null;
        var includeZero = false;
        var tight = false;
        double heatmapPad = 0;
        var hasBubbles = false;

        foreach (var s in series)
        {
            switch (s)
            {
                case XYSeries xy:
                    foreach (var p in xy.Points)
                    {
                        if (InRange(p.X))
                        {
                            Extend(ref min, ref max, horizontal ? p.X : p.Y, horizontal ? p.Y : p.X);
                        }
                    }

                    includeZero |= !horizontal && !string.IsNullOrEmpty(xy.FillColor) && xy.Points.Count > 0;
                    break;
                case XYZSeries xyz:
                    foreach (var p in xyz.Points)
                    {
                        if (InRange(p.X))
                        {
                            Extend(ref min, ref max, horizontal ? p.X : p.Y, horizontal ? p.Y : p.X);
                        }
                    }

                    if (xyz.Type == XYZSeriesType.Heatmap)
                    {
                        tight = true;
                        heatmapPad = Math.Max(heatmapPad, MinSpacing(xyz.Points.Select(p => horizontal ? p.X : p.Y)) / 2);
                    }
                    else if (xyz.Points.Count > 0)
                    {
                        hasBubbles = true;
                    }

                    break;
            }
        }

        if (min is not null && max is not null)
        {
            var pad = heatmapPad;
            if (hasBubbles)
            {
                pad = Math.Max(pad, (max.Value - min.Value) * 0.1);
            }

            min -= pad;
            max += pad;
        }

        return (min, max, includeZero, tight);
    }

    private static void Extend(ref double? min, ref double? max, double value, double other)
    {
        if (!IsFinite(value) || !IsFinite(other))
        {
            return;
        }

        min = min is null ? value : Math.Min(min.Value, value);
        max = max is null ? value : Math.Max(max.Value, value);
    }

    private static double MinSpacing(IEnumerable<double> values)
    {
        var sorted = values.Where(IsFinite).Distinct().Order().ToList();
        var best = double.MaxValue;
        for (var i = 1; i < sorted.Count; i++)
        {
            best = Math.Min(best, sorted[i] - sorted[i - 1]);
        }

        return best == double.MaxValue ? 1 : best;
    }

    private static double AxisStep(Axis axis, double span) =>
        AxisLabelFormatter.IsDateFormat(axis)
            ? NiceScale.DateStep(span, axis.TargetTickCount)
            : NiceScale.Step(span, axis.TargetTickCount);

    /// <summary>
    /// Resolves an axis range from explicit settings and data. Invalid explicit ranges
    /// fall back to [0, 100] with a warning; flat data is padded. When <paramref name="tight"/>
    /// is true (heatmaps, date axes) the data range is used as is instead of rounding out to ticks.
    /// </summary>
    public static (double Min, double Max, double Step) ResolveRange(Axis axis, double? dataMin, double? dataMax,
        bool includeZero, string axisName, List<string> warnings, bool tight = false)
    {
        var explicitMin = axis.Minimum is { } m && IsFinite(m) ? m : (double?)null;
        var explicitMax = axis.Maximum is { } n && IsFinite(n) ? n : (double?)null;

        if (explicitMin is not null && explicitMax is not null && explicitMin >= explicitMax)
        {
            warnings.Add($"Axis {axisName} has minimum {explicitMin} >= maximum {explicitMax}; using [0, 100].");
            return (0, 100, AxisStep(axis, 100));
        }

        if (dataMin is null || dataMax is null)
        {
            // No data: use explicit ends where given, otherwise a default range.
            var lo = explicitMin ?? (explicitMax is { } hi0 ? Math.Min(0, hi0 - 100) : 0);
            var hi = explicitMax ?? Math.Max(lo + 100, 100);
            return (lo, hi, AxisStep(axis, hi - lo));
        }

        var min = explicitMin ?? dataMin.Value;
        var max = explicitMax ?? dataMax.Value;

        if (includeZero)
        {
            if (explicitMin is null && min > 0)
            {
                min = 0;
            }

            if (explicitMax is null && max < 0)
            {
                max = 0;
            }
        }

        if (min >= max)
        {
            if (explicitMin is not null && explicitMax is null)
            {
                max = min + Math.Max(1, Math.Abs(min) * 0.1);
            }
            else if (explicitMax is not null && explicitMin is null)
            {
                min = max - Math.Max(1, Math.Abs(max) * 0.1);
            }
            else
            {
                // Flat data: pad around the single value.
                var pad = min == 0 ? 1 : Math.Abs(min) * 0.1;
                if (explicitMin is null)
                {
                    min -= pad;
                }

                if (explicitMax is null)
                {
                    max += pad;
                }
            }
        }

        var step = AxisStep(axis, max - min);
        if (axis.AutoRange && !tight)
        {
            if (explicitMin is null)
            {
                min = Math.Floor(min / step + 1e-9) * step;
            }

            if (explicitMax is null)
            {
                max = Math.Ceiling(max / step - 1e-9) * step;
            }

            if (min >= max)
            {
                max = min + step;
            }

            step = AxisStep(axis, max - min);
        }

        return (min, max, step);
    }

    // ---------- Series ----------

    private static SeriesLayout LayoutXY(Chart chart, XYSeries series, int index, string color, AxisLayout x, AxisLayout y,
        (double Min, double Max)? xFilter)
    {
        var points = series.Points ?? [];
        var indices = new List<int>(points.Count);
        bool InRange(int i) => i >= 0 && i < points.Count && points[i].X >= xFilter!.Value.Min && points[i].X <= xFilter.Value.Max;
        for (var i = 0; i < points.Count; i++)
        {
            if (!IsFinite(points[i].X) || !IsFinite(points[i].Y))
            {
                continue;
            }

            // When zoomed, keep points in view plus their neighbours so lines run to the plot edge.
            // Downsampling then works on the visible part only, so zooming in shows more detail.
            if (xFilter is null || InRange(i) || InRange(i - 1) || InRange(i + 1))
            {
                indices.Add(i);
            }
        }

        IReadOnlyList<int> kept = indices;
        var downsampled = false;
        if (chart.DownsampleThreshold > 0 && indices.Count > chart.DownsampleThreshold)
        {
            kept = Lttb.Downsample(points, indices, chart.DownsampleThreshold);
            downsampled = kept.Count < indices.Count;
        }

        var screen = new List<ScreenPoint>(kept.Count);
        foreach (var i in kept)
        {
            screen.Add(new ScreenPoint(x.ToPixel(points[i].X), y.ToPixel(points[i].Y)));
        }

        double? baseline = null;
        if (!string.IsNullOrEmpty(series.FillColor))
        {
            baseline = y.ToPixel(Math.Clamp(0, y.Min, y.Max));
        }

        return new SeriesLayout
        {
            Series = series,
            SeriesIndex = index,
            Color = color,
            ScreenPoints = screen,
            PointIndices = kept,
            BaselineY = baseline,
            LayoutXAxis = x,
            LayoutYAxis = y,
            IsEmpty = screen.Count == 0,
            IsDownsampled = downsampled,
        };
    }

    private static SeriesLayout LayoutXYZ(XYZSeries series, int index, string color, AxisLayout x, AxisLayout y,
        (double Min, double Max)? xFilter)
    {
        var points = series.Points ?? [];
        var indices = new List<int>(points.Count);
        double zMin = double.MaxValue, zMax = double.MinValue;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (IsFinite(p.X) && IsFinite(p.Y) && IsFinite(p.Z))
            {
                indices.Add(i);
                zMin = Math.Min(zMin, p.Z);
                zMax = Math.Max(zMax, p.Z);
            }
        }

        var screen = new List<ScreenPoint>(indices.Count);
        var sizes = new List<double>(indices.Count);
        var intensities = new List<double>(indices.Count);
        var minSize = Math.Max(0, Math.Min(series.MinBubbleSize, series.MaxBubbleSize));
        var maxSize = Math.Max(series.MinBubbleSize, series.MaxBubbleSize);

        foreach (var i in indices)
        {
            var p = points[i];
            screen.Add(new ScreenPoint(x.ToPixel(p.X), y.ToPixel(p.Y)));
            var t = zMax > zMin ? (p.Z - zMin) / (zMax - zMin) : 1;

            // Bubble area (not radius) is proportional to Z.
            sizes.Add(minSize + (maxSize - minSize) * Math.Sqrt(Math.Max(0, t)));
            intensities.Add(t);
        }

        double cellWidth = 0, cellHeight = 0;
        if (series.Type == XYZSeriesType.Heatmap && indices.Count > 0)
        {
            var dx = MinSpacing(indices.Select(i => points[i].X));
            var dy = MinSpacing(indices.Select(i => points[i].Y));
            cellWidth = Math.Abs(x.ToPixel(x.Min + dx) - x.ToPixel(x.Min));
            cellHeight = Math.Abs(y.ToPixel(y.Min + dy) - y.ToPixel(y.Min));
        }

        return new SeriesLayout
        {
            Series = series,
            SeriesIndex = index,
            Color = color,
            ScreenPoints = screen,
            PointIndices = indices,
            BubbleSizes = series.Type == XYZSeriesType.Bubble ? sizes : null,
            Intensities = series.Type == XYZSeriesType.Heatmap ? intensities : null,
            CellWidth = cellWidth,
            CellHeight = cellHeight,
            LayoutXAxis = x,
            LayoutYAxis = y,
            IsEmpty = screen.Count == 0,
        };
    }

    private static SeriesLayout LayoutValue(Chart chart, ValueSeries series, int index, string color, PlotArea region)
    {
        var points = series.Points ?? [];
        var visible = new List<int>();
        double total = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p is not null && p.Visible && IsFinite(p.Value) && p.Value > 0)
            {
                visible.Add(i);
                total += p.Value;
            }
        }

        var slices = new List<SliceLayout>(visible.Count);
        if (visible.Count > 0)
        {
            if (series.Type == ValueSeriesType.Funnel)
            {
                LayoutFunnel(chart, points, visible, total, region, slices);
            }
            else
            {
                LayoutPie(chart, series, points, visible, total, region, slices);
            }
        }

        return new SeriesLayout
        {
            Series = series,
            SeriesIndex = index,
            Color = color,
            Slices = slices,
            IsEmpty = slices.Count == 0,
        };
    }

    private static void LayoutPie(Chart chart, ValueSeries series, List<ValuePoint> points, List<int> visible, double total,
        PlotArea region, List<SliceLayout> slices)
    {
        var center = new ScreenPoint(region.CenterX, region.CenterY);
        var outer = Math.Max(1, Math.Min(region.Width, region.Height) / 2 * 0.9);
        var anyExploded = visible.Any(i => points[i].IsExploded);
        if (anyExploded)
        {
            outer *= 0.92; // leave room for the offset
        }

        var inner = series.Type == ValueSeriesType.Donut ? outer * Math.Clamp(series.InnerRadiusRatio, 0, 0.95) : 0;
        var angle = 0.0;
        foreach (var i in visible)
        {
            var p = points[i];
            var fraction = p.Value / total;
            var sweep = fraction * Math.PI * 2;
            var mid = angle + sweep / 2;
            var c = center;
            if (p.IsExploded && visible.Count > 1)
            {
                var offset = outer * 0.08;
                c = new ScreenPoint(center.X + Math.Sin(mid) * offset, center.Y - Math.Cos(mid) * offset);
            }

            var labelRadius = inner > 0 ? (inner + outer) / 2 : outer * 0.65;
            slices.Add(new SliceLayout
            {
                PointIndex = i,
                Point = p,
                Color = p.Color is { Length: > 0 } pc ? pc : ChartPalette.Pick(chart.Palette, i),
                Fraction = fraction,
                StartAngle = angle,
                EndAngle = angle + sweep,
                Center = c,
                OuterRadius = outer,
                InnerRadius = inner,
                LabelPosition = new ScreenPoint(c.X + Math.Sin(mid) * labelRadius, c.Y - Math.Cos(mid) * labelRadius),
            });
            angle += sweep;
        }
    }

    private static void LayoutFunnel(Chart chart, List<ValuePoint> points, List<int> visible, double total,
        PlotArea region, List<SliceLayout> slices)
    {
        const double gap = 2;
        var maxValue = visible.Max(i => points[i].Value);
        var maxWidth = region.Width * 0.9;
        var segmentHeight = (region.Height - gap * (visible.Count - 1)) / visible.Count;
        for (var n = 0; n < visible.Count; n++)
        {
            var i = visible[n];
            var p = points[i];
            var topWidth = p.Value / maxValue * maxWidth;
            var bottomWidth = n + 1 < visible.Count ? points[visible[n + 1]].Value / maxValue * maxWidth : topWidth * 0.8;
            var top = region.Top + n * (segmentHeight + gap);
            var bottom = top + segmentHeight;
            var cx = region.CenterX;
            slices.Add(new SliceLayout
            {
                PointIndex = i,
                Point = p,
                Color = p.Color is { Length: > 0 } pc ? pc : ChartPalette.Pick(chart.Palette, i),
                Fraction = p.Value / total,
                Polygon =
                [
                    new ScreenPoint(cx - topWidth / 2, top),
                    new ScreenPoint(cx + topWidth / 2, top),
                    new ScreenPoint(cx + bottomWidth / 2, bottom),
                    new ScreenPoint(cx - bottomWidth / 2, bottom),
                ],
                LabelPosition = new ScreenPoint(cx, (top + bottom) / 2),
            });
        }
    }

    private static List<PlotArea> SplitHorizontally(PlotArea plot, int count)
    {
        var result = new List<PlotArea>(count);
        if (count == 0)
        {
            return result;
        }

        var width = plot.Width / count;
        for (var i = 0; i < count; i++)
        {
            result.Add(new PlotArea(plot.Left + i * width, plot.Top, width, plot.Height));
        }

        return result;
    }

    // ---------- Legend ----------

    private sealed record LegendEntry(ISeries Series, ValuePoint? Point, int PointIndex, string Label, string Color, bool Enabled, MarkerStyle Marker, bool IsLine);

    private static List<LegendEntry> BuildLegendItems(Chart chart, List<ISeries> series)
    {
        var items = new List<LegendEntry>();

        // Several pies/donuts usually share categories: list each label once.
        var seenLabels = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < series.Count; i++)
        {
            var s = series[i];
            switch (s)
            {
                case null:
                    continue;
                case ValueSeries value:
                    for (var p = 0; p < value.Points.Count; p++)
                    {
                        var point = value.Points[p];
                        if (point is null || !seenLabels.Add(point.Label ?? ""))
                        {
                            continue;
                        }

                        var pointColor = point.Color is { Length: > 0 } pc ? pc : ChartPalette.Pick(chart.Palette, p);
                        items.Add(new LegendEntry(value, point, p, point.Label ?? "", pointColor, value.Visible && point.Visible, MarkerStyle.Square, false));
                    }

                    break;
                case XYSeries xy:
                    items.Add(new LegendEntry(xy, null, -1, xy.Name, SeriesColor(chart, xy, i), xy.Visible,
                        xy.Marker, xy.ShowLine));
                    break;
                default:
                    items.Add(new LegendEntry(s, null, -1, s.Name, SeriesColor(chart, s, i), s.Visible, MarkerStyle.Circle, false));
                    break;
            }
        }

        return items;
    }

    /// <summary>Approximate rendered width of text (no DOM measurement available).</summary>
    public static double EstimateTextWidth(string? text, double fontSize) => (text?.Length ?? 0) * fontSize * 0.62;

    private static double ItemHeight(LegendSettings s) => Math.Max(s.FontSize, s.MarkerSize) + 6;

    private static double ItemWidth(LegendEntry item, LegendSettings s) =>
        SymbolWidth(item, s) + 6 + EstimateTextWidth(item.Label, s.FontSize);

    private static double SymbolWidth(LegendEntry item, LegendSettings s) => item.IsLine ? Math.Max(18, s.MarkerSize * 1.8) : s.MarkerSize;

    private static (double Width, double Height) MeasureLegend(List<LegendEntry> items, LegendSettings s, bool horizontal, double maxWidth)
    {
        var rowHeight = ItemHeight(s);
        if (!horizontal)
        {
            var width = items.Max(i => ItemWidth(i, s)) + 2 * s.Padding;
            var height = items.Count * rowHeight + 2 * s.Padding;
            return (width, height);
        }

        var rows = LayoutRows(items, s, maxWidth - 2 * s.Padding);
        var w = rows.Max(r => r.Width) + 2 * s.Padding;
        return (w, rows.Count * rowHeight + 2 * s.Padding);
    }

    private static List<(List<LegendEntry> Items, double Width)> LayoutRows(List<LegendEntry> items, LegendSettings s, double maxWidth)
    {
        var rows = new List<(List<LegendEntry> Items, double Width)>();
        var current = new List<LegendEntry>();
        double width = 0;
        foreach (var item in items)
        {
            var w = ItemWidth(item, s);
            var add = current.Count == 0 ? w : w + LegendItemSpacing;
            if (current.Count > 0 && width + add > maxWidth)
            {
                rows.Add((current, width));
                current = [];
                width = 0;
                add = w;
            }

            current.Add(item);
            width += add;
        }

        if (current.Count > 0)
        {
            rows.Add((current, width));
        }

        return rows;
    }

    private static PlotArea ReserveLegendSpace(PlotArea plot, LegendPosition position, (double Width, double Height) size, bool reserveCorners)
    {
        var w = size.Width + LegendGap;
        var h = size.Height + LegendGap;
        return position switch
        {
            LegendPosition.Right => plot with { Width = Math.Max(1, plot.Width - w) },
            LegendPosition.Left => plot with { Left = plot.Left + w, Width = Math.Max(1, plot.Width - w) },
            LegendPosition.Top => plot with { Top = plot.Top + h, Height = Math.Max(1, plot.Height - h) },
            LegendPosition.Bottom => plot with { Height = Math.Max(1, plot.Height - h) },
            LegendPosition.TopRight or LegendPosition.BottomRight when reserveCorners => plot with { Width = Math.Max(1, plot.Width - w) },
            LegendPosition.TopLeft or LegendPosition.BottomLeft when reserveCorners => plot with { Left = plot.Left + w, Width = Math.Max(1, plot.Width - w) },
            _ => plot,
        };
    }

    private static LegendLayout PlaceLegend(List<LegendEntry> items, HashSet<ISeries> emptySeries, LegendSettings s,
        (double Width, double Height) size, bool horizontal, PlotArea plot, PlotArea outer, bool besidePlot)
    {
        var (w, h) = size;
        double x, y;
        switch (s.Position)
        {
            case LegendPosition.Top:
                x = plot.CenterX - w / 2;
                y = plot.Top - h - LegendGap;
                break;
            // Bottom/Left/Right sit at the chart edge, outside the axis labels in the margin.
            case LegendPosition.Bottom:
                x = plot.CenterX - w / 2;
                y = outer.Bottom - h - LegendGap / 2;
                break;
            case LegendPosition.Left:
                x = outer.Left + LegendGap / 2;
                y = plot.CenterY - h / 2;
                break;
            case LegendPosition.Right:
                x = outer.Right - w - LegendGap / 2;
                y = plot.CenterY - h / 2;
                break;
            case LegendPosition.TopLeft:
                x = plot.Left + LegendInset;
                y = plot.Top + LegendInset;
                break;
            case LegendPosition.BottomLeft:
                x = plot.Left + LegendInset;
                y = plot.Bottom - h - LegendInset;
                break;
            case LegendPosition.BottomRight:
                x = plot.Right - w - LegendInset;
                y = plot.Bottom - h - LegendInset;
                break;
            default: // TopRight
                x = plot.Right - w - LegendInset;
                y = plot.Top + LegendInset;
                break;
        }

        // When corner space was reserved (value-only charts), the legend sits beside the plot.
        if (besidePlot)
        {
            switch (s.Position)
            {
                case LegendPosition.TopRight:
                case LegendPosition.BottomRight:
                    x = plot.Right + LegendGap;
                    y = s.Position == LegendPosition.TopRight ? plot.Top : plot.Bottom - h;
                    break;
                case LegendPosition.TopLeft:
                case LegendPosition.BottomLeft:
                    x = plot.Left - w - LegendGap;
                    y = s.Position == LegendPosition.TopLeft ? plot.Top : plot.Bottom - h;
                    break;
            }
        }

        x = Math.Clamp(x, 0, Math.Max(0, outer.Width - w));
        y = Math.Clamp(y, 0, Math.Max(0, outer.Height - h));

        var rowHeight = ItemHeight(s);
        var placed = new List<LegendItemLayout>(items.Count);
        if (horizontal)
        {
            var rows = LayoutRows(items, s, w - 2 * s.Padding + 0.5);
            for (var r = 0; r < rows.Count; r++)
            {
                var ix = x + s.Padding + (w - 2 * s.Padding - rows[r].Width) / 2;
                foreach (var item in rows[r].Items)
                {
                    var iw = ItemWidth(item, s);
                    placed.Add(ToItem(item, emptySeries, ix, y + s.Padding + r * rowHeight, iw, rowHeight));
                    ix += iw + LegendItemSpacing;
                }
            }
        }
        else
        {
            for (var n = 0; n < items.Count; n++)
            {
                placed.Add(ToItem(items[n], emptySeries, x + s.Padding, y + s.Padding + n * rowHeight, w - 2 * s.Padding, rowHeight));
            }
        }

        return new LegendLayout { X = x, Y = y, Width = w, Height = h, Items = placed };
    }

    private static LegendItemLayout ToItem(LegendEntry e, HashSet<ISeries> emptySeries, double x, double y, double width, double height) => new()
    {
        Series = e.Series,
        Point = e.Point,
        PointIndex = e.PointIndex,
        Label = e.Label,
        Color = e.Color,
        Enabled = e.Enabled,
        IsEmpty = e.Point is null && emptySeries.Contains(e.Series) && e.Enabled,
        Marker = e.Marker,
        IsLine = e.IsLine,
        X = x,
        Y = y,
        Width = width,
        Height = height,
    };

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
