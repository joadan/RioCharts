using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using RioCharts.Core.Definitions;

namespace RioCharts.Docs.Editor;

// The playground's property editor, built with a future editor component in mind: everything here uses
// only the public API of RioCharts.Core and edits a definition as JSON, so it can move into a package
// later. It is not part of 1.0. What it guesses that a future Core model should say outright is marked
// "Guess" (see docs/implementation-notes.md, "Playground editor").

/// <summary>What kind of value a property takes, which decides the input the editor shows.</summary>
public enum PropertyKind
{
    Text,
    Number,
    Integer,
    Boolean,
    Choice,
    /// <summary>The name of a field in the dataset.</summary>
    Field,
    /// <summary>A list of strings, written comma separated.</summary>
    TextList,
    Object,
    ObjectList,
    /// <summary>Something the editor leaves to the JSON (inline rows, free-form objects).</summary>
    Json,
}

/// <summary>One option of a chart definition, read from <see cref="ChartDefinitionJson.Schema"/>.</summary>
public sealed record DefinitionProperty(
    string Name,
    string? Description,
    PropertyKind Kind,
    IReadOnlyList<string> Choices,
    IReadOnlyList<DefinitionProperty> Properties)
{
    /// <summary>Whether the name or description contains the search text (case-insensitive), or any child property does.</summary>
    public bool Matches(string? search) => MatchesSelf(search) || Properties.Any(p => p.Matches(search));

    /// <summary>Whether the name or description itself contains the search text; a section that does shows all its options.</summary>
    public bool MatchesSelf(string? search) =>
        string.IsNullOrWhiteSpace(search)
        || Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
        || (Description?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) ?? false);

    public DefinitionProperty? Child(string name) => Properties.FirstOrDefault(p => p.Name == name);
}

/// <summary>The chart definition's options as a tree, and reading and writing them on a JSON object.</summary>
public static class DefinitionProperties
{
    // Guess: the schema does not mark which string options name a dataset field, so they are listed here.
    private static readonly HashSet<string> FieldNames =
    [
        "x", "y", "groupBy", "low", "high", "open", "q1", "q3", "row", "start", "end", "total", "latitude", "longitude",
        "size", "colorField", "errorField", "errorLow", "errorHigh", "targetField", "from", "field",
    ];

    // Declared after FieldNames, which it reads while it is built.
    /// <summary>The root: the chart's own options, with <c>series</c>, axes, legend and so on as children.</summary>
    public static DefinitionProperty Root { get; } = FromSchema("definition", ChartDefinitionJson.Schema());

    private static DefinitionProperty FromSchema(string name, JsonNode? node, bool inFieldScope = true)
    {
        var obj = node as JsonObject;
        var description = obj?["description"]?.GetValue<string>();
        var types = Types(obj);

        if (obj?["enum"] is JsonArray choices)
        {
            return new(name, description, PropertyKind.Choice, [.. choices.Select(c => c?.ToString() ?? "")], []);
        }

        if (types.Contains("object") && obj?["properties"] is JsonObject)
        {
            return new(name, description, PropertyKind.Object, [], Children(obj));
        }

        if (types.Contains("array"))
        {
            var items = obj?["items"] as JsonObject;
            if (items?["properties"] is JsonObject)
            {
                return new(name, description, PropertyKind.ObjectList, [], Children(items));
            }

            return Types(items).Contains("string") || name == "path"
                ? new(name, description, PropertyKind.TextList, [], [])
                : new(name, description, PropertyKind.Json, [], []);
        }

        if (types.Contains("boolean"))
        {
            return new(name, description, PropertyKind.Boolean, [], []);
        }

        if (types.Contains("integer"))
        {
            return new(name, description, PropertyKind.Integer, [], []);
        }

        if (types.Contains("number"))
        {
            return new(name, description, PropertyKind.Number, [], []);
        }

        if (types.Contains("string"))
        {
            return new(name, description, inFieldScope && FieldNames.Contains(name) ? PropertyKind.Field : PropertyKind.Text, [], []);
        }

        return new(name, description, PropertyKind.Json, [], []);

        static List<DefinitionProperty> Children(JsonObject obj) =>
            [.. ((JsonObject)obj["properties"]!).Select(p => FromSchema(p.Key, p.Value))];
    }

    private static HashSet<string> Types(JsonObject? obj) => obj?["type"] switch
    {
        JsonValue v => [v.GetValue<string>()],
        JsonArray a => [.. a.Select(t => t?.GetValue<string>() ?? "")],
        _ => [],
    };

    /// <summary>The value of a property as the editor shows it: text, or null when it is not set.</summary>
    public static string? GetText(JsonObject target, DefinitionProperty property) => target[property.Name] switch
    {
        null => null,
        JsonArray list when property.Kind == PropertyKind.TextList => string.Join(", ", list.Select(i => i?.ToString())),
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonNode n => n.ToJsonString(),
    };

