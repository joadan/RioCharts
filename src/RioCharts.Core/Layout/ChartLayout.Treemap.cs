using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;
using RioCharts.Core.Text;

namespace RioCharts.Core.Layout;

public static partial class ChartLayout
{
    /// <summary>A group or item of a treemap, built from the paths.</summary>
    private sealed class TreeNode(string name, TreeNode? parent)
    {
        public string Name { get; } = name;
        public TreeNode? Parent { get; } = parent;
        public List<TreeNode> Children { get; } = [];
        public Dictionary<string, TreeNode> ByName { get; } = new(StringComparer.Ordinal);
        public double Value { get; set; }

        /// <summary>The item's data index; -1 for groups and "Other".</summary>
        public int Index { get; set; } = -1;

        /// <summary>The top-level group it belongs to (its color).</summary>
        public int Group { get; set; }

        public bool IsOther { get; init; }
        public bool HadValue { get; set; }
        public RectF Rect { get; set; }
        public bool HasHeader { get; set; }

        /// <summary>A group at the last visible level (VisibleLevels): drawn as one cell with its total.</summary>
        public bool Collapsed { get; set; }

        /// <summary>Drawn as a cell: an item, or a collapsed group.</summary>
        public bool IsCell => Children.Count == 0 || Collapsed;

        public IEnumerable<TreeNode> Ancestors()
        {
            for (var n = Parent; n?.Parent is not null; n = n.Parent)
            {
                yield return n;
            }
        }

        public string PathText(string separator) =>
            string.Join(separator, Ancestors().Reverse().Select(a => a.Name).Append(Name));
    }

