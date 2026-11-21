using System.Globalization;
using System.Text;
using System.Text.Json;
using RioCharts.Core.Data;

namespace RioCharts.Core.Specs;

/// <summary>A spec bound to data: concrete series (types resolved, groups expanded) and their columns.</summary>
public sealed record SpecBindResult(ChartSpec Spec, IReadOnlyDictionary<string, SeriesData> Data, IReadOnlyList<SpecError> Errors, IReadOnlyList<string> Warnings)
{
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Binds series that name dataset fields (<see cref="SeriesSpec.X"/>, <see cref="SeriesSpec.Y"/>)
/// to data: checks the fields (errors list what is available), applies filter, time bucket,
/// aggregate, groupBy, sort and limit, and resolves <see cref="SeriesType.Auto"/>.
/// </summary>
public static class ChartSpecBinder
{
    private const string InlineDataset = "inline";

    /// <summary>Loads the datasets the spec refers to from the source, then binds.</summary>
    public static async Task<SpecBindResult> BindAsync(ChartSpec spec, IChartDataSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(source);
        var names = spec.Series.Select(s => s.Dataset ?? spec.Dataset).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase);
        var loaded = new Dictionary<string, ChartDataset>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (await source.LoadAsync(name, cancellationToken).ConfigureAwait(false) is { } dataset)
            {
                loaded[name] = dataset;
            }
        }

