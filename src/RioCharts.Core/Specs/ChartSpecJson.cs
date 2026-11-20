using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RioCharts.Core.Data;

namespace RioCharts.Core.Specs;

/// <summary>A problem in a spec: where (a JSON path such as <c>series[0].y</c>) and what to do about it.</summary>
public sealed record SpecError(string Path, string Message)
{
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>The result of reading a JSON spec: the spec (null when it could not be read), errors and notes.</summary>
public sealed record SpecParseResult(ChartSpec? Spec, IReadOnlyList<SpecError> Errors, IReadOnlyList<string> Warnings)
{
    public bool Success => Spec is not null && Errors.Count == 0;
}

/// <summary>
/// JSON for chart specs: tolerant reading (any letter case, unknown fields ignored, common
/// synonyms such as "column" or "doughnut"), strict errors that name the path and the valid
/// options, compact writing, and the JSON schema with descriptions for AI agents.
/// </summary>
public static class ChartSpecJson
{
    public const int MaxSeries = 20;
    public const int MaxInlineRows = 1000;
    public const int MaxAnnotations = 50;

    /// <summary>camelCase names and enum values, nulls and defaults left out when writing.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipDefaults } },
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly();
        return options;
    }

    // ---------- Writing ----------

    /// <summary>The spec as compact JSON (only what differs from the defaults).</summary>
    public static string Serialize(ChartSpec spec) => JsonSerializer.Serialize(spec, Options);

    private static void SkipDefaults(JsonTypeInfo type)
    {
        if (type.Kind != JsonTypeInfoKind.Object || type.Type.GetConstructor(Type.EmptyTypes) is null)
        {
            return;
        }

        var defaults = Activator.CreateInstance(type.Type);
        foreach (var property in type.Properties)
        {
            var getter = property.Get;
            if (getter is null)
            {
                continue;
            }

            var defaultValue = getter(defaults!);
            property.ShouldSerialize = (_, value) => value switch
            {
                null => false,
                string s when s.Length == 0 => false,
                ICollection { Count: 0 } => false,
                IEnumerable e when value is not string && !e.Cast<object?>().Any() => false,
                _ => !Equals(value, defaultValue),
            };
        }
    }

    // ---------- Schema ----------