    /// <summary>
    /// The treemap pass: the series' x values are paths ("Europe/Sweden/Stockholm"), split into
    /// groups and items, laid out with the squarified algorithm. The legend lists the top-level
    /// groups, which also pick the colors; hiding one leaves it out.
    /// </summary>
    private static ChartLayoutResult ComputeTreemap(ChartDefinition spec, List<Resolved> resolved, LayoutOptions options, List<string> warnings, StyleTable styles)
    {
        var culture = options.Culture;
        var texts = options.Texts ?? ChartTexts.For(culture);
        var measure = options.Measurer;
        var width = Math.Max(50, options.Width);
        var height = Math.Max(50, options.Height);
        var font = options.FontSize;
        var small = Math.Max(9, font - 1);
        var lineHeight = font * 1.3;
        var chrome = FitChrome(spec.AutoCompact, width, height, font);

        foreach (var r in resolved.Where(r => r.Spec.Type != SeriesType.Treemap))
        {
            warnings.Add($"series '{r.Spec.Id}': {r.Spec.Type} series cannot be mixed with a treemap; the series is skipped.");
        }

        var trees = resolved.Where(r => r.Spec.Type == SeriesType.Treemap).ToList();
        if (trees.Count > 1)
        {
            warnings.Add("only one treemap series is drawn; the others are skipped.");
        }

        var series = trees[0];
        var format = series.Spec.Format ?? spec.Y.Format ?? "";
        string Format(double v) => ValueFormatter.Format(v, culture, format, compactFrom: 1e5);

        // ---------- The tree ----------
        var root = BuildTree(series, culture, warnings, out var leftOut);
        var groups = root.Children.ToList(); // before hiding: the legend keeps hidden groups
        for (var g = 0; g < groups.Count; g++)
        {
            SetGroup(groups[g], g);
        }

        bool Hidden(string group) => options.HiddenSeries?.Contains(group) == true;
        root.Children.RemoveAll(c => Hidden(c.Name));
        if (series.Spec.GroupSmallSlices is > 0 and var threshold)
        {
            GroupSmall(root, threshold, texts.Other);
        }

        Sum(root);
        var total = root.Value;
        var hasData = total > 0 && series.Visible;

        // Drilled into a group (LayoutOptions.TreemapPath): it fills the plot, its children pick the
        // colors, and a path bar above leads back up. A path that is not (or no longer) there shows all.
        var separator = string.IsNullOrEmpty(series.Spec.PathSeparator) ? "/" : series.Spec.PathSeparator;
        var focus = hasData ? FindGroup(root, options.TreemapPath, separator) ?? root : root;
        var drilled = focus != root;
        for (var g = 0; drilled && g < focus.Children.Count; g++)
        {
            SetGroup(focus.Children[g], g);
        }

        string ColorOf(int group) => groups.Count == 1 && !drilled && series.Spec.Color is { } own ? own : ThemeTokens.Series(group);

        // An item's own color (SeriesData.Colors), else its group's; groups keep their group's color.
        string NodeColor(TreeNode n) => n.Children.Count == 0 && n.Index >= 0 ? series.Data.ColorAt(n.Index, ColorOf(n.Group)) : ColorOf(n.Group);

        // ---------- Legend: the top-level groups ----------
        var depth = MaxDepth(root);
        var legendEntries = groups.Select(g => new LegendEntry(g.Name, g.Name, ColorOf(g.Group), SeriesType.Treemap, !Hidden(g.Name), false,
                GroupTip(g)))
            .ToList();
        string GroupTip(TreeNode g)
        {
            var lines = new List<string> { $"{g.Name}\t{Format(g.Value)}" + (total > 0 && !Hidden(g.Name) ? $" ({Percent(g.Value / total, culture)})" : "") };
            lines.AddRange(g.Children.Take(8).Select(c => $"{OtherPartPrefix}{c.Name}\t{Format(c.Value)}"));
            return string.Join("\n", lines);
        }

        var showLegend = spec.Legend.Mode switch
        {
            LegendMode.Show => legendEntries.Count > 0,
            LegendMode.Hide => false,
            _ => depth > 1 && legendEntries.Count is > 1 and <= 12 && chrome.Legend,
        } && !drilled; // drilled, the colors follow the group's own children and the path bar leads back
        var legendPosition = FitLegendPosition(spec.Legend.Position, chrome);

        // ---------- Title and legend ----------
        var statics = new List<Node>();
        var y0 = PadTop;
        if (chrome.Title && !string.IsNullOrWhiteSpace(spec.Title))
        {
            var titleHeight = options.TitleFontSize * 1.3;
            statics.Add(new TextNode(spec.Title!, new PointF((float)PadLeft, (float)(y0 + titleHeight / 2)), new TextStyle("title"), Anchor.Start)
            {
                Class = "rio-title",
            });
            y0 += titleHeight + 6;
        }

        if (drilled)
        {
            y0 += PathBar(focus, separator, texts, culture, measure, small, y0, options.InteractiveTreemap, styles, statics);
        }

        var legendItems = showLegend ? MeasureLegend(legendEntries, measure, font) : [];
        double legendTop = 0, legendBottom = 0, legendRight = 0;
        List<List<(LegendEntry Entry, double Width)>> legendRows = [];
        if (showLegend)
        {
            if (legendPosition == LegendPosition.Right)
            {
                legendRight = legendItems.Max(i => i.Width) + 16;
            }
            else
            {
                legendRows = WrapRows(legendItems, width - PadLeft - PadRight);
                var legendHeight = legendRows.Count * (lineHeight + 4);
                if (LegendTooTall(spec, legendHeight, height))
                {
                    showLegend = false;
                    legendRows = [];
                }
                else
                {
                    (legendTop, legendBottom) = legendPosition == LegendPosition.Top ? (legendHeight + 8, 0.0) : (0.0, legendHeight + 8);
                }
            }
        }

        var plotTop = y0 + legendTop + 2;
        var plotBottom = height - PadBottom - legendBottom - 2;
        var plotRight = width - PadRight - legendRight;
        var plot = new RectF((float)PadLeft, (float)plotTop, (float)Math.Max(10, plotRight - PadLeft), (float)Math.Max(10, plotBottom - plotTop));
        if (showLegend)
        {
            statics.Add(BuildLegend(legendItems, legendRows, legendPosition, width - PadRight - legendRight + 16, plot, y0,
                plotBottom + 8, lineHeight, styles, texts.Legend));
        }

        // ---------- Rectangles ----------
        var headerHeight = small * 1.25 + 6;
        if (hasData)
        {
            Place(focus, plot, headerHeight, focus, 0, series.Spec.VisibleLevels is > 0 and var levels ? levels : int.MaxValue);
        }

        var showLabels = series.Spec.DataLabels ?? true;
        styles.Add("treemap-header", new TextStyleDef { FontSize = (float)small, FontWeight = 600, Fill = ThemeTokens.Text });
        styles.Add("treemap-header-value", new TextStyleDef { FontSize = (float)small, Fill = ThemeTokens.TextMuted });
        styles.Add("treemap-value", new TextStyleDef { FontSize = (float)small, FontWeight = 400, Fill = ThemeTokens.DataLabelInverse });
        // Per top-level group (in walk order: a group's tint before its items), so hovering its
        // legend item can highlight it like a series.
        var shapes = focus.Children.ToDictionary(g => g.Group, _ => new List<Node>());
        var groupTexts = focus.Children.ToDictionary(g => g.Group, _ => new List<Node>());
        var leaves = new List<TreeNode>();
        var parents = new List<TreeNode>();
        foreach (var node in hasData ? WalkShown(focus).Skip(1) : [])
        {
            var r = node.Rect;
            if (r.Width < 0.5 || r.Height < 0.5)
            {
                continue;
            }

            var color = NodeColor(node);
            if (!node.IsCell)
            {
                parents.Add(node);
                Node tint = new RectNode(r, styles.Add("treemap-group-" + color, new Style { Fill = color, FillOpacity = 0.16f }))
                {
                    CornerRadius = 3,
                    Class = "rio-treemap-group",
                };
                if (options.InteractiveTreemap && !options.TreemapDrillOnItems)
                {
                    // Click the group (its header or frame) to drill into it. (When items drill, the
                    // whole top group is the button instead, below.)
                    tint = new GroupNode([tint])
                    {
                        Class = "rio-treemap-drill",
                        Interaction = new NodeInteraction("treemap", node.PathText(separator)),
                        AriaLabel = texts.Fill(nameof(ChartTexts.TreemapShow), culture, node.PathText(" › ")),
                    };
                }

                shapes[node.Group].Add(tint);
                if (node.HasHeader && showLabels)
                {
                    var room = r.Width - 12;
                    var value = Format(node.Value);
                    var valueWidth = measure.Measure(value, small);
                    var name = Shorten(node.Name, room - (room > valueWidth * 2.5 ? valueWidth + 8 : 0), measure, small);
                    var y = r.Y + headerHeight / 2;
                    if (Readable(name, node.Name))
                    {
                        groupTexts[node.Group].Add(new TextNode(name, new PointF(r.X + 6, (float)y), new TextStyle("treemap-header"), Anchor.Start) { Class = "rio-treemap-header" });
                    }

                    if (room > valueWidth * 2.5 && measure.Measure(name, small) + valueWidth + 8 <= room)
                    {
                        groupTexts[node.Group].Add(new TextNode(value, new PointF(r.Right - 6, (float)y), new TextStyle("treemap-header-value"), Anchor.End));
                    }
                }

                continue;
            }

            // An item: inset by half the gap, so neighbours are set apart.
            leaves.Add(node);
            var inset = new RectF(r.X + 1, r.Y + 1, Math.Max(0.5f, r.Width - 2), Math.Max(0.5f, r.Height - 2));
            var style = node.IsOther
                ? styles.Add("treemap-other-" + color, new Style { Fill = color, FillOpacity = 0.55f })
                : styles.Add("treemap-" + color, new Style { Fill = color });
            Node cell = new RectNode(inset, style)
            {
                CornerRadius = 2,
                Class = node.IsOther ? "rio-treemap-cell rio-treemap-other" : node.Collapsed ? "rio-treemap-cell rio-treemap-collapsed" : "rio-treemap-cell",
            };
            if (node.Collapsed && options.InteractiveTreemap && !options.TreemapDrillOnItems)
            {
                // A collapsed group drills into itself (when items drill, its top group is the button).
                cell = new GroupNode([cell])
                {
                    Class = "rio-treemap-drill",
                    Interaction = new NodeInteraction("treemap", node.PathText(separator)),
                    AriaLabel = texts.Fill(nameof(ChartTexts.TreemapShow), culture, node.PathText(" \u203a ")),
                };
            }

            shapes[node.Group].Add(cell);
            if (showLabels && inset.Width >= 28 && inset.Height >= small * 1.25 + 6)
            {
                // A collapsed group: "Photos ›", a hint that it opens.
                var full = node.Collapsed ? node.Name + " ›" : node.Name;
                var name = Shorten(full, inset.Width - 12, measure, small);
                if (Readable(name, full))
                {
                    var top = inset.Y + 4 + small * 0.65;
                    groupTexts[node.Group].Add(new TextNode(name, new PointF(inset.X + 6, (float)top), new TextStyle("data-label-inverse"), Anchor.Start));
                    var value = Format(node.Value);
                    if (inset.Height >= small * 2.5 + 8 && measure.Measure(value, small) <= inset.Width - 12)
                    {
                        groupTexts[node.Group].Add(new TextNode(value, new PointF(inset.X + 6, (float)(top + small * 1.25)), new TextStyle("treemap-value"), Anchor.Start));
                    }
                }
            }
        }

        // Selected items: an outline.
        var selected = new List<Node>();
        if (options.SelectedPoints is { } picked && picked.TryGetValue(series.Spec.Id, out var indexes) && indexes.Count > 0)
        {
            var outline = styles.Add("selected-bar", new Style { Stroke = ThemeTokens.Text, StrokeWidth = 1.5f });
            foreach (var leaf in leaves.Where(l => l.Index >= 0 && indexes.Contains(l.Index)))
            {
                var r = leaf.Rect;
                selected.Add(new RectNode(new RectF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), outline) { CornerRadius = 3, Class = "rio-selected-bar" });
            }
        }