    /// <summary>
    /// Sets a property from what the editor's input holds; empty text removes it, so the chart falls back
    /// to its default. Numbers are written as JSON numbers when they parse, otherwise kept as text (an axis
    /// min can be a date).
    /// </summary>
    public static void SetText(JsonObject target, DefinitionProperty property, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            target.Remove(property.Name);
            return;
        }

        text = text.Trim();
        target[property.Name] = property.Kind switch
        {
            PropertyKind.Boolean => JsonValue.Create(text == "true"),
            PropertyKind.Integer when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => JsonValue.Create(i),
            PropertyKind.Number when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => JsonValue.Create(d),
            PropertyKind.TextList => new JsonArray([.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => (JsonNode?)JsonValue.Create(s))]),
            _ => JsonValue.Create(text),
        };
    }

    /// <summary>The objects of a list property (series, annotations...), creating the list when <paramref name="create"/> is set.</summary>
    public static JsonArray? List(JsonObject target, string name, bool create = false)
    {
        if (target[name] is JsonArray list)
        {
            return list;
        }

        if (!create)
        {
            return null;
        }

        list = [];
        target[name] = list;
        return list;
    }

    /// <summary>A child object (axis, legend...), creating it when <paramref name="create"/> is set.</summary>
    public static JsonObject? Object(JsonObject target, string name, bool create = false)
    {
        if (target[name] is JsonObject obj)
        {
            return obj;
        }

        if (!create)
        {
            return null;
        }

        obj = [];
        target[name] = obj;
        return obj;
    }

    /// <summary>Removes empty objects and lists the editor may have left behind, so the JSON stays as short as what is set.</summary>
    public static void Prune(JsonObject target)
    {
        foreach (var (name, value) in target.ToList())
        {
            switch (value)
            {
                case JsonObject child:
                    Prune(child);
                    if (child.Count == 0)
                    {
                        target.Remove(name);
                    }

                    break;
                case JsonArray list when name != "series":
                    foreach (var item in list.OfType<JsonObject>())
                    {
                        Prune(item);
                    }

                    if (list.Count == 0)
                    {
                        target.Remove(name);
                    }

                    break;
            }
        }
    }

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Reads definition text as a JSON object, or null when it is not one (the editor then waits for the JSON to be fixed).</summary>
    public static JsonObject? Read(string json)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: ReadOptions) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the definition indented, keeping characters such as € and ° readable.</summary>
    public static string Write(JsonObject definition)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            definition.WriteTo(writer);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// The dataset a series reads: its own, the chart's, or the chart's inline rows. Null when the name is
    /// not one of <paramref name="datasets"/> (the field pickers then take any text).
    /// </summary>
    public static ChartDataset? DatasetOf(JsonObject definition, JsonObject? series, IReadOnlyDictionary<string, ChartDataset> datasets)
    {
        var name = series?["dataset"]?.ToString() ?? definition["dataset"]?.ToString();
        if (name is null && definition["data"] is JsonArray { Count: > 0 } rows)
        {
            try
            {
                using var doc = JsonDocument.Parse(rows.ToJsonString());
                return ChartDataset.FromJson("inline", doc.RootElement);
            }
            catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
            {
                return null;
            }
        }

        return name is not null && datasets.TryGetValue(name, out var ds) ? ds : null;
    }

    /// <summary>
    /// A first definition for a dataset: the first time or text field along x and the first number as y,
    /// as a line along time or bars per category. Along time, a text field with
    /// a few values splits the data into one series each (e.g. one line per genre).
    /// </summary>
    public static JsonObject Starter(ChartDataset dataset)
    {
        var x = dataset.Fields.FirstOrDefault(f => f.Type == FieldType.Time)
            ?? dataset.Fields.FirstOrDefault(f => f.Type == FieldType.Text)
            ?? dataset.Fields.FirstOrDefault();
        var y = dataset.Fields.FirstOrDefault(f => f.Type == FieldType.Number && f != x);

        // Time along x: a line; categories: bars. (Not "auto": the editor shows the chart that was picked.)
        var series = new JsonObject { ["type"] = x?.Type == FieldType.Time ? "line" : "bar" };
        if (x is not null)
        {
            series["x"] = x.Name;
        }

        var split = x?.Type == FieldType.Time
            ? dataset.Fields.FirstOrDefault(f => f.Type == FieldType.Text && f.Values.Distinct().Count() is > 1 and <= 12)
            : null;
        if (split is not null)
        {
            series["groupBy"] = split.Name;
        }

        if (x?.Type == FieldType.Time)
        {
            series["timeBucket"] = "auto";
        }

        if (y is not null)
        {
            series["y"] = y.Name;
            series["aggregate"] = "sum";
        }
        else
        {
            series["aggregate"] = "count";
        }

        return new JsonObject
        {
            ["dataset"] = dataset.Name,
            ["series"] = new JsonArray(series),
        };
    }
}
