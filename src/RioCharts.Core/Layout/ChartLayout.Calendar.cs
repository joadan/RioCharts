using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;

namespace RioCharts.Core.Layout;

public static partial class ChartLayout
{
    private const double MsPerDay = 86_400_000;

    /// <summary>Largest calendar cell, so a few weeks of data do not fill the chart with huge squares.</summary>
    private const double CalendarMaxCell = 40;

    /// <summary>
    /// Calendar heatmap: a square per day, weeks as columns and weekdays as rows (the week starts on
    /// the culture's first day), colored like a heatmap. Up to a year of data is one block from the first
    /// day to the last; longer data is one block per calendar year, each labeled with its year. Month names
    /// sit above the first full week of each month. Days are taken in the display zone.
    /// </summary>
    private static ChartLayoutResult ComputeCalendar(ChartDefinition spec, List<Resolved> resolved, LayoutOptions options, List<string> warnings, StyleTable styles,
        TimeConversion? time)
    {
        var culture = options.Culture;
        var texts = options.Texts ?? ChartTexts.For(culture);
        var measure = options.Measurer;
        var compact = spec.Compact || AutoCompacts(spec, options);
        var width = Math.Max(compact ? 8 : 50, options.Width);
        var height = Math.Max(compact ? 8 : 50, options.Height);
        var font = options.FontSize;
        var lineHeight = font * 1.3;
        var chrome = FitChrome(spec.AutoCompact && !compact, width, height, font);

        foreach (var other in resolved.Where(r => r.Spec.Type != SeriesType.Calendar))
        {
            warnings.Add($"series '{other.Spec.Id}': {other.Spec.Type} series cannot be mixed with a calendar; the series is skipped.");
        }

        var calendars = resolved.Where(r => r.Spec.Type == SeriesType.Calendar).ToList();
        foreach (var extra in calendars.Skip(1))
        {
            warnings.Add($"series '{extra.Spec.Id}': a chart shows one calendar; the series is skipped.");
        }

        var r = calendars[0];
        var d = r.Data;
        if (d.Count > 0 && d.Kind != XKind.Time)
        {
            warnings.Add($"series '{r.Spec.Id}': x values are {d.Kind}; a calendar needs dates or times. Nothing is drawn.");
        }

        // ---------- Days (in the display zone) ----------
        var byDay = new SortedDictionary<int, int>();
        int? repeated = null;
        if (d.Kind == XKind.Time)
        {
            for (var i = 0; i < d.Count; i++)
            {
                var wall = time?.ToDisplay(d.X[i]) ?? d.X[i];
                if (!double.IsFinite(d.Y[i]) || !double.IsFinite(wall))
                {
                    continue;
                }

                var day = (int)Math.Floor(wall / MsPerDay);
                if (!byDay.TryAdd(day, i))
                {
                    repeated ??= day;
                }
            }
        }

        string DayLabel(int day)
        {
            var date = DateTime.UnixEpoch.AddDays(day);
            return spec.X.Format is { } f
                ? date.ToString(f, culture)
                : culture.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek) + " " + date.ToString("d", culture);
        }

        if (repeated is { } twice)
        {
            warnings.Add($"series '{r.Spec.Id}': several values on {DayLabel(twice)}; only the first is drawn. Combine them with an aggregate (Aggregate or YAggregate on a series component, 'aggregate' in a definition).");
        }

        var hasData = byDay.Count > 0;
        var values = byDay.Values.Select(i => d.Y[i]).ToList();
        var (vMin, vMax) = hasData ? (values.Min(), values.Max()) : (0d, 1d);
        if (r.Spec.Max is { } fixedMax && fixedMax > vMin)
        {
            vMax = fixedMax;
        }

        var color = r.Spec.Color ?? ThemeTokens.Series(0);
        var format = ValueFormat.Of(r.Spec.Format, spec.Y);
        string F(double v) => ValueFormatter.Format(v, culture, format);
        var mid = r.Spec.Midpoint is { } m && double.IsFinite(m) ? m : (double?)null;
        var below = r.Spec.BelowColor ?? ThemeTokens.Negative;
        double Opacity(double v) => mid is { } md
            ? HeatMinOpacity + (1 - HeatMinOpacity) * DivergingStrength(v, md, vMin, vMax)
            : vMax > vMin ? HeatMinOpacity + (1 - HeatMinOpacity) * Math.Clamp((v - vMin) / (vMax - vMin), 0, 1) : 0.6;
        bool IsBelow(double v) => mid is { } md && v < md;
        StyleRef CellStyle(double v)
        {
            var opacity = Math.Round(Opacity(v), 2);
            var key = opacity.ToString(CultureInfo.InvariantCulture);
            return IsBelow(v)
                ? styles.Add($"heat-below-{key}", new Style { Fill = below, FillOpacity = (float)opacity })
                : styles.Add($"heat-{key}", new Style { Fill = color, FillOpacity = (float)opacity });
        }