        // Each top-level group is keyed like a series (its legend item's id), so legend hover and
        // HighlightSeriesAsync with a group name fade the others.
        var seriesNodes = new List<Node>
        {
            new GroupNode([.. focus.Children.Select(g =>
            {
                var summary = $"{g.Name} {Format(g.Value)} ({Percent(g.Value / Math.Max(total, double.Epsilon), culture)})";
                // Items do not click (no point clicks or selection): anywhere in a group drills one
                // level into it, so a group too small for a header can still be opened.
                var drill = options.InteractiveTreemap && options.TreemapDrillOnItems && g.Children.Count > 0;
                return new GroupNode(shapes[g.Group])
                {
                    Key = "series:" + g.Name,
                    Class = drill ? "rio-series rio-treemap-branch rio-treemap-drill" : "rio-series rio-treemap-branch",
                    Interaction = drill ? new NodeInteraction("treemap", g.PathText(separator)) : null,
                    AriaLabel = drill ? texts.Fill(nameof(ChartTexts.TreemapShow), culture, summary) : summary,
                };
            })])
            {
                Class = "rio-series-treemap",
                AriaLabel = series.Spec.DisplayName,
            },
        };
        var labelNodes = focus.Children.Select(g => (Node)new GroupNode(groupTexts[g.Group]) { Key = "labels:" + g.Name }).ToList();
        if (selected.Count > 0)
        {
            seriesNodes.Add(new GroupNode(selected) { Key = "selection:points", Class = "rio-selected-points" });
        }

