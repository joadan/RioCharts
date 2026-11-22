using System.Globalization;
using System.Text;
using System.Text.Json;
using RioCharts.Core;

namespace RioCharts.AI;

/// <summary>
/// Turns a spec (e.g. one an agent built) into Razor component markup, so a developer can move
/// the chart into the codebase. Fields become properties of an item type (camelCase → PascalCase);
/// transforms that components do not do (filter, timeBucket, groupBy...) become comments to
/// implement in the query; an aggregate per x value becomes the series' <c>Aggregate</c>.
/// </summary>
public static class ChartRazor
{
    /// <param name="itemType">The C# type of the rows, e.g. "Reading".</param>
    /// <param name="itemsName">The expression that holds the rows, e.g. "readings".</param>
    public static string FromSpec(ChartSpec spec, string itemType = "Row", string itemsName = "rows")
    {
        ArgumentNullException.ThrowIfNull(spec);
        var sb = new StringBuilder();
        sb.Append($"<RioChart TItem=\"{itemType}\" Items=\"{itemsName}\"");
        if (spec.Title is { } title)
        {
            sb.Append($" Title=\"{Attr(title)}\"");
        }

        if (spec.Y.Title is { } yTitle)
        {
            sb.Append($" YAxis=\"@(new AxisSpec {{ Title = {Str(yTitle)} }})\"");
        }

        if (spec.X.Title is { } xTitle)
        {
            sb.Append($" XAxis=\"@(new AxisSpec {{ Title = {Str(xTitle)} }})\"");
        }

        if (spec.YAxes.Count > 0)
        {
            sb.Append(" YAxes=\"@([").Append(string.Join(", ", spec.YAxes.Select(a =>
                $"new AxisSpec {{ Id = {Str(a.Id ?? "")}{(a.Title is null ? "" : $", Title = {Str(a.Title)}")} }}"))).Append("])\"");
        }

        if (spec.TimeZone is { } zone)
        {
            sb.Append($" TimeZone=\"{Attr(zone)}\"");
        }

        if (spec.Horizontal)
        {
            sb.Append(" Horizontal=\"true\"");
        }

        if (!spec.AutoCompact)
        {
            sb.Append(" AutoCompact=\"false\"");
        }

        if (spec.Compact)
        {
            sb.Append(" Compact=\"true\" Height=\"32\"");
        }

        sb.AppendLine(">");
        foreach (var a in spec.Annotations)
        {
            sb.Append("    ").AppendLine(Annotation(a));
        }

        foreach (var s in spec.Series)
        {
            var notes = new List<string>();
            if (s.Dataset is { } dataset)
            {
                notes.Add($"dataset: {dataset}");
            }

            if (s.Filter is { Count: > 0 } filter)
            {
                notes.Add("filter: " + string.Join(" and ", filter.Select(f => $"{f.Field} {f.Op.ToString().ToLowerInvariant()} {Value(f.Value)}")));
            }

            var candleBucket = s.Type == SeriesType.Candlestick && s.Open is null ? s.TimeBucket ?? TimeBucket.Day : (TimeBucket?)null;
            if (s.TimeBucket is { } bucket && candleBucket is null)
            {
                notes.Add($"per {bucket.ToString().ToLowerInvariant()}");
            }

            // Components aggregate per x value; per time bucket stays a note.
            var componentAggregate = s.TimeBucket is null ? s.Aggregate : null;
            if (s.Aggregate is { } aggregate && componentAggregate is null)
            {
                notes.Add($"aggregate: {aggregate.ToString().ToLowerInvariant()}");
            }

            if (s.GroupBy is { } groupBy)
            {
                notes.Add($"one series per {groupBy} (e.g. a @foreach over the groups)");
            }

            if (s.Sort is { } sort)
            {
                notes.Add($"sorted {sort.ToString().ToLowerInvariant()}{(s.Limit is { } l ? $", top {l}" : "")}");
            }

            if (notes.Count > 0)
            {
                sb.Append("    @* ").Append(string.Join("; ", notes)).AppendLine(" (do this in the query) *@");
            }

            var component = s.Type switch
            {
                SeriesType.Area => "AreaSeries",
                SeriesType.Bar => "BarSeries",
                SeriesType.Pie when s.InnerRadius is > 0 => "DonutSeries",
                SeriesType.Pie => "PieSeries",
                SeriesType.RadialBar => "RadialBarSeries",
                SeriesType.Radar => "RadarSeries",
                SeriesType.Treemap => "TreemapSeries",
                SeriesType.Scatter => "ScatterSeries",
                SeriesType.Heatmap => "HeatmapSeries",
                SeriesType.RangeArea => "RangeAreaSeries",
                SeriesType.BoxPlot => "BoxPlotSeries",
                SeriesType.Candlestick => "CandlestickSeries",
                _ => "LineSeries",
            };

            sb.Append($"    <{component}");
            if (s.Name is { } name)
            {
                sb.Append($" Name=\"{Attr(name)}\"");
            }

            if (s.Type == SeriesType.Treemap && s.Path is { Count: > 0 } pathFields)
            {
                // The groups' fields joined into the item's path.
                var separator = s.PathSeparator ?? "/";
                sb.Append($" XValue=\"r => $\"{string.Join(Attr(separator), pathFields.Select(f => "{r." + Property(f) + "}"))}\"\"");
                if (s.PathSeparator is { } custom)
                {
                    sb.Append($" PathSeparator=\"{Attr(custom)}\"");
                }
            }
            else
            {
                sb.Append($" XValue=\"r => r.{Property(s.X ?? "x")}\"");
            }

            if (s.Type == SeriesType.Treemap && s.VisibleLevels is { } levels)
            {
                sb.Append($" VisibleLevels=\"{levels}\"");
            }
            if (s.Type == SeriesType.RangeArea && s.Low is { } lowField)
            {
                sb.Append($" LowValue=\"r => r.{Property(lowField)}\"");
            }

            if (candleBucket is { } perBucket)
            {
                sb.Append($" Bucket=\"TimeBucket.{perBucket}\""); // raw readings: the component makes the candles
            }
            else if (s.Type == SeriesType.Candlestick && s.Open is { } openField)
            {
                sb.Append($" OpenValue=\"r => r.{Property(openField)}\"");
                if (s.High is { } highField)
                {
                    sb.Append($" HighValue=\"r => r.{Property(highField)}\"");
                }

                if (s.Low is { } lowCandle)
                {
                    sb.Append($" LowValue=\"r => r.{Property(lowCandle)}\"");
                }
            }

            if (s.Type == SeriesType.BoxPlot && s.Q1 is { } q1Field && s.Q3 is { } q3Field)
            {
                // Precomputed boxes; without them the component computes the boxes from YValue per x.
                sb.Append($" Q1Value=\"r => r.{Property(q1Field)}\" Q3Value=\"r => r.{Property(q3Field)}\"");
                if (s.Low is { } boxLow)
                {
                    sb.Append($" LowValue=\"r => r.{Property(boxLow)}\"");
                }

                if (s.High is { } boxHigh)
                {
                    sb.Append($" HighValue=\"r => r.{Property(boxHigh)}\"");
                }
            }

            if (componentAggregate != Aggregate.Count || s.Y is not null)
            {
                sb.Append($" YValue=\"r => r.{Property(s.Y ?? "value")}\"");
            }

            if (componentAggregate is { } a)
            {
                sb.Append($" Aggregate=\"Aggregate.{a}\"");
            }
            if (s.Curve != CurveType.Linear)
            {
                sb.Append($" Curve=\"CurveType.{s.Curve}\"");
            }

            if (s.Type == SeriesType.Scatter)
            {
                if (s.MarkerSize is { } size && size != 5)
                {
                    sb.Append(" MarkerSize=\"").Append(size.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('"');
                }

                if (s.TrendLine)
                {
                    sb.Append(" TrendLine=\"true\"");
                }
            }

            Optional(sb, "Stack", s.Stack);
            Optional(sb, "YAxisId", s.YAxisId);
            Optional(sb, "Color", s.Color);
            Optional(sb, "Format", s.Format);
            if (s.DataLabels is { } labels)
            {
                sb.Append($" DataLabels=\"{(labels ? "true" : "false")}\"");
            }

            if (s.Highlight != PointHighlight.None && s.Type is SeriesType.Line or SeriesType.Area)
            {
                // Flags: "Last, Max" becomes PointHighlight.Last | PointHighlight.Max.
                var flags = s.Highlight.ToString().Split(", ").Select(f => $"PointHighlight.{f}");
                sb.Append($" Highlight=\"{string.Join(" | ", flags)}\"");
            }

            if (s.Type == SeriesType.Pie && s.InnerRadius is { } inner && inner is not 0 and not 0.6)
            {
                sb.Append($" InnerRadius=\"{Number(inner)}\"");
            }

            if (s.Max is { } max)
            {
                sb.Append($" Max=\"{Number(max)}\"");
            }

            sb.AppendLine(" />");
        }

        sb.Append("</RioChart>");
        return sb.ToString();
    }

    private static string Annotation(AnnotationSpec a)
    {
        var axis = a.Axis == AnnotationAxis.X ? " Axis=\"AnnotationAxis.X\"" : "";
        var label = a.Label is null ? "" : $" Label=\"{Attr(a.Label)}\"";
        return a.Type switch
        {
            AnnotationType.Band => $"<ReferenceBand{axis} From=\"{Value(a.From)}\" To=\"{Value(a.To)}\"{label} />",
            AnnotationType.Point => $"<PointAnnotation X=\"{Value(a.X)}\" Y=\"{Value(a.Y)}\"{label} />",
            AnnotationType.Callout => $"<Callout X=\"{Value(a.X)}\" Y=\"{Value(a.Y)}\"{label} />",
            _ => $"<ReferenceLine{axis} Value=\"{Value(a.Value)}\"{label} />",
        };
    }

    private static void Optional(StringBuilder sb, string attribute, string? value)
    {
        if (value is not null)
        {
            sb.Append($" {attribute}=\"{Attr(value)}\"");
        }
    }

    /// <summary>Numbers as literals; times and text as C# expressions.</summary>
    private static string Value(object? value) => value switch
    {
        null => "null",
        JsonElement { ValueKind: JsonValueKind.Number } j => Number(j.GetDouble()),
        JsonElement { ValueKind: JsonValueKind.String } j => TextValue(j.GetString() ?? ""),
        JsonElement j => Attr(j.GetRawText()),
        double or float or int or long or decimal => Number(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
        DateTime t => $"@(new DateTime({t.Year}, {t.Month}, {t.Day}, {t.Hour}, {t.Minute}, {t.Second}))",
        string s => TextValue(s),
        _ => Attr(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""),
    };

    private static string TextValue(string s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) && s.Contains('-', StringComparison.Ordinal)
            ? $"@(DateTime.Parse({Str(s)}))"
            : $"@({Str(s)})";

    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>"responseMs", "response_ms" → "ResponseMs".</summary>
    private static string Property(string field)
    {
        var sb = new StringBuilder();
        var upper = true;
        foreach (var c in field)
        {
            if (!char.IsLetterOrDigit(c))
            {
                upper = true;
                continue;
            }

            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return sb.Length > 0 && char.IsDigit(sb[0]) ? "_" + sb : sb.ToString();
    }

    private static string Attr(string value) => value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("@", "@@", StringComparison.Ordinal);

    private static string Str(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