        // ---------- Title and color legend ----------
        var statics = new List<Node>();
        var y0 = PadTop;
        if (!compact && chrome.Title && !string.IsNullOrWhiteSpace(spec.Title))
        {
            var titleHeight = options.TitleFontSize * 1.3;
            statics.Add(new TextNode(spec.Title!, new PointF((float)PadLeft, (float)(y0 + titleHeight / 2)), new TextStyle("title"), Anchor.Start) { Class = "rio-title" });
            y0 += titleHeight + 6;
        }

        var showScale = !compact && hasData && spec.Legend.Mode != LegendMode.Hide && (chrome.Legend || spec.Legend.Mode == LegendMode.Show);
        GroupNode Scale(double y) => mid is { } md
            ? DivergingLegend(PadLeft, y, lineHeight + 4, below, color, vMin < md ? F(vMin) : null, F(md), vMax > md ? F(vMax) : null, measure, font, styles,
                texts.Legend, HeatMinOpacity, "heat")
            : HeatLegend(PadLeft, y, lineHeight + 4, color, F(vMin), F(vMax), measure, font, styles, texts.Legend);

        // Above the plot, or below it with Legend.Position = Bottom.
        var scaleAtBottom = showScale && spec.Legend.Position == LegendPosition.Bottom;
        if (showScale && !scaleAtBottom)
        {
            statics.Add(Scale(y0));
            y0 += ScaleRow(lineHeight);
        }
        else if (scaleAtBottom)
        {
            statics.Add(Scale(height - PadBottom - (lineHeight + 4)));
        }

        // ---------- Blocks: the data's days, or one per calendar year ----------
        var blocks = new List<(int Start, int End, int? Year)>();
        if (hasData)
        {
            int first = byDay.Keys.First(), last = byDay.Keys.Last();
            if (last - first < 371)
            {
                blocks.Add((first, last, null));
            }
            else
            {
                for (var year = DateTime.UnixEpoch.AddDays(first).Year; year <= DateTime.UnixEpoch.AddDays(last).Year; year++)
                {
                    blocks.Add((DayNumber(new DateTime(year, 1, 1)), DayNumber(new DateTime(year, 12, 31)), year));
                }
            }
        }

        var firstDow = (int)culture.DateTimeFormat.FirstDayOfWeek;
        int Row(int day) => ((Weekday(day) - firstDow) % 7 + 7) % 7;
        int Column((int Start, int End, int? Year) block, int day) => (day - Row(day) - (block.Start - Row(block.Start))) / 7;
        var maxWeeks = blocks.Count == 0 ? 1 : blocks.Max(b => Column(b, b.End) + 1);

        var dayNames = Enumerable.Range(0, 7).Select(row => culture.DateTimeFormat.AbbreviatedDayNames[(firstDow + row) % 7]).ToArray();
        var years = blocks.Where(b => b.Year is not null).Select(b => b.Year!.Value.ToString(culture)).ToList();
        var showDays = !compact && chrome.YLabels;
        var showMonths = !compact && chrome.XLabels;
        var labelWidth = compact ? 0 : Math.Max(showDays ? dayNames.Max(n => measure.Measure(n, font)) : 0, years.Count > 0 ? years.Max(y => measure.Measure(y, font)) : 0);
        var plotLeft = compact ? 0 : PadLeft + (labelWidth > 0 ? labelWidth + LabelGap : 0);
        var plotRight = compact ? width : width - PadRight;
        var plotTop = compact ? 0 : y0;
        var plotBottom = compact ? height : height - PadBottom - (scaleAtBottom ? ScaleRow(lineHeight) : 0);
        var monthRow = showMonths || years.Count > 0 ? lineHeight + 2 : 0;
        var blockGap = blocks.Count > 1 ? Math.Max(6, font * 0.8) : 0;
        var blockCount = Math.Max(1, blocks.Count);
        var cell = Math.Min((plotRight - plotLeft) / maxWeeks, (plotBottom - plotTop - blockCount * monthRow - (blockCount - 1) * blockGap) / (7.0 * blockCount));
        cell = Math.Max(1, compact ? cell : Math.Min(CalendarMaxCell, cell));
        var plot = new RectF((float)plotLeft, (float)plotTop, (float)Math.Max(2, plotRight - plotLeft), (float)Math.Max(2, plotBottom - plotTop));