        // ---------- Tooltip: items first, then groups (found in their headers and gaps) ----------
        var tips = leaves.Concat(parents).ToList();
        var tooltipSeries = new List<TooltipSeries>();
        if (hasData && tips.Count > 0)
        {
            const string arrow = " › ";
            tooltipSeries.Add(new TooltipSeries(series.Spec.Id, series.Spec.DisplayName, series.Color, SeriesType.Treemap, 0, 0, 0,
                [.. tips.Select(n => n.Rect.CenterX)], [.. tips.Select(n => n.Rect.CenterY)], [.. tips.Select(n => n.Index)],
                [.. tips.Select(n => n.PathText(arrow))], [.. tips.Select(n => Format(n.Value))])
            {
                CellWidths = [.. tips.Select(n => n.Rect.Width)],
                CellHeights = [.. tips.Select(n => n.Rect.Height)],
                PointColors = [.. tips.Select(NodeColor)],
                PartLabels = [texts.ShareOfGroup, texts.ShareOfTotal],
                PartValues = [.. tips.Select(n => new[]
                {
                    n.Parent is { Parent: not null } p && p.Value > 0 ? Percent(n.Value / p.Value, culture) : "",
                    Percent(n.Value / total, culture),
                })],
            });
        }

        var overlay = new List<Node>();
        if (!hasData)
        {
            var allHidden = groups.Count > 0 && groups.All(g => Hidden(g.Name));
            overlay.Add(new TextNode(allHidden ? texts.AllSlicesHidden : EmptyMessage(resolved, texts), new PointF(plot.CenterX, plot.CenterY),
                new TextStyle("empty"), Anchor.Middle) { Class = "rio-empty" });
        }