    /// <summary>The JSON schema of a chart spec, with a description for every property.</summary>
    public static JsonNode Schema()
    {
        var schema = Options.GetJsonSchemaAsNode(typeof(ChartSpec), new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) =>
            {
                var description = context.PropertyInfo?.AttributeProvider?.GetCustomAttributes(typeof(DescriptionAttribute), true)
                    .OfType<DescriptionAttribute>().FirstOrDefault()?.Description
                    ?? (context.PropertyInfo is null ? context.TypeInfo.Type.GetCustomAttribute<DescriptionAttribute>()?.Description : null);
                if (description is not null && node is JsonObject obj && !obj.ContainsKey("description"))
                {
                    obj.Insert(0, "description", description);
                }

                return node;
            },
        });

        if (schema is JsonObject root)
        {
            root.Insert(0, "$schema", "https://json-schema.org/draft/2020-12/schema");
            root.Insert(1, "title", "RioCharts chart spec");
            root.Insert(2, "description",
                "A chart: series read x and y fields from a dataset (or inline data); everything else has sensible defaults. "
                + "Minimal example: {\"dataset\":\"ahu\",\"series\":[{\"type\":\"line\",\"x\":\"timestamp\",\"y\":\"supplyTemp\"}]}");
        }

        return schema;
    }

    // ---------- Reading ----------

    /// <summary>Reads a spec, accepting common variations; problems come back as errors, never exceptions.</summary>
    public static SpecParseResult Parse(string json)
    {
        var errors = new List<SpecError>();
        var warnings = new List<string>();
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json, new JsonNodeOptions { PropertyNameCaseInsensitive = true },
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException e)
        {
            return new SpecParseResult(null, [new SpecError("$", $"not valid JSON ({e.Message})")], warnings);
        }

        if (node is not JsonObject root)
        {
            return new SpecParseResult(null, [new SpecError("$", "a spec is a JSON object, e.g. {\"series\":[{\"type\":\"line\",\"x\":\"time\",\"y\":\"value\"}]}")], warnings);
        }

        Normalize(root, errors, warnings);
        if (errors.Count > 0)
        {
            return new SpecParseResult(null, errors, warnings);
        }

        try
        {
            var spec = root.Deserialize<ChartSpec>(Options);
            return new SpecParseResult(spec, errors, warnings);
        }
        catch (JsonException e)
        {
            errors.Add(new SpecError(e.Path?.TrimStart('$', '.') is { Length: > 0 } p ? p : "$", ReadableJsonError(e)));
            return new SpecParseResult(null, errors, warnings);
        }
    }

    private static string ReadableJsonError(JsonException e) =>
        e.InnerException is InvalidOperationException or FormatException ? e.InnerException.Message : e.Message.Split(" Path:")[0];

    private static readonly Dictionary<string, (string Type, Action<JsonObject>? Extra)> SeriesTypes = new(StringComparer.Ordinal)
    {
        ["line"] = ("line", null),
        ["linechart"] = ("line", null),
        ["spline"] = ("line", s => s.TryAdd("curve", "monotone")),
        ["smooth"] = ("line", s => s.TryAdd("curve", "monotone")),
        ["smoothline"] = ("line", s => s.TryAdd("curve", "monotone")),
        ["stepline"] = ("line", s => s.TryAdd("curve", "step")),
        ["timeseries"] = ("line", null),
        ["area"] = ("area", null),
        ["areachart"] = ("area", null),
        ["stackedarea"] = ("area", s => s.TryAdd("stack", "stack")),
        ["bar"] = ("bar", null),
        ["bars"] = ("bar", null),
        ["barchart"] = ("bar", null),
        ["column"] = ("bar", null),
        ["columns"] = ("bar", null),
        ["columnchart"] = ("bar", null),
        ["stackedbar"] = ("bar", s => s.TryAdd("stack", "stack")),
        ["stackedcolumn"] = ("bar", s => s.TryAdd("stack", "stack")),
        ["pie"] = ("pie", null),
        ["piechart"] = ("pie", null),
        ["donut"] = ("pie", s => s.TryAdd("innerRadius", 0.6)),
        ["doughnut"] = ("pie", s => s.TryAdd("innerRadius", 0.6)),
        ["ring"] = ("pie", s => s.TryAdd("innerRadius", 0.6)),
        ["radialbar"] = ("radialBar", null),
        ["radial"] = ("radialBar", null),
        ["gauge"] = ("radialBar", null),
        ["auto"] = ("auto", null),
    };

    private static readonly Dictionary<string, string> Curves = new(StringComparer.Ordinal)
    {
        ["linear"] = "linear", ["straight"] = "linear", ["monotone"] = "monotone", ["smooth"] = "monotone", ["spline"] = "monotone",
        ["curved"] = "monotone", ["step"] = "step", ["stepped"] = "step", ["steps"] = "step", ["stepafter"] = "step",
    };

    private static readonly Dictionary<string, string> Aggregates = new(StringComparer.Ordinal)
    {
        ["sum"] = "sum", ["total"] = "sum", ["average"] = "average", ["avg"] = "average", ["mean"] = "average", ["min"] = "min",
        ["minimum"] = "min", ["max"] = "max", ["maximum"] = "max", ["count"] = "count", ["n"] = "count", ["last"] = "last", ["latest"] = "last",
    };

    private static readonly Dictionary<string, string> Buckets = new(StringComparer.Ordinal)
    {
        ["minute"] = "minute", ["hour"] = "hour", ["hourly"] = "hour", ["day"] = "day", ["daily"] = "day", ["date"] = "day", ["week"] = "week",
        ["weekly"] = "week", ["month"] = "month", ["monthly"] = "month", ["quarter"] = "quarter", ["quarterly"] = "quarter", ["year"] = "year",
        ["yearly"] = "year", ["annual"] = "year",
    };

    private static readonly Dictionary<string, string> Sorts = new(StringComparer.Ordinal)
    {
        ["ascending"] = "ascending", ["asc"] = "ascending", ["descending"] = "descending", ["desc"] = "descending",
    };

    private static readonly Dictionary<string, string> FilterOps = new(StringComparer.Ordinal)
    {
        ["eq"] = "eq", ["="] = "eq", ["=="] = "eq", ["equals"] = "eq", ["ne"] = "ne", ["!="] = "ne", ["<>"] = "ne", ["notequals"] = "ne",
        ["gt"] = "gt", [">"] = "gt", ["gte"] = "gte", [">="] = "gte", ["ge"] = "gte", ["lt"] = "lt", ["<"] = "lt", ["lte"] = "lte",
        ["<="] = "lte", ["le"] = "lte", ["contains"] = "contains", ["like"] = "contains",
    };

    /// <summary>Fixes the shape and vocabulary of a spec in place, collecting errors for what cannot be read.</summary>
    private static void Normalize(JsonObject root, List<SpecError> errors, List<string> warnings)
    {
        // A single series given flat ({ "type": "line", "x": ..., "y": ... }) or as an object.
        if (root["series"] is null && (root.ContainsKey("x") || root.ContainsKey("y")))
        {
            var single = new JsonObject();
            foreach (var key in new[] { "type", "x", "y", "name", "groupBy", "aggregate", "timeBucket", "curve", "stack", "filter", "sort", "limit" })
            {
                if (root[key] is { } value)
                {
                    root.Remove(key);
                    single[key] = value;
                }
            }

            root["series"] = new JsonArray(single);
            warnings.Add("series: read the top-level x/y as one series.");
        }
        else if (root["series"] is JsonObject one)
        {
            root.Remove("series");
            root["series"] = new JsonArray(one);
        }

        if (root["series"] is JsonArray series)
        {
            if (series.Count > MaxSeries)
            {
                errors.Add(new SpecError("series", $"at most {MaxSeries} series (got {series.Count}); use groupBy only on fields with few values."));
            }

            for (var i = 0; i < series.Count; i++)
            {
                if (series[i] is not JsonObject s)
                {
                    errors.Add(new SpecError($"series[{i}]", "each series is an object, e.g. {\"type\":\"line\",\"x\":\"time\",\"y\":\"value\"}"));
                    continue;
                }

                if (s["type"] is JsonValue t && t.TryGetValue<string>(out var typeName))
                {
                    if (SeriesTypes.TryGetValue(Key(typeName), out var mapped))
                    {
                        s["type"] = mapped.Type;
                        mapped.Extra?.Invoke(s);
                        if (!string.Equals(Key(typeName), Key(mapped.Type), StringComparison.Ordinal))
                        {
                            warnings.Add($"series[{i}].type: '{typeName}' read as {mapped.Type}.");
                        }
                    }
                    else
                    {
                        errors.Add(new SpecError($"series[{i}].type",
                            $"'{typeName}' is not supported; valid: line, area, bar, pie, radialBar, auto (also column, donut, stackedBar, spline, gauge)."));
                    }
                }

                Vocabulary(s, "curve", Curves, $"series[{i}].curve", errors);
                Vocabulary(s, "aggregate", Aggregates, $"series[{i}].aggregate", errors);
                Vocabulary(s, "timeBucket", Buckets, $"series[{i}].timeBucket", errors);
                Vocabulary(s, "sort", Sorts, $"series[{i}].sort", errors);
                if (s["filter"] is JsonObject singleFilter)
                {
                    s.Remove("filter");
                    s["filter"] = new JsonArray(singleFilter);
                }

                if (s["filter"] is JsonArray filters)
                {
                    for (var f = 0; f < filters.Count; f++)
                    {
                        if (filters[f] is JsonObject filter)
                        {
                            Vocabulary(filter, "op", FilterOps, $"series[{i}].filter[{f}].op", errors);
                        }
                    }
                }
            }
        }

        if (root["data"] is JsonArray rows && rows.Count > MaxInlineRows)
        {
            errors.Add(new SpecError("data", $"at most {MaxInlineRows} inline rows (got {rows.Count}); refer to a dataset id instead."));
        }

        if (root["annotations"] is JsonArray annotations && annotations.Count > MaxAnnotations)
        {
            errors.Add(new SpecError("annotations", $"at most {MaxAnnotations} annotations (got {annotations.Count})."));
        }

        // Other enums: any letter case and separators; unknown values get the list of valid ones.
        CheckEnum<ScaleType>(root["x"], "scale", "x.scale", errors);
        CheckEnum<ScaleType>(root["y"], "scale", "y.scale", errors);
        CheckEnum<AxisPosition>(root["y"], "position", "y.position", errors);
        if (root["yAxes"] is JsonArray yAxes)
        {
            for (var i = 0; i < yAxes.Count; i++)
            {
                CheckEnum<ScaleType>(yAxes[i], "scale", $"yAxes[{i}].scale", errors);
                CheckEnum<AxisPosition>(yAxes[i], "position", $"yAxes[{i}].position", errors);
            }
        }

        CheckEnum<LegendMode>(root["legend"], "mode", "legend.mode", errors);
        CheckEnum<LegendPosition>(root["legend"], "position", "legend.position", errors);
        CheckEnum<UnspecifiedTimes>(root, "unspecifiedTimes", "unspecifiedTimes", errors);
        if (root["annotations"] is JsonArray list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                CheckEnum<AnnotationType>(list[i], "type", $"annotations[{i}].type", errors);
                CheckEnum<AnnotationAxis>(list[i], "axis", $"annotations[{i}].axis", errors);
                CheckEnum<LabelPosition>(list[i], "labelPosition", $"annotations[{i}].labelPosition", errors);
                CheckEnum<AnnotationLayer>(list[i], "layer", $"annotations[{i}].layer", errors);
            }
        }
    }

    /// <summary>Lower case without separators ("Stacked-Bar" → "stackedbar"); symbols such as "&gt;=" stay as they are.</summary>
    private static string Key(string value) =>
        new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray()) is { Length: > 0 } k ? k : value.Trim();

    private static void Vocabulary(JsonObject obj, string property, Dictionary<string, string> words, string path, List<SpecError> errors)
    {
        if (obj[property] is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return;
        }

        if (words.TryGetValue(Key(text), out var mapped) || words.TryGetValue(text.Trim(), out mapped))
        {
            obj[property] = mapped;
        }
        else
        {
            errors.Add(new SpecError(path, $"'{text}' is not valid; use one of: {string.Join(", ", words.Values.Distinct())}."));
        }
    }

    private static void CheckEnum<TEnum>(JsonNode? parent, string property, string path, List<SpecError> errors)
        where TEnum : struct, Enum
    {
        if (parent is not JsonObject obj || obj[property] is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return;
        }

        var match = Enum.GetNames<TEnum>().FirstOrDefault(n => Key(n) == Key(text));
        if (match is null)
        {
            errors.Add(new SpecError(path, $"'{text}' is not valid; use one of: {string.Join(", ", Enum.GetNames<TEnum>().Select(JsonNamingPolicy.CamelCase.ConvertName))}."));
        }
        else
        {
            obj[property] = JsonNamingPolicy.CamelCase.ConvertName(match);
        }
    }
}