        // Weekday names on every row when they fit, else Monday, Wednesday and Friday, else none.
        bool Named(int row) => showDays && (cell >= lineHeight * 0.95
            || cell * 2 >= font && (DayOfWeek)((firstDow + row) % 7) is DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday);

        var gap = cell >= 10 ? 2f : cell >= 4 ? 1f : 0f;
        var cellNodes = new List<Node>();
        var emptyNodes = new List<Node>();
        var selectedCells = new List<Node>();
        var labelNodes = new List<Node>();
        var axisNodes = new List<Node>();
        var points = new List<(float X, float Y, int Index)>();
        var selectedHere = options.SelectedPoints is { } selected && selected.TryGetValue(r.Spec.Id, out var picked) && picked.Count > 0 ? picked : null;
        var empty = styles.Add("calendar-empty", new Style { Fill = ThemeTokens.Grid });
        styles.Add("heat-label", new TextStyleDef { FontSize = (float)Math.Max(9, font - 1), Fill = ThemeTokens.Text });
        styles.Add("heat-label-inverse", new TextStyleDef { FontSize = (float)Math.Max(9, font - 1), FontWeight = 500, Fill = ThemeTokens.DataLabelInverse });
        for (var k = 0; k < blocks.Count; k++)
        {
            var block = blocks[k];
            var top = plotTop + k * (monthRow + 7 * cell + blockGap);
            var gridTop = top + monthRow;
            var weeks = Column(block, block.End) + 1;

            if (!compact && block.Year is { } year)
            {
                axisNodes.Add(new TextNode(year.ToString(culture), new PointF((float)PadLeft, (float)(top + monthRow / 2)), new TextStyle("axis-title"), Anchor.Start)
                {
                    Class = "rio-calendar-year",
                });
            }

            for (var row = 0; row < 7 && !compact; row++)
            {
                if (Named(row))
                {
                    axisNodes.Add(new TextNode(dayNames[row], new PointF((float)(plotLeft - LabelGap), (float)(gridTop + cell * (row + 0.5))), new TextStyle("axis-label"),
                        Anchor.End) { Class = "rio-row-label" });
                }
            }

            if (showMonths)
            {
                // Each month's name above its first full week (the block's first month above its first column).
                var right = double.NegativeInfinity;
                var date = DateTime.UnixEpoch.AddDays(block.Start);
                for (var month = new DateTime(date.Year, date.Month, 1); DayNumber(month) <= block.End; month = month.AddMonths(1))
                {
                    var start = DayNumber(month);
                    var column = start <= block.Start ? 0 : Column(block, start) + (Row(start) == 0 ? 0 : 1);
                    var name = culture.DateTimeFormat.GetAbbreviatedMonthName(month.Month);
                    var x = plotLeft + column * cell;
                    var w = measure.Measure(name, font);
                    if (column >= weeks || x < right + 6 || (column == 0 && start < block.Start && Column(block, DayNumber(month.AddMonths(1))) * cell < w + 6))
                    {
                        continue; // past the end, too close to the previous name, or a month that has barely begun
                    }

                    axisNodes.Add(new TextNode(name, new PointF((float)x, (float)(top + monthRow / 2)), new TextStyle("axis-label"), Anchor.Start) { Class = "rio-month-label" });
                    right = x + w;
                }
            }

            for (var day = block.Start; day <= block.End; day++)
            {
                var rect = new RectF((float)(plotLeft + Column(block, day) * cell + gap / 2), (float)(gridTop + Row(day) * cell + gap / 2),
                    (float)Math.Max(0.5, cell - gap), (float)Math.Max(0.5, cell - gap));
                var radius = cell >= 8 ? 2 : 0;
                if (!byDay.TryGetValue(day, out var i))
                {
                    emptyNodes.Add(new RectNode(rect, empty) { CornerRadius = radius });
                    continue;
                }

                var v = d.Y[i];
                cellNodes.Add(new RectNode(rect, CellStyle(v)) { CornerRadius = radius });
                if (selectedHere?.Contains(i) == true)
                {
                    selectedCells.Add(new RectNode(new RectF(rect.X + 0.75f, rect.Y + 0.75f, Math.Max(0.5f, rect.Width - 1.5f), Math.Max(0.5f, rect.Height - 1.5f)),
                        styles.Add("selected-bar", new Style { Stroke = ThemeTokens.Text, StrokeWidth = 1.5f })) { CornerRadius = 2, Class = "rio-selected-bar" });
                }

                var cx = rect.X + rect.Width / 2;
                var cy = rect.Y + rect.Height / 2;
                points.Add((cx, cy, i));
                if (r.Spec.DataLabels == true && !compact)
                {
                    var text = F(v);
                    if (measure.Measure(text, font - 1) + 4 <= cell && cell >= font + 2)
                    {
                        labelNodes.Add(new TextNode(text, new PointF(cx, cy), new TextStyle(Opacity(v) > 0.55 ? "heat-label-inverse" : "heat-label"), Anchor.Middle)
                        {
                            Class = "rio-data-label",
                        });
                    }
                }
            }
        }

