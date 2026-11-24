using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Geo;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;
using RioCharts.Core.Text;

namespace RioCharts.Core.Layout;

internal static partial class ChartLayout
{
    private const double MapMinOpacity = 0.25;

    private readonly record struct MapBubble(Resolved Series, int Index, double Latitude, double Longitude, double Value, string Label);

    /// <summary>
    /// The map pass: a world map in <see cref="ChartDefinition.Projection"/>. A map series colors each country by
    /// its value (one hue, more opaque for higher values, like a heatmap); map bubble series put a
    /// circle at each point's latitude and longitude, its area proportional to the value.
    /// <see cref="LayoutOptions.MapRegion"/> zooms into a country, with a path bar back to the world.
    /// </summary>
    private static ChartLayoutResult ComputeMap(ChartDefinition spec, List<Resolved> resolved, LayoutOptions options, List<string> warnings, StyleTable styles)
    {
        var culture = options.Culture;
        var texts = options.Texts ?? ChartTexts.For(culture);
        var measure = options.Measurer;
        var font = options.FontSize;
        var small = Math.Max(9, font - 1);
        var lineHeight = font * 1.3;
        var chrome = FitChrome(spec, options, out var compact, out var width, out var height);

        foreach (var r in resolved.Where(r => r.Spec.Type is not (SeriesType.Map or SeriesType.MapBubble)))
        {
            warnings.Add($"series '{r.Spec.Id}': {r.Spec.Type} series cannot be mixed with a map; the series is skipped.");
        }

        var areas = resolved.Where(r => r.Spec.Type == SeriesType.Map).ToList();
        if (areas.Count > 1)
        {
            warnings.Add("only one map series colors the countries; the others are skipped.");
        }

        // ---------- Country values ----------
        var area = areas.FirstOrDefault();
        var values = new Dictionary<GeoRegion, (double Value, int Index)>();
        var combined = new HashSet<GeoRegion>();
        if (area is { Visible: true })
        {
            var labels = SliceLabels(area.Data, culture);
            var unknown = new List<string>();
            for (var i = 0; i < labels.Length; i++)
            {
                var v = area.Data.Y[i];
                if (!double.IsFinite(v))
                {
                    continue;
                }

                if (WorldMap.Find(labels[i]) is not { } region)
                {
                    unknown.Add(labels[i]);
                    continue;
                }

                if (values.TryGetValue(region, out var seen))
                {
                    values[region] = (seen.Value + v, -1); // several items: the sum is no one item's value
                    combined.Add(region);
                }
                else
                {
                    values[region] = (v, i);
                }
            }

            if (unknown.Count > 0)
            {
                warnings.Add($"series '{area.Spec.Id}': {unknown.Count} value(s) for regions not on the map ({string.Join(", ", unknown.Take(5))}); use ISO 3166 codes such as \"SE\" or \"SWE\", or English names.");
            }
        }

        var format = ValueFormat.Of(area?.Spec.Format, spec.Y);
        string F(double v) => ValueFormatter.Format(v, culture, format, compactFrom: 1e5);
        var (vMin, vMax) = values.Count > 0 ? (values.Values.Min(v => v.Value), values.Values.Max(v => v.Value)) : (0d, 1d);
        if (area?.Spec.Max is { } fixedMax && fixedMax > vMin)
        {
            vMax = fixedMax;
        }

        var color = area?.Color ?? ThemeTokens.Series(0);

        // Without a fixed Max, the color follows each value's rank, so a few large values do not wash out the rest.
        var ranks = values.Values.Select(v => v.Value).Distinct().Order().ToList();

        // With a midpoint the colors diverge: above it the series color, below it the below color, each side
        // ranked (or scaled, with Max) on its own from the midpoint out.
        var mid = area?.Spec.Midpoint is { } m && double.IsFinite(m) ? m : (double?)null;
        var below = area?.Spec.BelowColor ?? ThemeTokens.Negative;
        var aboveRanks = mid is { } ma ? ranks.Where(v => v > ma).ToList() : [];
        var belowRanks = mid is { } mb ? ranks.Where(v => v < mb).Reverse().ToList() : []; // nearest the midpoint first
        double Strength(double v)
        {
            if (mid is { } md)
            {
                if (area?.Spec.Max is not null)
                {
                    return DivergingStrength(v, md, vMin, vMax);
                }

                var side = v > md ? aboveRanks : v < md ? belowRanks : null;
                return side is null ? 0 : (double)(side.IndexOf(v) + 1) / side.Count;
            }

            return area?.Spec.Max is not null
                ? (vMax > vMin ? Math.Clamp((v - vMin) / (vMax - vMin), 0, 1) : 1)
                : ranks.Count > 1 ? (double)ranks.BinarySearch(v) / (ranks.Count - 1) : 1;
        }

        double Opacity(double v) => MapMinOpacity + (1 - MapMinOpacity) * Strength(v);
        string ColorOf(double v) => mid is { } mc && v < mc ? below : color;

        // ---------- Bubbles ----------
        var bubbles = new List<MapBubble>();
        foreach (var r in resolved.Where(r => r.Spec.Type == SeriesType.MapBubble && r.Visible))
        {
            var d = r.Data;
            if (d.Latitudes is not { } lat || d.Longitudes is not { } lon)
            {
                if (d.Count > 0)
                {
                    warnings.Add($"series '{r.Spec.Id}': map bubbles need a latitude and longitude per point (SeriesData.WithLocations); nothing is drawn.");
                }

                continue;
            }

            var labels = SliceLabels(d, culture);
            for (var i = 0; i < d.Count; i++)
            {
                if (double.IsFinite(lat[i]) && double.IsFinite(lon[i]) && double.IsFinite(d.Y[i]) && d.Y[i] > 0)
                {
                    bubbles.Add(new MapBubble(r, i, lat[i], lon[i], d.Y[i], labels[i]));
                }
            }
        }

        var hasData = values.Count > 0 || bubbles.Count > 0;
        var focus = compact ? null : WorldMap.Find(options.MapRegion);
        var continent = compact ? null : focus?.Continent ?? WorldMap.FindContinent(options.MapRegion);
        if (focus is null)
        {
            bubbles = GroupByCountry(bubbles);
        }

        // ---------- Title, path bar and color scale ----------
        var statics = new List<Node>();
        var y0 = PadTop;
        if (chrome.Title && !string.IsNullOrWhiteSpace(spec.Title))
        {
            var titleHeight = options.TitleFontSize * 1.3;
            statics.Add(new TextNode(spec.Title!, new PointF((float)PadLeft, (float)(y0 + titleHeight / 2)), new TextStyle("title"), Anchor.Start) { Class = "rio-title" });
            y0 += titleHeight + 6;
        }

        if (focus is not null || continent is not null)
        {
            y0 += MapPathBar(continent, focus, texts, culture, measure, small, y0, options.InteractiveMap, styles, statics);
        }

        var showScale = !compact && values.Count > 0 && spec.Legend.Mode != LegendMode.Hide && (chrome.Legend || spec.Legend.Mode == LegendMode.Show);
        GroupNode Scale(double y) => mid is { } md
            ? DivergingLegend(PadLeft, y, lineHeight + 4, below, color, vMin < md ? F(vMin) : null, F(md), vMax > md ? F(vMax) : null, measure, font, styles,
                texts.Legend, MapMinOpacity, "heat")
            : HeatLegend(PadLeft, y, lineHeight + 4, color, F(vMin), F(vMax), measure, font, styles, texts.Legend, MapMinOpacity);

        // Above the map, or below it with Legend.Position = Bottom.
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

        // ---------- Series legend: names the bubbles (the color scale explains the countries) ----------
        var legendEntries = resolved.Where(r => r.Spec.Type is SeriesType.Map or SeriesType.MapBubble)
            .Select(r => new LegendEntry(r.Spec.Id, r.Spec.DisplayName, r.Color, r.Spec.Type, r.Visible, r.Empty))
            .ToList();
        var showLegend = !compact && spec.Legend.Mode switch
        {
            LegendMode.Show => legendEntries.Count > 0,
            LegendMode.Hide => false,
            _ => chrome.Legend && legendEntries.Any(e => e.Type == SeriesType.MapBubble) && legendEntries.Count > 1,
        };
        if (showLegend)
        {
            var legendItems = MeasureLegend(legendEntries, measure, font);
            var legendRows = WrapRows(legendItems, width - PadLeft - PadRight);
            var legendHeight = legendRows.Count * (lineHeight + 4);
            if (!LegendTooTall(spec, legendHeight, height))
            {
                statics.Add(BuildLegend(legendItems, legendRows, LegendPosition.Top, width - PadRight, new RectF((float)PadLeft, (float)y0, 1, 1), y0, y0,
                    lineHeight, styles, texts.Legend));
                y0 += legendHeight + 6;
            }
        }

        var plot = compact
            ? CompactPlot(width, height)
            : new RectF((float)PadLeft, (float)y0, (float)Math.Max(10, width - PadLeft - PadRight), (float)Math.Max(10, height - PadBottom - (scaleAtBottom ? ScaleRow(lineHeight) : 0) - y0));

        // ---------- Projection: the world, a continent, or the country zoomed into ----------
        var project = Projector(spec.Projection);
        var central = focus is not null ? Middle(focus.MainPolygons.Select(p => p.CenterLongitude)) : continent?.CenterLongitude ?? 0;
        var showAntarctica = focus?.Alpha2 == "AQ" || values.Keys.Any(r => r.Alpha2 == "AQ");
        var fitTo = focus is not null ? focus.MainPolygons.Select(p => p.Rings[0])
            : continent is not null ? [Outline(continent)]
            : WorldMap.Countries.Where(r => showAntarctica || r.Alpha2 != "AQ").SelectMany(r => r.Polygons).Select(p => p.Rings[0]);
        double bx0 = double.MaxValue, by0 = double.MaxValue, bx1 = double.MinValue, by1 = double.MinValue;
        foreach (var outer in fitTo)
        {
            var ring = ProjectRing(outer, central, project);
            for (var i = 0; i < ring.Length; i += 2)
            {
                bx0 = Math.Min(bx0, ring[i]);
                bx1 = Math.Max(bx1, ring[i]);
                by0 = Math.Min(by0, ring[i + 1]);
                by1 = Math.Max(by1, ring[i + 1]);
            }
        }

        var padding = focus is not null ? 1.25 : 1.02;
        var scale = Math.Min(plot.Width / Math.Max(1e-6, (bx1 - bx0) * padding), plot.Height / Math.Max(1e-6, (by1 - by0) * padding));
        var ox = plot.CenterX - scale * (bx0 + bx1) / 2;
        var oy = plot.CenterY - scale * (by0 + by1) / 2;

        // ---------- Countries ----------
        // Zoomed into a continent (or a country on it), the rest of the world fades, so the area shown stands out.
        const float faded = 0.35f;
        styles.Add("map-land", new Style { Fill = ThemeTokens.Grid, FillOpacity = 0.7f, Stroke = ThemeTokens.Surface, StrokeWidth = 0.5f });
        styles.Add("map-land-faded", new Style { Fill = ThemeTokens.Grid, FillOpacity = 0.7f, Stroke = ThemeTokens.Surface, StrokeWidth = 0.5f, Opacity = faded });
        StyleRef ValueStyle(double v, bool fade)
        {
            var opacity = Math.Round(Opacity(v), 2);
            var fill = ColorOf(v);
            return styles.Add($"map-{fill}-{opacity.ToString(CultureInfo.InvariantCulture)}{(fade ? "-faded" : "")}",
                new Style { Fill = fill, FillOpacity = (float)opacity, Stroke = ThemeTokens.Surface, StrokeWidth = 0.5f, Opacity = fade ? faded : null });
        }

        var land = new List<Node>();
        var tips = new List<(GeoRegion Region, int Index, double Value, float[][] Rings, PointF Center)>();
        foreach (var region in WorldMap.Countries.Where(r => showAntarctica || r.Alpha2 != "AQ"))
        {
            var rings = new List<float[]>();
            var path = new PathBuilder();
            foreach (var polygon in region.Polygons)
            {
                foreach (var ring in polygon.Rings)
                {
                    var pixels = ToPixels(ProjectRing(ring, central, project), scale, ox, oy);
                    if (!Overlaps(pixels, plot))
                    {
                        continue;
                    }

                    rings.Add(pixels);
                    path.MoveTo(pixels[0], pixels[1]);
                    for (var i = 2; i < pixels.Length; i += 2)
                    {
                        path.LineTo(pixels[i], pixels[i + 1]);
                    }

                    path.Close();
                }
            }

            if (rings.Count == 0)
            {
                continue;
            }

            var hasValue = values.TryGetValue(region, out var entry);
            var fade = continent is not null && region.Continent != continent && region != focus;
            Node shape = new PathNode(path.Build(), hasValue ? ValueStyle(entry.Value, fade) : new StyleRef(fade ? "map-land-faded" : "map-land"))
            {
                Class = region == focus ? "rio-map-region rio-map-focus" : "rio-map-region",
            };
            if (options.InteractiveMap && !compact)
            {
                // One level down per click: the world zooms to a continent, a continent to a country; the
                // country shown goes back up to its continent.
                var (target, name) = region == focus ? (continent?.Name ?? "", continent?.Name ?? texts.MapWorld)
                    : focus is null && continent is null && region.Continent is { } next ? (next.Name, next.Name)
                    : (region.Code, region.Name);
                shape = new GroupNode([shape])
                {
                    Class = "rio-map-zoom",
                    Interaction = new NodeInteraction("map", target) { Focusable = hasValue || region == focus },
                    AriaLabel = texts.Fill(nameof(ChartTexts.TreemapShow), culture, name),
                };
            }

            land.Add(shape);
            if (hasValue)
            {
                var main = ToPixels(ProjectRing(region.MainPolygons.MaxBy(p => p.Area)!.Rings[0], central, project), scale, ox, oy);
                var xs = main.Where((_, i) => i % 2 == 0).ToList();
                var ys = main.Where((_, i) => i % 2 == 1).ToList();
                tips.Add((region, entry.Index, entry.Value, [.. rings], new PointF((xs.Min() + xs.Max()) / 2, (ys.Min() + ys.Max()) / 2)));
            }
        }

        if (focus is not null)
        {
            var outline = styles.Add("map-focus", new Style { Stroke = ThemeTokens.Text, StrokeWidth = 1.2f });
            var path = new PathBuilder();
            foreach (var ring in focus.Polygons.Select(p => ToPixels(ProjectRing(p.Rings[0], central, project), scale, ox, oy)).Where(r => Overlaps(r, plot)))
            {
                path.MoveTo(ring[0], ring[1]);
                for (var i = 2; i < ring.Length; i += 2)
                {
                    path.LineTo(ring[i], ring[i + 1]);
                }

                path.Close();
            }

            land.Add(new PathNode(path.Build(), outline) { Class = "rio-map-focus-outline" });
        }

        var seriesNodes = new List<Node>
        {
            // Not keyed "series:": the countries are buttons, so they stay in the SVG when the series layer is a canvas.
            new GroupNode(land) { Key = "map:" + (area?.Spec.Id ?? "map"), Class = "rio-series rio-series-map", AriaLabel = area?.Spec.DisplayName ?? texts.Map },
        };

        // ---------- Bubbles: largest first, so smaller ones stay on top ----------
        var maxBubble = bubbles.Count > 0 ? bubbles.Max(b => b.Value) : 1;
        var maxRadius = resolved.FirstOrDefault(r => r.Spec.Type == SeriesType.MapBubble)?.Spec.MarkerSize / 2
            ?? Math.Clamp(Math.Min(plot.Width, plot.Height) / 24, 4, 20);
        var placed = bubbles
            .Select(b => (Bubble: b, At: project(EqualEarth.Wrap(b.Longitude - central), b.Latitude), Radius: Math.Max(2, maxRadius * Math.Sqrt(b.Value / maxBubble))))
            .Select(p => (p.Bubble, X: ox + scale * p.At.X, Y: oy + scale * p.At.Y, p.Radius))
            .Where(p => p.X >= plot.X - p.Radius && p.X <= plot.Right + p.Radius && p.Y >= plot.Y - p.Radius && p.Y <= plot.Bottom + p.Radius)
            .ToList();
        var tooltipSeries = new List<TooltipSeries>();
        foreach (var group in placed.GroupBy(p => p.Bubble.Series))
        {
            var r = group.Key;
            var style = styles.Add("map-bubble-" + r.Color, new Style { Fill = r.Color, FillOpacity = 0.75f, Stroke = ThemeTokens.Surface, StrokeWidth = 1 });
            var circles = new PathBuilder();
            foreach (var p in group.OrderByDescending(p => p.Radius))
            {
                circles.MoveTo(p.X, p.Y - p.Radius).Arc(p.X, p.Y, p.Radius, 0, Math.PI * 2).Close();
            }

            seriesNodes.Add(new PathNode(circles.Build(), style) { Key = "series:" + r.Spec.Id, Class = "rio-series rio-series-mapbubble" });
            var byX = group.OrderBy(p => p.X).ToList();
            var bubbleFormat = ValueFormat.Of(r.Spec.Format, spec.Y);
            tooltipSeries.Add(new TooltipSeries(r.Spec.Id, r.Spec.DisplayName, r.Color, SeriesType.MapBubble, 0, 0, 0,
                [.. byX.Select(p => (float)p.X)], [.. byX.Select(p => (float)p.Y)], [.. byX.Select(p => p.Bubble.Index)],
                [.. byX.Select(p => p.Bubble.Label)], [.. byX.Select(p => ValueFormatter.Format(p.Bubble.Value, culture, bubbleFormat, compactFrom: 1e5))])
            {
                Radii = [.. byX.Select(p => (float)p.Radius)],
            });
        }

        if (area is not null && tips.Count > 0)
        {
            tips.Sort((a, b) => a.Center.X.CompareTo(b.Center.X)); // keyboard navigation steps through points by x
            tooltipSeries.Add(new TooltipSeries(area.Spec.Id, area.Spec.DisplayName, color, SeriesType.Map, 0, 0, 0,
                [.. tips.Select(t => t.Center.X)], [.. tips.Select(t => t.Center.Y)], [.. tips.Select(t => t.Index)],
                [.. tips.Select(t => t.Region.Name)], [.. tips.Select(t => F(t.Value))])
            {
                Shapes = [.. tips.Select(t => t.Rings)],
                PointColors = mid is null ? null : [.. tips.Select(t => ColorOf(t.Value))],
            });
        }

        // ---------- Description: the countries with the highest values ----------
        var kind = (focus?.Name ?? continent?.Name) is { } shown ? $"{texts.Map}, {shown}" : texts.Map;
        var ranked = values.OrderByDescending(v => v.Value.Value).ToList();
        var listed = string.Join(", ", ranked.Take(8).Select(v => $"{v.Key.Name} {F(v.Value.Value)}"))
            + (ranked.Count > 8 ? " " + texts.Fill(nameof(ChartTexts.More), culture, ranked.Count - 8) : "");
        var description = (string.IsNullOrWhiteSpace(spec.Title) ? "" : spec.Title + ". ")
            + (values.Count > 0
                ? texts.FillCount(nameof(ChartTexts.PolarItems), values.Count, culture, kind, values.Count, listed) + "."
                : bubbles.Count > 0 ? ""
                : hasData ? kind + "." : texts.Fill(nameof(ChartTexts.PolarNoData), culture, kind));

        // The bubbles too, per series and largest first (before, a bubble-only map said just "Map.").
        foreach (var series in bubbles.GroupBy(b => b.Series))
        {
            var bubbleValues = ValueFormat.Of(series.Key.Spec.Format, spec.Y);
            var largest = series.OrderByDescending(b => b.Value).ToList();
            var places = string.Join(", ", largest.Take(8).Select(b => $"{b.Label} {ValueFormatter.Format(b.Value, culture, bubbleValues, compactFrom: 1e5)}"))
                + (largest.Count > 8 ? " " + texts.Fill(nameof(ChartTexts.More), culture, largest.Count - 8) : "");
            var name = values.Count > 0 || bubbles.Select(b => b.Series).Distinct().Count() > 1 ? series.Key.Spec.DisplayName : kind;
            description += (description.Length > 0 && !description.EndsWith(' ') ? " " : "")
                + texts.FillCount(nameof(ChartTexts.PolarItems), largest.Count, culture, name, largest.Count, places) + ".";
        }

        var scene = new Scene.Scene(
            new GroupNode(statics) { Class = "rio-static" },
            new GroupNode([new GroupNode(seriesNodes, plot) { Class = "rio-plot" }]) { Class = "rio-series-layer rio-map" },
            new GroupNode([]) { Class = "rio-overlay" })
        {
            Width = (float)width,
            Height = (float)height,
            Styles = styles,
            Description = description,
        };

        var unit = new ContinuousScale(0, 1, plot.Bottom, plot.Y);
        return new ChartLayoutResult
        {
            Scene = scene,
            Plot = plot,
            HitTest = new HitTestIndex(),
            Tooltip = new TooltipModel(plot, spec.Tooltip.Enabled && hasData, false, 0, tooltipSeries),
            Legend = legendEntries,
            Warnings = warnings,
            XKind = XKind.Category,
            YScale = unit,
            YScales = new Dictionary<string, ContinuousScale>(StringComparer.Ordinal) { [spec.Y.Id ?? "y"] = unit },
            HasData = hasData,
            MapRegion = focus?.Code ?? continent?.Name,
        };
    }