        // ---------- Description: the top-level groups and their shares ----------
        var shown = focus.Children;
        var kind = drilled ? $"{texts.Treemap}, {focus.PathText(" › ")}" : texts.Treemap;
        var items = string.Join(", ", shown.Take(8).Select(g => $"{g.Name} {Percent(g.Value / Math.Max(total, double.Epsilon), culture)}"))
            + (shown.Count > 8 ? " " + texts.Fill(nameof(ChartTexts.More), culture, shown.Count - 8) : "");
        var description = (string.IsNullOrWhiteSpace(spec.Title) ? "" : spec.Title + ". ")
            + (hasData
                ? texts.FillCount(nameof(ChartTexts.PolarItems), shown.Count, culture, kind, shown.Count, items) + "."
                : texts.Fill(nameof(ChartTexts.PolarNoData), culture, texts.Treemap))
            + (leftOut > 0 ? " " + texts.FillCount(nameof(ChartTexts.TreemapLeftOut), leftOut, culture, leftOut) + "." : "");

        var scene = new Scene.Scene(
            new GroupNode(statics) { Class = "rio-static" },
            new GroupNode([new GroupNode(seriesNodes) { Class = "rio-plot" }, new GroupNode(labelNodes) { Class = "rio-labels" }]) { Class = "rio-series-layer rio-treemap" },
            new GroupNode(overlay) { Class = "rio-overlay" })
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
            TreemapPath = drilled ? focus.PathText(separator) : null,
        };
    }

    /// <summary>The group at a path ("Asia/India"), or null when there is none (or it has no children).</summary>
    private static TreeNode? FindGroup(TreeNode root, string? path, string separator)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var node = root;
        foreach (var part in path.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!node.ByName.TryGetValue(part, out var child) || !node.Children.Contains(child))
            {
                return null; // hidden, left out or not there
            }

            node = child;
        }

        return node.Children.Count > 0 && node != root ? node : null;
    }

    /// <summary>
    /// The path bar of a drilled treemap: "All › Asia › India", each step but the last a button that
    /// shows that group (Interaction "treemap" with its path; "" for all). Returns its height.
    /// </summary>
    private static double PathBar(TreeNode focus, string separator, ChartTexts texts, CultureInfo culture, ITextMeasurer measure, double font, double y0,
        bool interactive, StyleTable styles, List<Node> statics)
    {
        styles.Add("treemap-crumb", new TextStyleDef { FontSize = (float)font, Fill = ThemeTokens.TextMuted });
        styles.Add("treemap-crumb-current", new TextStyleDef { FontSize = (float)font, FontWeight = 600, Fill = ThemeTokens.Text });
        var height = font * 1.3 + 8;
        var cy = (float)(y0 + height / 2 - 2);
        var steps = new List<(string Label, string Path)> { (texts.TreemapAll, "") };
        steps.AddRange(focus.Ancestors().Reverse().Select(a => (a.Name, a.PathText(separator))));
        steps.Add((focus.Name, focus.PathText(separator)));
        var x = PadLeft;
        var nodes = new List<Node>();
        for (var i = 0; i < steps.Count; i++)
        {
            var (label, path) = steps[i];
            var last = i == steps.Count - 1;
            var w = measure.Measure(label, font);
            var text = new TextNode(label, new PointF((float)x, cy), new TextStyle(last ? "treemap-crumb-current" : "treemap-crumb"), Anchor.Start);
            if (last || !interactive)
            {
                nodes.Add(text);
            }
            else
            {
                nodes.Add(new GroupNode(
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
                    Interaction = new NodeInteraction("treemap", path),
                    AriaLabel = texts.Fill(nameof(ChartTexts.TreemapShow), culture, label),
                });
            }

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

    /// <summary>A shortened name is shown only with at least three characters before the "…".</summary>
    private static bool Readable(string shown, string full) => shown == full ? shown.Length > 0 : shown.Length >= 4;

    /// <summary>The tree from the series' paths. Zero and negative values are left out (counted); gaps are skipped.</summary>
    private static TreeNode BuildTree(Resolved series, CultureInfo culture, List<string> warnings, out int leftOut)
    {
        var root = new TreeNode("", null);
        var separator = string.IsNullOrEmpty(series.Spec.PathSeparator) ? "/" : series.Spec.PathSeparator;
        var labels = SliceLabels(series.Data, culture);
        var (duplicates, parentValues) = (0, 0);
        leftOut = 0;
        for (var i = 0; i < labels.Length; i++)
        {
            var v = series.Data.Y[i];
            if (!double.IsFinite(v))
            {
                continue;
            }

            if (v <= 0)
            {
                leftOut++;
                continue;
            }

            var parts = labels[i].Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                parts = [labels[i]];
            }

            var node = root;
            foreach (var part in parts)
            {
                if (!node.ByName.TryGetValue(part, out var child))
                {
                    child = new TreeNode(part, node);
                    node.ByName[part] = child;
                    node.Children.Add(child);
                }

                node = child;
            }

            if (node.HadValue)
            {
                duplicates++;
            }
            else
            {
                node.Index = i;
            }

            node.Value += v;
            node.HadValue = true;
        }

        foreach (var node in Walk(root).Where(n => n.HadValue && n.Children.Count > 0))
        {
            parentValues++;
            node.Index = -1; // a group's value is the sum of its items
        }

        if (leftOut > 0)
        {
            warnings.Add($"series '{series.Spec.Id}': {leftOut} zero or negative value(s) cannot be shown in a treemap and are left out.");
        }

        if (duplicates > 0)
        {
            warnings.Add($"series '{series.Spec.Id}': {duplicates} path(s) appear more than once; their values are added.");
        }

        if (parentValues > 0)
        {
            warnings.Add($"series '{series.Spec.Id}': {parentValues} group(s) also have a value of their own; a group's value is the sum of its items, so it is ignored.");
        }

        return root;
    }

    /// <summary>The nodes drawn: like <see cref="Walk"/>, but not inside collapsed groups.</summary>
    private static IEnumerable<TreeNode> WalkShown(TreeNode node)
    {
        yield return node;
        if (node.Collapsed)
        {
            yield break;
        }

        foreach (var child in node.Children)
        {
            foreach (var n in WalkShown(child))
            {
                yield return n;
            }
        }
    }

    private static IEnumerable<TreeNode> Walk(TreeNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var n in Walk(child))
            {
                yield return n;
            }
        }
    }

    private static void SetGroup(TreeNode node, int group)
    {
        foreach (var n in Walk(node))
        {
            n.Group = group;
        }
    }

    private static int MaxDepth(TreeNode node) => node.Children.Count == 0 ? 0 : 1 + node.Children.Max(MaxDepth);

    /// <summary>Group values from their items, largest first; empty groups are removed.</summary>
    private static double Sum(TreeNode node)
    {
        if (node.Children.Count > 0)
        {
            foreach (var child in node.Children)
            {
                Sum(child);
            }

            node.Children.RemoveAll(c => !(c.Value > 0));
            node.Children.Sort((a, b) => b.Value.CompareTo(a.Value));
            node.Value = node.Children.Sum(c => c.Value);
        }

        return node.Value;
    }

    /// <summary>Per group: items under the threshold share of the group (two or more) become one "Other" item.</summary>
    private static void GroupSmall(TreeNode node, double threshold, string other)
    {
        foreach (var child in node.Children)
        {
            GroupSmall(child, threshold, other);
        }

        var total = node.Children.Sum(c => c.Children.Count > 0 ? Walk(c).Where(n => n.Children.Count == 0).Sum(n => n.Value) : c.Value);
        var small = node.Children.Where(c => c.Children.Count == 0 && total > 0 && c.Value / total < threshold).ToList();
        if (small.Count < 2)
        {
            return;
        }

        node.Children.RemoveAll(small.Contains);
        node.Children.Add(new TreeNode(other, node) { IsOther = true, Value = small.Sum(c => c.Value), Group = small[0].Group });
    }

    /// <summary>
    /// Gives each node its rectangle: children share their group's rectangle (minus a header with
    /// the group's name when there is room) with the squarified layout.
    /// </summary>
    /// <param name="depth">Levels below <paramref name="top"/> (its children are level 1).</param>
    /// <param name="levels">How many levels to draw below <paramref name="top"/>; groups at the last one are collapsed into cells.</param>
    private static void Place(TreeNode node, RectF rect, double headerHeight, TreeNode top, int depth, int levels)
    {
        node.Rect = rect;
        node.Collapsed = node != top && node.Children.Count > 0 && depth >= levels;
        if (node.Children.Count == 0 || node.Collapsed)
        {
            return;
        }

        var inner = rect;
        if (node != top)
        {
            const float pad = 2;
            node.HasHeader = rect.Height >= headerHeight * 2.4 && rect.Width >= 48;
            var headerTop = node.HasHeader ? (float)headerHeight : pad;
            inner = new RectF(rect.X + pad, rect.Y + headerTop, Math.Max(0, rect.Width - 2 * pad), Math.Max(0, rect.Height - headerTop - pad));
        }

        var rects = Squarify([.. node.Children.Select(c => c.Value)], inner);
        for (var i = 0; i < node.Children.Count; i++)
        {
            Place(node.Children[i], rects[i], headerHeight, top, depth + 1, levels);
        }
    }

    /// <summary>
    /// The squarified treemap layout (Bruls, Huizing and van Wijk): values (largest first) fill the
    /// rectangle in rows along its shorter side, a row growing while its worst aspect ratio improves.
    /// </summary>
    internal static RectF[] Squarify(IReadOnlyList<double> values, RectF rect)
    {
        var result = new RectF[values.Count];
        var sum = values.Sum();
        if (values.Count == 0 || !(sum > 0) || rect.Width <= 0 || rect.Height <= 0)
        {
            Array.Fill(result, new RectF(rect.X, rect.Y, 0, 0));
            return result;
        }

        var scale = rect.Width * (double)rect.Height / sum;
        double x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height;
        var row = new List<int>();
        double rowSum = 0;

        double Worst(double total, double min, double max, double side)
        {
            var s2 = side * side;
            var t2 = total * total;
            return Math.Max(s2 * max / t2, t2 / (s2 * min));
        }

        void LayoutRow()
        {
            if (row.Count == 0)
            {
                return;
            }

            if (w >= h)
            {
                // A column on the left.
                var cw = h > 0 ? rowSum / h : 0;
                var cy = y;
                foreach (var i in row)
                {
                    var ch = cw > 0 ? values[i] * scale / cw : 0;
                    result[i] = new RectF((float)x, (float)cy, (float)cw, (float)ch);
                    cy += ch;
                }

                x += cw;
                w = Math.Max(0, w - cw);
            }
            else
            {
                // A row along the top.
                var rh = w > 0 ? rowSum / w : 0;
                var cx = x;
                foreach (var i in row)
                {
                    var cw = rh > 0 ? values[i] * scale / rh : 0;
                    result[i] = new RectF((float)cx, (float)y, (float)cw, (float)rh);
                    cx += cw;
                }

                y += rh;
                h = Math.Max(0, h - rh);
            }

            row.Clear();
            rowSum = 0;
        }

        double rowMin = double.PositiveInfinity, rowMax = 0;
        for (var i = 0; i < values.Count;)
        {
            var area = Math.Max(0, values[i]) * scale;
            var side = Math.Min(w, h);
            if (row.Count == 0 || side <= 0
                || Worst(rowSum + area, Math.Min(rowMin, area), Math.Max(rowMax, area), side) <= Worst(rowSum, rowMin, rowMax, side))
            {
                row.Add(i);
                rowSum += area;
                (rowMin, rowMax) = (Math.Min(rowMin, area), Math.Max(rowMax, area));
                i++;
            }
            else
            {
                LayoutRow();
                (rowMin, rowMax) = (double.PositiveInfinity, 0);
            }
        }

        LayoutRow();
        return result;
    }
}