        if (axisNodes.Count > 0)
        {
            statics.Add(new GroupNode(axisNodes) { Class = "rio-axis-x", AriaLabel = texts.XAxis });
        }

        var overlay = new List<Node>();
        if (!hasData && !compact)
        {
            overlay.Add(new TextNode(texts.NoData, new PointF(plot.CenterX, plot.CenterY), new TextStyle("empty"), Anchor.Middle) { Class = "rio-empty" });
        }

        // ---------- Tooltip and hit testing ----------
        points.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        var hitTest = new HitTestIndex();
        var hits = new HitTestIndex.SeriesHits(r.Spec.Id, [.. points.Select(p => p.X)], [.. points.Select(p => p.Y)], [.. points.Select(p => p.Index)],
            [.. points.Select(p => d.ToSourceIndex(p.Index))]);
        hitTest.Add(hits);
        string PointDay(int i) => DayLabel((int)Math.Floor((time?.ToDisplay(d.X[i]) ?? d.X[i]) / MsPerDay));
        var tooltipSeries = new List<TooltipSeries>
        {
            new(r.Spec.Id, r.Spec.DisplayName, color, SeriesType.Calendar, (float)(cell / 2), (float)cell, 0,
                hits.X, hits.Y, hits.Index, [.. points.Select(p => PointDay(p.Index))], [.. points.Select(p => F(d.Y[p.Index]))])
            {
                CellHeight = (float)cell,
                PointColors = mid is null ? null : [.. points.Select(p => IsBelow(d.Y[p.Index]) ? below : color)],
            },
        };

        var seriesLayer = new GroupNode(
        [
            new GroupNode(
            [
                new GroupNode(emptyNodes) { Class = "rio-calendar-empty" },
                new GroupNode(cellNodes) { Key = "series:calendar", Class = "rio-series rio-series-calendar" },
                .. selectedCells.Count == 0 ? Array.Empty<Node>() : [new GroupNode(selectedCells) { Key = "selection:points", Class = "rio-selected-points" }],
            ]) { Class = "rio-plot" },
            new GroupNode(labelNodes) { Class = "rio-labels" },
        ])
        {
            Class = "rio-series-layer",
        };

        // "Calendar heatmap with 365 days: lowest 0 at Sun 1/4/2026, highest 42 at Tue 3/3/2026."
        var low = byDay.Values.OrderBy(i => d.Y[i]).FirstOrDefault(-1);
        var high = byDay.Values.OrderByDescending(i => d.Y[i]).FirstOrDefault(-1);
        var description = (string.IsNullOrWhiteSpace(spec.Title) ? "" : spec.Title + ". ")
            + (hasData
                ? texts.FillCount(nameof(ChartTexts.CalendarDays), byDay.Count, culture, texts.CalendarChart, byDay.Count,
                    texts.Fill(nameof(ChartTexts.LowestHighest), culture, F(d.Y[low]), PointDay(low), F(d.Y[high]), PointDay(high))) + "."
                : texts.Fill(nameof(ChartTexts.PolarNoData), culture, texts.CalendarChart));

        var scene = new Scene.Scene(new GroupNode(statics) { Class = "rio-static" }, seriesLayer, new GroupNode(overlay) { Class = "rio-overlay" })
        {
            Width = (float)width,
            Height = (float)height,
            Styles = styles,
            Description = description,
        };

        var yScale = new ContinuousScale(7, 0, plot.Bottom, plot.Y);
        return new ChartLayoutResult
        {
            Scene = scene,
            Plot = plot,
            HitTest = hitTest,
            Tooltip = new TooltipModel(plot, spec.Tooltip.Enabled && hasData, false, 0, tooltipSeries),
            Legend = [],
            Warnings = warnings,
            XKind = XKind.Time,
            YScale = yScale,
            YScales = new Dictionary<string, ContinuousScale> { ["y"] = yScale },
            HasData = hasData,
        };
    }

    /// <summary>Days since 1 January 1970.</summary>
    private static int DayNumber(DateTime date) => (int)Math.Floor((date - DateTime.UnixEpoch.Date).TotalDays);

    /// <summary>The day of the week of a day number (1 January 1970 was a Thursday).</summary>
    private static int Weekday(int day) => ((day + (int)DayOfWeek.Thursday) % 7 + 7) % 7;
}