    /// <summary>
    /// The world view of map bubbles: per series (with GroupByCountry, the default), the places in one
    /// country become one bubble at their value-weighted middle, named after the country, with the sum
    /// of their values. Places in no country stay as they are.
    /// </summary>
    private static List<MapBubble> GroupByCountry(List<MapBubble> bubbles)
    {
        var grouped = new List<MapBubble>();
        foreach (var series in bubbles.GroupBy(b => b.Series))
        {
            if (series.Key.Spec.GroupByCountry == false)
            {
                grouped.AddRange(series);
                continue;
            }

            foreach (var country in series.GroupBy(b => WorldMap.Locate(b.Latitude, b.Longitude)))
            {
                if (country.Key is not { } region || country.Count() == 1 && country.First().Label == region.Name)
                {
                    grouped.AddRange(country);
                    continue;
                }

                var total = country.Sum(b => b.Value);
                var largest = country.MaxBy(b => b.Value);
                grouped.Add(new MapBubble(series.Key, country.Count() == 1 ? largest.Index : -1, // several places: no one item's value
                    country.Sum(b => b.Latitude * b.Value) / total,
                    largest.Longitude + country.Sum(b => EqualEarth.Wrap(b.Longitude - largest.Longitude) * b.Value) / total,
                    total, region.Name));
            }
        }

        return grouped;
    }