        return Bind(spec, loaded, source.Describe().Select(d => d.Name).ToList());
    }

    /// <summary>Binds against datasets already in memory (by name, case-insensitive).</summary>
    public static SpecBindResult Bind(ChartSpec spec, IReadOnlyDictionary<string, ChartDataset> datasets, IReadOnlyList<string>? available = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var errors = new List<SpecError>();
        var warnings = new List<string>();
        var lookup = new Dictionary<string, ChartDataset>(datasets, StringComparer.OrdinalIgnoreCase);
        available ??= [.. lookup.Keys];
        if (spec.Data is { Count: > 0 } rows)
        {
            if (rows.Count > ChartSpecJson.MaxInlineRows)
            {
                errors.Add(new SpecError("data", $"at most {ChartSpecJson.MaxInlineRows} inline rows (got {rows.Count}); refer to a dataset id instead."));
            }

            lookup[InlineDataset] = ChartDataset.FromRows(InlineDataset, rows);
        }

        var series = new List<SeriesSpec>();
        var data = new Dictionary<string, SeriesData>(StringComparer.Ordinal);
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < spec.Series.Count; i++)
        {
            BindSeries(spec, spec.Series[i], $"series[{i}]", lookup, available, series, data, usedIds, errors, warnings);
        }

        if (spec.Series.Count == 0)
        {
            errors.Add(new SpecError("series", "add at least one series, e.g. {\"type\":\"line\",\"x\":\"time\",\"y\":\"value\"}."));
        }
        else if (series.Count > ChartSpecJson.MaxSeries)
        {
            errors.Add(new SpecError("series", $"the spec makes {series.Count} series; at most {ChartSpecJson.MaxSeries}. Use groupBy on a field with fewer values, or a filter."));
        }

        var bound = spec with { Series = series };
        return new SpecBindResult(bound, data, errors, warnings);
    }

    private readonly record struct Point(object X, double? Y);

    private static void BindSeries(ChartSpec chart, SeriesSpec s, string path, Dictionary<string, ChartDataset> datasets, IReadOnlyList<string> available,
        List<SeriesSpec> output, Dictionary<string, SeriesData> data, HashSet<string> usedIds, List<SpecError> errors, List<string> warnings)
    {
        var errorsBefore = errors.Count;
        var datasetName = s.Dataset ?? chart.Dataset ?? (datasets.ContainsKey(InlineDataset) ? InlineDataset : null);
        if (datasetName is null)
        {
            errors.Add(new SpecError($"{path}.dataset",
                available.Count > 0
                    ? $"no data: set \"dataset\" on the chart or the series (available: {string.Join(", ", available)}) or give inline \"data\"."
                    : "no data: give inline \"data\" rows or a dataset id."));
            return;
        }

        if (!datasets.TryGetValue(datasetName, out var dataset))
        {
            errors.Add(new SpecError(s.Dataset is null ? "dataset" : $"{path}.dataset",
                $"dataset '{datasetName}' not found; available: {(available.Count > 0 ? string.Join(", ", available) : "none")}."));
            return;
        }

        // ---------- Fields ----------
        var count = s.Aggregate == Aggregate.Count;
        var x = s.X is null ? InferX(dataset) : Find(dataset, s.X, $"{path}.x", null, errors);
        if (s.X is null)
        {
            if (x is null)
            {
                errors.Add(new SpecError($"{path}.x", $"set the x field; available: {Describe(dataset.Fields)}."));
            }
            else
            {
                warnings.Add($"{path}.x: not set; using '{x.Name}'.");
            }
        }

        DataField? y = null;
        if (s.Y is not null)
        {
            y = Find(dataset, s.Y, $"{path}.y", FieldType.Number, errors);
        }
        else if (!count)
        {
            var numbers = dataset.Fields.Where(f => f.Type == FieldType.Number && f != x).ToList();
            if (numbers.Count == 1)
            {
                y = numbers[0];
                warnings.Add($"{path}.y: not set; using '{y.Name}'.");
            }
            else
            {
                errors.Add(new SpecError($"{path}.y", numbers.Count == 0
                    ? $"dataset '{dataset.Name}' has no numeric field; use aggregate \"count\" to count rows."
                    : $"set the y field; numeric fields: {string.Join(", ", numbers.Select(f => f.Name))}."));
            }
        }

        var groupBy = s.GroupBy is null ? null : Find(dataset, s.GroupBy, $"{path}.groupBy", null, errors);
        var filters = new List<(FilterSpec Spec, DataField Field)>();
        for (var f = 0; f < (s.Filter?.Count ?? 0); f++)
        {
            var filter = s.Filter![f];
            if (Find(dataset, filter.Field, $"{path}.filter[{f}].field", null, errors) is { } field)
            {
                filters.Add((filter, field));
            }
        }

        if (s.TimeBucket is not null && x is not null && x.Type != FieldType.Time)
        {
            errors.Add(new SpecError($"{path}.timeBucket", $"needs a time x field; '{x.Name}' is {x.Type.ToString().ToLowerInvariant()}."));
        }

        if (x is null || (y is null && !count) || errors.Count > errorsBefore)
        {
            return;
        }

        // ---------- Rows ----------
        var rows = Enumerable.Range(0, dataset.RowCount).Where(r => filters.All(f => Matches(f.Field.Values[r], f.Spec))).ToList();
        if (filters.Count > 0 && rows.Count == 0)
        {
            warnings.Add($"{path}.filter: no rows match.");
        }

        var groups = groupBy is null
            ? [(Key: (string?)null, Rows: rows)]
            : rows.GroupBy(r => Text(groupBy.Values[r]) ?? "(none)").Select(g => (Key: (string?)g.Key, Rows: g.ToList())).ToList();
        if (groups.Count > ChartSpecJson.MaxSeries)
        {
            errors.Add(new SpecError($"{path}.groupBy", $"'{groupBy!.Name}' has {groups.Count} values; at most {ChartSpecJson.MaxSeries} series. Filter it first."));
            return;
        }

        var aggregate = s.Aggregate;
        if (aggregate is null && s.TimeBucket is not null)
        {
            aggregate = Aggregate.Average;
            warnings.Add($"{path}.aggregate: not set with timeBucket; using average.");
        }
        else if (aggregate is null && x.Type == FieldType.Text && groups.Any(g => g.Rows.Select(r => Text(x.Values[r])).Distinct().Count() < g.Rows.Count))
        {
            aggregate = Aggregate.Sum;
            warnings.Add($"{path}.aggregate: '{x.Name}' repeats; using sum per value (set aggregate to change).");
        }

        foreach (var (key, groupRows) in groups)
        {
            var points = Points(groupRows, x, y, s.TimeBucket, aggregate);
            if (s.Sort is { } sort)
            {
                points = sort == SortOrder.Ascending
                    ? [.. points.OrderBy(p => p.Y ?? double.PositiveInfinity)]
                    : [.. points.OrderByDescending(p => p.Y ?? double.NegativeInfinity)];
            }

            if (s.Limit is > 0 and var limit && points.Count > limit)
            {
                points = points.Take(limit).ToList();
            }

            var type = s.Type == SeriesType.Auto ? Recommend(x, points, groups.Count, s.TimeBucket) : s.Type;
            var id = UniqueId(string.IsNullOrEmpty(s.Id) ? Slug(y?.Name ?? "count") : s.Id, key, usedIds);
            var name = key is null ? s.Name ?? Humanize(y?.Name ?? "count") : s.Name is null ? key : $"{s.Name}: {key}";
            var bound = s with { Id = id, Name = name, Type = type };
            if (s.Type == SeriesType.Auto && type == SeriesType.Pie)
            {
                bound = bound with { InnerRadius = bound.InnerRadius ?? 0.6 };
                warnings.Add($"{path}.type: auto chose a donut (the values are shares of a whole).");
            }

            output.Add(bound);
            data[id] = SeriesData.FromItems(points, p => p.X, p => p.Y);
        }
    }

    private static List<Point> Points(List<int> rows, DataField x, DataField? y, TimeBucket? bucket, Aggregate? aggregate)
    {
        object? XAt(int r)
        {
            var v = x.Values[r];
            return bucket is { } b && v is not null ? Floor(v, b) : v;
        }

        double? YAt(int r) => y?.Values[r] is double d ? d : null;

        if (aggregate is null)
        {
            return rows.Where(r => x.Values[r] is not null).Select(r => new Point(XAt(r)!, YAt(r))).ToList();
        }

        // Group by x (or its bucket), keeping first-appearance order (categories) — times and numbers are sorted later anyway.
        var groups = new Dictionary<object, List<int>>();
        var order = new List<object>();
        foreach (var r in rows)
        {
            if (XAt(r) is not { } key)
            {
                continue;
            }

            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
                order.Add(key);
            }

            list.Add(r);
        }

        return order.Select(key => new Point(key, Aggregation.Apply(aggregate.Value, groups[key].Select(YAt).ToList()))).ToList();
    }

    /// <summary>The start of the bucket a time falls in (wall-clock time; weeks start on Monday).</summary>
    private static object Floor(object value, TimeBucket bucket)
    {
        var t = value switch { DateTimeOffset o => o.DateTime, DateTime d => d, _ => default };
        var floored = bucket switch
        {
            TimeBucket.Minute => new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0),
            TimeBucket.Hour => new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0),
            TimeBucket.Day => t.Date,
            TimeBucket.Week => t.Date.AddDays(-(((int)t.DayOfWeek + 6) % 7)),
            TimeBucket.Month => new DateTime(t.Year, t.Month, 1),
            TimeBucket.Quarter => new DateTime(t.Year, (t.Month - 1) / 3 * 3 + 1, 1),
            _ => new DateTime(t.Year, 1, 1),
        };
        return value is DateTimeOffset offset ? new DateTimeOffset(floored, offset.Offset) : floored;
    }

    /// <summary>
    /// The chart type for "auto": times and numbers give a line (bars for time buckets), categories
    /// give bars, and a single series of up to six positive values that add up to 100 (or 1) gives a donut.
    /// </summary>
    private static SeriesType Recommend(DataField x, List<Point> points, int seriesCount, TimeBucket? bucket)
    {
        if (x.Type != FieldType.Text)
        {
            return bucket is null ? SeriesType.Line : SeriesType.Bar;
        }

        var values = points.Select(p => p.Y).OfType<double>().ToList();
        var sum = values.Sum();
        var shares = seriesCount == 1 && values.Count is >= 2 and <= 6 && values.All(v => v > 0) && (Math.Abs(sum - 100) <= 1 || Math.Abs(sum - 1) <= 0.01);
        return shares ? SeriesType.Pie : SeriesType.Bar;
    }

    // ---------- Fields ----------

    private static DataField? Find(ChartDataset dataset, string name, string path, FieldType? required, List<SpecError> errors)
    {
        var field = dataset.Field(name) ?? dataset.Field(name.Replace(" ", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal));
        if (field is null)
        {
            var close = dataset.Fields.FirstOrDefault(f => Distance(f.Name.ToLowerInvariant(), name.ToLowerInvariant()) <= 2);
            errors.Add(new SpecError(path, $"field '{name}' not found in '{dataset.Name}'"
                + (close is not null ? $" (did you mean '{close.Name}'?)" : "") + $"; available: {Describe(dataset.Fields)}."));
            return null;
        }

        if (required is { } type && field.Type != type)
        {
            var kind = type.ToString().ToLowerInvariant();
            var matching = string.Join(", ", dataset.Fields.Where(f => f.Type == type).Select(f => f.Name));
            errors.Add(new SpecError(path, $"field '{field.Name}' is {field.Type.ToString().ToLowerInvariant()}, not {kind}; "
                + $"{kind} fields: {(matching.Length > 0 ? matching : "none")}."));
            return null;
        }

        return field;
    }

    private static DataField? InferX(ChartDataset dataset) =>
        dataset.Fields.FirstOrDefault(f => f.Type == FieldType.Time) ?? dataset.Fields.FirstOrDefault(f => f.Type == FieldType.Text);

    private static string Describe(IEnumerable<DataField> fields) =>
        string.Join(", ", fields.Select(f => $"{f.Name} ({f.Type.ToString().ToLowerInvariant()})"));

    private static bool Matches(object? value, FilterSpec filter)
    {
        var target = filter.Value is JsonElement json ? json.ValueKind switch
        {
            JsonValueKind.Number => (object)json.GetDouble(),
            JsonValueKind.String => json.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        } : filter.Value;

        if (filter.Op == FilterOp.Contains)
        {
            return Text(value) is { } text && Text(target) is { } part && text.Contains(part, StringComparison.OrdinalIgnoreCase);
        }

        var comparison = Compare(value, target);
        return filter.Op switch
        {
            FilterOp.Eq => comparison == 0,
            FilterOp.Ne => comparison != 0,
            FilterOp.Gt => comparison > 0,
            FilterOp.Gte => comparison >= 0,
            FilterOp.Lt => comparison < 0,
            _ => comparison <= 0,
        };
    }

    /// <summary>Numbers and times compare by value, text case-insensitively; null when they cannot be compared.</summary>
    private static int? Compare(object? value, object? target)
    {
        if (value is null || target is null)
        {
            return value is null && target is null ? 0 : null;
        }

        if (value is double d)
        {
            return AxisValue.ToDouble(target, XKind.Numeric) is var t && double.IsFinite(t) ? d.CompareTo(t) : null;
        }

        if (value is DateTime or DateTimeOffset)
        {
            var a = AxisValue.ToDouble(value, XKind.Time);
            var b = AxisValue.ToDouble(target, XKind.Time);
            return double.IsFinite(b) ? a.CompareTo(b) : null;
        }

        return string.Compare(Text(value), Text(target), StringComparison.OrdinalIgnoreCase);
    }

    private static string? Text(object? value) => value switch
    {
        null => null,
        double d => d.ToString(CultureInfo.InvariantCulture),
        DateTime t => t.ToString("s", CultureInfo.InvariantCulture),
        DateTimeOffset o => o.ToString("o", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }
        }

        return d[a.Length, b.Length];
    }

    // ---------- Names ----------

    private static string UniqueId(string baseId, string? group, HashSet<string> used)
    {
        var id = group is null ? baseId : $"{baseId}-{Slug(group)}";
        var candidate = id;
        for (var n = 2; !used.Add(candidate); n++)
        {
            candidate = $"{id}-{n}";
        }

        return candidate;
    }

    private static string Slug(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        }

        return sb.ToString().Trim('-') is { Length: > 0 } slug ? slug : "series";
    }

    /// <summary>"responseMs" or "response_ms" → "Response ms".</summary>
    public static string Humanize(string field)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < field.Length; i++)
        {
            var c = field[i];
            if (c is '_' or '-')
            {
                sb.Append(' ');
            }
            else if (char.IsUpper(c) && i > 0 && char.IsLower(field[i - 1]))
            {
                sb.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
            }
        }

        return sb.ToString();
    }
}
