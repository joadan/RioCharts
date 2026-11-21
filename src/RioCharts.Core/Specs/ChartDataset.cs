using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RioCharts.Core.Specs;

/// <summary>The kind of values in a dataset field.</summary>
public enum FieldType
{
    Number,
    Time,
    Text,
}

/// <summary>A named column: its type and values (double, DateTime/DateTimeOffset or string; null for missing).</summary>
public sealed class DataField
{
    public DataField(string name, FieldType type, object?[] values, string? description = null)
    {
        Name = name;
        Type = type;
        Values = values;
        Description = description;
    }

    public string Name { get; }
    public FieldType Type { get; }
    public object?[] Values { get; }

    /// <summary>What the field means (shown to AI agents), e.g. "Revenue in €".</summary>
    public string? Description { get; }
}

/// <summary>
/// Tabular data that chart specs refer to by name and fields, so a spec (e.g. from an AI agent)
/// stays small and never has to carry the data itself.
/// </summary>
public sealed partial class ChartDataset
{
    private readonly Dictionary<string, DataField> _byName;

    public ChartDataset(string name, IReadOnlyList<DataField> fields, string? description = null)
    {
        Name = name;
        Fields = fields;
        Description = description;
        RowCount = fields.Count == 0 ? 0 : fields.Max(f => f.Values.Length);
        _byName = fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
    }

    public string Name { get; }
    public string? Description { get; }
    public IReadOnlyList<DataField> Fields { get; }
    public int RowCount { get; }

    /// <summary>The field with this name (case-insensitive), or null.</summary>
    public DataField? Field(string name) => _byName.GetValueOrDefault(name);

    /// <summary>Builds a dataset from rows of field values (numbers, times, text; JSON elements are read too).</summary>
    public static ChartDataset FromRows(string name, IEnumerable<IReadOnlyDictionary<string, object?>> rows, string? description = null,
        IReadOnlyDictionary<string, string>? fieldDescriptions = null)
    {
        var list = rows.ToList();
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in list.SelectMany(r => r.Keys))
        {
            if (seen.Add(key))
            {
                names.Add(key);
            }
        }

        var fields = names.Select(field =>
        {
            var raw = list.Select(r => r.TryGetValue(field, out var v) ? Normalize(v) : null).ToArray();
            return Column(field, raw, fieldDescriptions?.GetValueOrDefault(field));
        }).ToList();
        return new ChartDataset(name, fields, description);
    }

    /// <summary>Builds a dataset from objects: one field per public readable property (camelCase names).</summary>
    public static ChartDataset FromItems<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(string name, IEnumerable<T> items,
        string? description = null, IReadOnlyDictionary<string, string>? fieldDescriptions = null)
    {
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0).ToArray();
        var list = items.ToList();
        var fields = properties.Select(p =>
        {
            var fieldName = JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            var raw = list.Select(item => Normalize(p.GetValue(item))).ToArray();
            return Column(fieldName, raw, fieldDescriptions?.GetValueOrDefault(fieldName) ?? fieldDescriptions?.GetValueOrDefault(p.Name));
        }).ToList();
        return new ChartDataset(name, fields, description);
    }

    /// <summary>Builds a dataset from a JSON array of objects.</summary>
    public static ChartDataset FromJson(string name, JsonElement array, string? description = null)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Expected a JSON array of objects.", nameof(array));
        }

        var rows = array.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.Object)
            .Select(e => (IReadOnlyDictionary<string, object?>)e.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone(), StringComparer.Ordinal))
            .ToList();
        return FromRows(name, rows, description);
    }

    /// <summary>Numbers become double, times DateTime/DateTimeOffset, everything else a string.</summary>
    private static object? Normalize(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonElement json:
                return json.ValueKind switch
                {
                    JsonValueKind.Number => json.GetDouble(),
                    JsonValueKind.String => Normalize(json.GetString()),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => json.GetRawText(),
                };
            case DateTime or DateTimeOffset:
                return value;
            case DateOnly d:
                return d.ToDateTime(TimeOnly.MinValue);
            case string s:
                return ParseTime(s) ?? (object)s;
            case bool b:
                return b ? "true" : "false";
            case IConvertible c when value is not char:
                try
                {
                    return c.ToDouble(CultureInfo.InvariantCulture);
                }
                catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
                {
                    return value.ToString();
                }

            default:
                return value.ToString();
        }
    }

    /// <summary>ISO 8601 dates and times ("2026-03-01", "2026-03-01T12:00", with or without offset).</summary>
    private static object? ParseTime(string s)
    {
        if (!IsoPattern().IsMatch(s))
        {
            return null;
        }

        if (OffsetPattern().IsMatch(s) && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var o))
        {
            return o;
        }

        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static DataField Column(string name, object?[] values, string? description)
    {
        var first = values.FirstOrDefault(v => v is not null);
        var type = first switch
        {
            double => FieldType.Number,
            DateTime or DateTimeOffset => FieldType.Time,
            _ => FieldType.Text,
        };

        // A column must be one type: mixed values are read as text.
        if (type != FieldType.Text && values.Any(v => v is not null && TypeOf(v) != type))
        {
            type = FieldType.Text;
            values = values.Select(v => v is null ? null : (object)Convert.ToString(v, CultureInfo.InvariantCulture)!).ToArray();
        }

        return new DataField(name, type, values, description);

        static FieldType TypeOf(object v) => v switch { double => FieldType.Number, DateTime or DateTimeOffset => FieldType.Time, _ => FieldType.Text };
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?)?(Z|[+-]\d{2}:?\d{2})?$")]
    private static partial Regex IsoPattern();

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$")]
    private static partial Regex OffsetPattern();
}

/// <summary>
/// Where specs get their data: the app lists its datasets (so an agent knows what it may chart)
/// and loads one on demand. Querying and access control stay in the app.
/// </summary>
public interface IChartDataSource
{
    /// <summary>The datasets a spec may use (names, fields and descriptions; values may be empty).</summary>
    IReadOnlyList<ChartDataset> Describe();

    /// <summary>The dataset with its values, or null when it does not exist.</summary>
    Task<ChartDataset?> LoadAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>A data source over datasets that are already in memory.</summary>
public sealed class InMemoryDataSource(IEnumerable<ChartDataset> datasets) : IChartDataSource
{
    private readonly Dictionary<string, ChartDataset> _datasets = datasets.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ChartDataset> Describe() => [.. _datasets.Values];

    public Task<ChartDataset?> LoadAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(_datasets.GetValueOrDefault(name));
}