    /// <summary>
    /// A ring projected around <paramref name="central"/>: longitudes are unwrapped along the ring
    /// and clamped to the map's edge, so a ring that crosses the far side of the world is cut there
    /// instead of drawing a line across the map.
    /// </summary>
    private static double[] ProjectRing(double[] ring, double central, Func<double, double, (double X, double Y)> project)
    {
        var projected = new double[ring.Length];
        var relative = new double[ring.Length / 2];
        var previous = EqualEarth.Wrap(ring[0] - central);
        relative[0] = previous;
        for (var i = 1; i < relative.Length; i++)
        {
            var lon = EqualEarth.Wrap(ring[i * 2] - central);
            previous += EqualEarth.Wrap(lon - previous);
            relative[i] = previous;
        }

        var shift = 360 * Math.Round(relative.Average() / 360);
        for (var i = 0; i < relative.Length; i++)
        {
            var (x, y) = project(Math.Clamp(relative[i] - shift, -180, 180), ring[i * 2 + 1]);
            projected[i * 2] = x;
            projected[i * 2 + 1] = y;
        }

        return projected;
    }

    /// <summary>The projection of a longitude relative to the central meridian and a latitude, both in degrees.</summary>
    private static Func<double, double, (double X, double Y)> Projector(MapProjection projection) => projection switch
    {
        MapProjection.Mercator => Mercator.ProjectRelative,
        _ => EqualEarth.ProjectRelative,
    };

    private static float[] ToPixels(double[] projected, double scale, double ox, double oy)
    {
        var pixels = new float[projected.Length];
        for (var i = 0; i < projected.Length; i += 2)
        {
            pixels[i] = (float)Math.Round(ox + scale * projected[i], 1);
            pixels[i + 1] = (float)Math.Round(oy + scale * projected[i + 1], 1);
        }

        return pixels;
    }

    private static bool Overlaps(float[] pixels, RectF plot)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (var i = 0; i < pixels.Length; i += 2)
        {
            x0 = Math.Min(x0, pixels[i]);
            x1 = Math.Max(x1, pixels[i]);
            y0 = Math.Min(y0, pixels[i + 1]);
            y1 = Math.Max(y1, pixels[i + 1]);
        }

        return x1 >= plot.X && x0 <= plot.Right && y1 >= plot.Y && y0 <= plot.Bottom && (x1 - x0 >= 0.5 || y1 - y0 >= 0.5);
    }

    /// <summary>The middle of a set of longitudes, taking the short way around the world.</summary>
    private static double Middle(IEnumerable<double> longitudes)
    {
        var list = longitudes.ToList();
        var first = list[0];
        var offsets = list.Select(l => EqualEarth.Wrap(l - first)).ToList();
        return EqualEarth.Wrap(first + (offsets.Min() + offsets.Max()) / 2);
    }

    /// <summary>A continent's map area as a ring of longitude, latitude pairs, its edges sampled so the projection's curves are followed.</summary>
    private static double[] Outline(GeoContinent continent)
    {
        var points = new List<double>();
        for (var i = 0; i <= 8; i++)
        {
            var lon = continent.West + (continent.East - continent.West) * i / 8;
            var lat = continent.South + (continent.North - continent.South) * i / 8;
            points.AddRange([lon, continent.South, lon, continent.North, continent.West, lat, continent.East, lat]);
        }

        return [.. points];
    }

    /// <summary>The path bar of a zoomed map: "World › Europe › Sweden", each step but the last a button back. Returns its height.</summary>
    private static double MapPathBar(GeoContinent? continent, GeoRegion? focus, ChartTexts texts, CultureInfo culture, ITextMeasurer measure, double font, double y0,
        bool interactive, StyleTable styles, List<Node> statics)
    {
        styles.Add("treemap-crumb", new TextStyleDef { FontSize = (float)font, Fill = ThemeTokens.TextMuted });
        styles.Add("treemap-crumb-current", new TextStyleDef { FontSize = (float)font, FontWeight = 600, Fill = ThemeTokens.Text });
        var height = font * 1.3 + 8;
        var cy = (float)(y0 + height / 2 - 2);
        var steps = new List<(string Label, string Target)> { (texts.MapWorld, "") };
        if (continent is not null)
        {
            steps.Add((continent.Name, continent.Name));
        }

        if (focus is not null)
        {
            steps.Add((focus.Name, focus.Code));
        }

        var x = PadLeft;
        var nodes = new List<Node>();
        for (var i = 0; i < steps.Count; i++)
        {
            var (label, target) = steps[i];
            var last = i == steps.Count - 1;
            var w = measure.Measure(label, font);
            var text = new TextNode(label, new PointF((float)x, cy), new TextStyle(last ? "treemap-crumb-current" : "treemap-crumb"), Anchor.Start);
            nodes.Add(last || !interactive
                ? text
                : new GroupNode(
                [
                    new RectNode(new RectF((float)(x - 3), (float)(cy - height / 2 + 2), (float)(w + 6), (float)(height - 4)), new StyleRef("legend-hit"))
                    {
                        CornerRadius = 3,
                        Class = "rio-legend-hit",
                    },
                    text,
                ])
                {
                    Class = "rio-treemap-crumb",
                    Interaction = new NodeInteraction("map", target),
                    AriaLabel = texts.Fill(nameof(ChartTexts.TreemapShow), culture, label),
                });
            x += w;
            if (!last)
            {
                nodes.Add(new TextNode("›", new PointF((float)(x + 6), cy), new TextStyle("treemap-crumb"), Anchor.Middle));
                x += 12;
            }
        }

        statics.Add(new GroupNode(nodes) { Class = "rio-treemap-path", AriaLabel = texts.TreemapPathLabel });
        return height;
    }
}
