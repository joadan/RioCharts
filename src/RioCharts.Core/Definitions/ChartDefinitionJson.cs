using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RioCharts.Core.Data;

namespace RioCharts.Core.Definitions;

/// <summary>A problem in a definition: where (a JSON path such as <c>series[0].y</c>) and what to do about it.</summary>
public sealed record DefinitionError(string Path, string Message)
{
    /// <summary>"path: message".</summary>
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>The result of reading a JSON definition: the definition (null when it could not be read), errors and notes.</summary>
public sealed record DefinitionParseResult(ChartDefinition? Definition, IReadOnlyList<DefinitionError> Errors, IReadOnlyList<string> Warnings)
{
    /// <summary>True when the definition was read without errors.</summary>
    public bool Success => Definition is not null && Errors.Count == 0;
}

/// <summary>
/// JSON for chart definitions: tolerant reading (any letter case, unknown fields ignored, common
/// synonyms such as "column" or "doughnut"), strict errors that name the path and the valid
/// options, compact writing, and the JSON schema with descriptions for AI agents.
/// </summary>
public static class ChartDefinitionJson
{
    /// <summary>The most series a JSON definition may have.</summary>
    public const int MaxSeries = 20;
    /// <summary>The most inline data rows a JSON definition may have.</summary>
    public const int MaxInlineRows = 1000;
    /// <summary>The most annotations a JSON definition may have.</summary>
    public const int MaxAnnotations = 50;

    /// <summary>
    /// camelCase names and enum values, nulls and defaults left out when writing. Read definitions with
    /// <see cref="Parse"/>: the source-generated metadata cannot keep the defaults of properties
    /// missing from the JSON, so Parse fills them in first.
    /// </summary>
    internal static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        // Source-generated metadata (ChartDefinitionJsonContext): no reflection, so trimming and native AOT keep working.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            // Saved definitions stay readable: "#,0 €" and "°C" as they are, not €. HTML-sensitive
            // characters (< > & ' ") are still escaped, so the JSON is safe to put in a page.
            Encoder = JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            TypeInfoResolver = ChartDefinitionJsonContext.Default.WithAddedModifier(SkipDefaults),
        };

        // camelCase enum values ("line", "monotone"); generic converters need no runtime code generation.
        var camel = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter<SeriesType>(camel));
        options.Converters.Add(new JsonStringEnumConverter<CurveType>(camel));
        options.Converters.Add(new JsonStringEnumConverter<LineDash>(camel));
        options.Converters.Add(new JsonStringEnumConverter<PointHighlight>(camel));
        options.Converters.Add(new JsonStringEnumConverter<ScaleType>(camel));
        options.Converters.Add(new JsonStringEnumConverter<AxisPosition>(camel));
        options.Converters.Add(new JsonStringEnumConverter<LegendMode>(camel));
        options.Converters.Add(new JsonStringEnumConverter<LegendPosition>(camel));
        options.Converters.Add(new JsonStringEnumConverter<MapProjection>(camel));
        options.Converters.Add(new JsonStringEnumConverter<AnnotationType>(camel));
        options.Converters.Add(new JsonStringEnumConverter<AnnotationAxis>(camel));
        options.Converters.Add(new JsonStringEnumConverter<LabelPosition>(camel));
        options.Converters.Add(new JsonStringEnumConverter<AnnotationLayer>(camel));
        options.Converters.Add(new JsonStringEnumConverter<FilterOp>(camel));
        options.Converters.Add(new JsonStringEnumConverter<TimeBucket>(camel));
        options.Converters.Add(new JsonStringEnumConverter<Aggregate>(camel));
        options.Converters.Add(new JsonStringEnumConverter<ChartSort>(camel));
        options.Converters.Add(new JsonStringEnumConverter<UnspecifiedTimes>(camel));
        options.MakeReadOnly();
        return options;
    }

    private static JsonTypeInfo<ChartDefinition> SpecInfo => (JsonTypeInfo<ChartDefinition>)Options.GetTypeInfo(typeof(ChartDefinition));

    // ---------- Writing ----------

    /// <summary>
    /// The definition format this version of RioCharts writes and reads. Every 1.x release reads version 1
    /// definitions: fields, chart type names and their synonyms may be added, never removed or changed in meaning.
    /// </summary>
    public const int FormatVersion = 1;

    /// <summary>The definition as compact JSON (only what differs from the defaults), with its format <c>version</c>.</summary>
    public static string Serialize(ChartDefinition spec) =>
        JsonSerializer.Serialize(spec.Version is null ? spec with { Version = FormatVersion } : spec, SpecInfo);

    /// <summary>
    /// The definition types, created with their defaults. Source generation treats init-only properties
    /// as constructor arguments, so it has no CreateObject for them and would write default(T) for
    /// a property missing from the JSON (an annotation's axis would become x, a tooltip disabled).
    /// </summary>
    private static readonly Dictionary<Type, Func<object>> Factories = new()
    {
        [typeof(ChartDefinition)] = () => new ChartDefinition(),
        [typeof(AxisOptions)] = () => new AxisOptions(),
        [typeof(LegendOptions)] = () => new LegendOptions(),
        [typeof(TooltipOptions)] = () => new TooltipOptions(),
        [typeof(SeriesOptions)] = () => new SeriesOptions(),
        [typeof(Annotation)] = () => new Annotation(),
        [typeof(Filter)] = () => new Filter(),
        [typeof(ColorBand)] = () => new ColorBand(),
    };

    /// <summary>The definition types with defaults (for a test that every object type in a definition is listed).</summary>
    internal static IEnumerable<Type> TypesWithDefaults => Factories.Keys;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, JsonObject> DefaultNodes = new();

    /// <summary>Adds the default value of every property missing from the JSON, through nested objects and lists.</summary>
    private static void FillDefaults(JsonNode node, JsonTypeInfo info)
    {
        if (info.Kind == JsonTypeInfoKind.Object && node is JsonObject obj)
        {
            var defaults = Factories.TryGetValue(info.Type, out var create)
                ? DefaultNodes.GetOrAdd(info.Type, t => (JsonObject)JsonSerializer.SerializeToNode(create(), ChartDefinitionJsonContext.Default.GetTypeInfo(t)!)!)
                : null;
            foreach (var property in info.Properties)
            {
                if (obj.TryGetPropertyValue(property.Name, out var value))
                {
                    if (value is not null && Options.TryGetTypeInfo(property.PropertyType, out var child))
                    {
                        FillDefaults(value, child);
                    }
                }
                else if (defaults?[property.Name] is { } defaultValue)
                {
                    obj[property.Name] = defaultValue.DeepClone();
                }
            }
        }
        else if (info.Kind == JsonTypeInfoKind.Enumerable && node is JsonArray array && info.ElementType is { } elementType
            && Options.TryGetTypeInfo(elementType, out var element))
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    FillDefaults(item, element);
                }
            }
        }
    }

    private static void SkipDefaults(JsonTypeInfo type)
    {
        var create = type.CreateObject ?? Factories.GetValueOrDefault(type.Type);
        if (type.Kind != JsonTypeInfoKind.Object || create is null)
        {
            return;
        }

        var defaults = create();
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

    /// <summary>The JSON schema of a chart definition, with a description for every property.</summary>
    public static JsonNode Schema()
    {
        var schema = SpecInfo.GetJsonSchemaAsNode(new JsonSchemaExporterOptions
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
            root.Insert(1, "title", "RioCharts chart definition");
            root.Insert(2, "description",
                "A chart: series read x and y fields from a dataset (or inline data); everything else has sensible defaults. "
                + "Minimal example: {\"dataset\":\"shop\",\"series\":[{\"type\":\"line\",\"x\":\"timestamp\",\"y\":\"sessions\"}]}");
        }

        return schema;
    }

    // ---------- Reading ----------

    /// <summary>Reads a definition, accepting common variations; problems come back as errors, never exceptions.</summary>
    public static DefinitionParseResult Parse(string json)
    {
        var errors = new List<DefinitionError>();
        var warnings = new List<string>();
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json, new JsonNodeOptions { PropertyNameCaseInsensitive = true },
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException e)
        {
            return new DefinitionParseResult(null, [new DefinitionError("$", $"not valid JSON ({e.Message})")], warnings);
        }

        if (node is not JsonObject root)
        {
            return new DefinitionParseResult(null, [new DefinitionError("$", "a definition is a JSON object, e.g. {\"series\":[{\"type\":\"line\",\"x\":\"time\",\"y\":\"value\"}]}")], warnings);
        }

        Normalize(root, errors, warnings);
        if (errors.Count > 0)
        {
            return new DefinitionParseResult(null, errors, warnings);
        }

        try
        {
            FillDefaults(root, SpecInfo);
            var spec = root.Deserialize(SpecInfo);
            return new DefinitionParseResult(spec, errors, warnings);
        }
        catch (JsonException e)
        {
            errors.Add(new DefinitionError(e.Path?.TrimStart('$', '.') is { Length: > 0 } p ? p : "$", ReadableJsonError(e)));
            return new DefinitionParseResult(null, errors, warnings);
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
        ["stackedarea100"] = ("area", PercentStack),
        ["percentstackedarea"] = ("area", PercentStack),
        ["100stackedarea"] = ("area", PercentStack),
        ["percentarea"] = ("area", PercentStack),
        ["bar"] = ("bar", null),
        ["horizontalbar"] = ("bar", s => s["horizontal"] = true),
        ["horizontalbars"] = ("bar", s => s["horizontal"] = true),
        ["hbar"] = ("bar", s => s["horizontal"] = true),
        ["barh"] = ("bar", s => s["horizontal"] = true),
        ["horizontalbarchart"] = ("bar", s => s["horizontal"] = true),
        ["bars"] = ("bar", null),
        ["barchart"] = ("bar", null),
        ["column"] = ("bar", null),
        ["columns"] = ("bar", null),
        ["columnchart"] = ("bar", null),
        ["stackedbar"] = ("bar", s => s.TryAdd("stack", "stack")),
        ["stackedcolumn"] = ("bar", s => s.TryAdd("stack", "stack")),
        ["stackedbar100"] = ("bar", PercentStack),
        ["stackedcolumn100"] = ("bar", PercentStack),
        ["percentstackedbar"] = ("bar", PercentStack),
        ["percentstackedcolumn"] = ("bar", PercentStack),
        ["100stackedbar"] = ("bar", PercentStack),
        ["100stackedcolumn"] = ("bar", PercentStack),
        ["percentbar"] = ("bar", PercentStack),
        ["pie"] = ("pie", null),
        ["piechart"] = ("pie", null),
        ["donut"] = ("pie", s => s.TryAdd("innerRadius", 0.6)),
        ["doughnut"] = ("pie", s => s.TryAdd("innerRadius", 0.6)),
        ["ring"] = ("pie", s => s.TryAdd("innerRadius", 0.6)),
        ["radialbar"] = ("radialBar", null),
        ["radar"] = ("radar", null),
        ["treemap"] = ("treemap", null),
        ["tree"] = ("treemap", null),
        ["treemapchart"] = ("treemap", null),
        ["radarchart"] = ("radar", null),
        ["spider"] = ("radar", null),
        ["spiderweb"] = ("radar", null),
        ["web"] = ("radar", null),
        ["scatter"] = ("scatter", null),
        ["bubble"] = ("scatter", null),
        ["bubbles"] = ("scatter", null),
        ["bubblechart"] = ("scatter", null),
        ["heatmap"] = ("heatmap", null),
        ["rangearea"] = ("rangeArea", null),
        ["boxplot"] = ("boxPlot", null),
        ["candlestick"] = ("candlestick", null),
        ["candle"] = ("candlestick", null),
        ["candles"] = ("candlestick", null),
        ["ohlc"] = ("candlestick", null),
        ["stock"] = ("candlestick", null),
        ["waterfall"] = ("waterfall", null),
        ["histogram"] = ("histogram", null),
        ["hist"] = ("histogram", null),
        ["distribution"] = ("histogram", null),
        ["bridge"] = ("waterfall", null),
        ["cascade"] = ("waterfall", null),
        ["box"] = ("boxPlot", null),
        ["boxandwhisker"] = ("boxPlot", null),
        ["whisker"] = ("boxPlot", null),
        ["range"] = ("rangeArea", null),
        ["rangeband"] = ("rangeArea", null),
        ["band"] = ("rangeArea", null),
        ["minmax"] = ("rangeArea", null),
        ["heat"] = ("heatmap", null),
        ["matrix"] = ("heatmap", null),
        ["calendar"] = ("calendar", null),
        ["calendarheatmap"] = ("calendar", null),
        ["calendarchart"] = ("calendar", null),
        ["contributions"] = ("calendar", null),
        ["contributiongraph"] = ("calendar", null),
        ["scatterplot"] = ("scatter", null),
        ["scatterchart"] = ("scatter", null),
        ["points"] = ("scatter", null),
        ["point"] = ("scatter", null),
        ["dots"] = ("scatter", null),
        ["xy"] = ("scatter", null),
        ["radial"] = ("radialBar", null),
        ["gauge"] = ("gauge", null),
        ["gaugechart"] = ("gauge", null),
        ["meter"] = ("gauge", null),
        ["dial"] = ("gauge", null),
        ["speedometer"] = ("gauge", null),
        ["kpigauge"] = ("gauge", null),
        ["bullet"] = ("bullet", null),
        ["map"] = ("map", null),
        ["worldmap"] = ("map", null),
        ["choropleth"] = ("map", null),
        ["choroplethmap"] = ("map", null),
        ["geomap"] = ("map", null),
        ["filledmap"] = ("map", null),
        ["mapbubble"] = ("mapBubble", null),
        ["bubblemap"] = ("mapBubble", null),
        ["geobubble"] = ("mapBubble", null),
        ["pointmap"] = ("mapBubble", null),
        ["symbolmap"] = ("mapBubble", null),
        ["dumbbell"] = ("dumbbell", null),
        ["dumbbellchart"] = ("dumbbell", null),
        ["dumbbelldot"] = ("dumbbell", null),
        ["connecteddot"] = ("dumbbell", null),
        ["connecteddotplot"] = ("dumbbell", null),
        ["dotplot"] = ("dumbbell", null),
        ["barbell"] = ("dumbbell", null),
        ["bulletchart"] = ("bullet", null),
        ["bulletgraph"] = ("bullet", null),
        ["bulletbar"] = ("bullet", null),
        ["funnel"] = ("funnel", null),
        ["funnelchart"] = ("funnel", null),
        ["conversionfunnel"] = ("funnel", null),
        ["salesfunnel"] = ("funnel", null),
        ["pipeline"] = ("funnel", null),
        ["rangebar"] = ("rangeBar", null),
        ["timeline"] = ("rangeBar", null),
        ["gantt"] = ("rangeBar", null),
        ["ganttchart"] = ("rangeBar", null),
        ["statetimeline"] = ("rangeBar", null),
        ["schedule"] = ("rangeBar", null),
        ["auto"] = ("auto", null),
    };

    // "stackedBar100" and the like: stacked, on an axis that stacks to 100 % (moved there by Normalize).
    private static void PercentStack(JsonObject series)
    {
        series.TryAdd("stack", "stack");
        series.TryAdd("stacking", "percent");
    }

    private static readonly Dictionary<string, string> Stackings = new(StringComparer.Ordinal)
    {
        ["normal"] = "normal", ["stacked"] = "normal", ["absolute"] = "normal", ["percent"] = "percent", ["percentage"] = "percent",
        ["100"] = "percent", ["normalize"] = "percent", ["normalized"] = "percent", ["expand"] = "percent", ["relative"] = "percent",
    };

    private static readonly Dictionary<string, string> Projections = new(StringComparer.Ordinal)
    {
        ["equalearth"] = "equalEarth", ["equalarea"] = "equalEarth", ["default"] = "equalEarth",
        ["mercator"] = "mercator", ["webmercator"] = "mercator", ["web"] = "mercator", ["epsg3857"] = "mercator",
    };

    private static readonly Dictionary<string, string> Curves = new(StringComparer.Ordinal)
    {
        ["linear"] = "linear", ["straight"] = "linear", ["monotone"] = "monotone", ["smooth"] = "monotone", ["spline"] = "monotone",
        ["curved"] = "monotone", ["step"] = "step", ["stepped"] = "step", ["steps"] = "step", ["stepafter"] = "step",
    };

    private static readonly Dictionary<string, string> Dashes = new(StringComparer.Ordinal)
    {
        ["solid"] = "solid", ["none"] = "solid", ["dashed"] = "dashed", ["dash"] = "dashed", ["dashes"] = "dashed",
        ["dotted"] = "dotted", ["dot"] = "dotted", ["dots"] = "dotted",
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
        ["yearly"] = "year", ["annual"] = "year", ["auto"] = "auto", ["automatic"] = "auto",
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

    /// <summary>Fixes the shape and vocabulary of a definition in place, collecting errors for what cannot be read.</summary>
    private static void Normalize(JsonObject root, List<DefinitionError> errors, List<string> warnings)
    {
        Version(root, warnings);

        // A single series given flat ({ "type": "line", "x": ..., "y": ... }) or as an object.
        if (root["series"] is null && (root.ContainsKey("x") || root.ContainsKey("y")))
        {
            var single = new JsonObject();
            foreach (var key in new[] { "type", "x", "y", "name", "groupBy", "aggregate", "timeBucket", "curve", "dash", "stack", "filter", "sort", "limit" })
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
                errors.Add(new DefinitionError("series", $"at most {MaxSeries} series (got {series.Count}); use groupBy only on fields with few values."));
            }

            for (var i = 0; i < series.Count; i++)
            {
                if (series[i] is not JsonObject s)
                {
                    errors.Add(new DefinitionError($"series[{i}]", "each series is an object, e.g. {\"type\":\"line\",\"x\":\"time\",\"y\":\"value\"}"));
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
                        errors.Add(new DefinitionError($"series[{i}].type",
                            $"'{typeName}' is not supported; valid: line, area, bar, scatter, bubble, heatmap, rangeArea, boxPlot, candlestick, waterfall, histogram, rangeBar, pie, radialBar, gauge, bullet, dumbbell, funnel, calendar, radar, treemap, map, mapBubble, auto (also column, donut, stackedBar, spline, meter, points, spider, timeline, gantt, pipeline)."));
                    }
                }

                // "horizontalBar" (or "horizontal": true on a series) makes the whole chart horizontal.
                if (s["horizontal"] is JsonValue h && h.TryGetValue<bool>(out var horizontal))
                {
                    s.Remove("horizontal");
                    if (horizontal)
                    {
                        root["horizontal"] = true;
                    }
                }

                // "stacking" belongs on the series' axis (all stacks on an axis add up the same way).
                if (s["stacking"] is { } stacking)
                {
                    s.Remove("stacking");
                    MoveToAxis(root, s, "stacking", stacking, $"series[{i}].stacking", warnings);
                }

                Vocabulary(s, "curve", Curves, $"series[{i}].curve", errors);
                Vocabulary(s, "dash", Dashes, $"series[{i}].dash", errors);
                Vocabulary(s, "aggregate", Aggregates, $"series[{i}].aggregate", errors);
                Vocabulary(s, "timeBucket", Buckets, $"series[{i}].timeBucket", errors);
                Vocabulary(s, "sort", Sorts, $"series[{i}].sort", errors);
                Highlights(s, $"series[{i}].highlight", errors);
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
            errors.Add(new DefinitionError("data", $"at most {MaxInlineRows} inline rows (got {rows.Count}); refer to a dataset id instead."));
        }

        if (root["annotations"] is JsonArray annotations && annotations.Count > MaxAnnotations)
        {
            errors.Add(new DefinitionError("annotations", $"at most {MaxAnnotations} annotations (got {annotations.Count})."));
        }

        if (root["stacking"] is { } rootStacking)
        {
            root.Remove("stacking");
            root["y"] ??= new JsonObject();
            if (root["y"] is JsonObject y && !y.ContainsKey("stacking"))
            {
                y["stacking"] = rootStacking;
                warnings.Add("stacking: moved to y.stacking.");
            }
        }

        Vocabulary(root["y"] as JsonObject, "stacking", Stackings, "y.stacking", errors);
        if (root["yAxes"] is JsonArray stackAxes)
        {
            for (var i = 0; i < stackAxes.Count; i++)
            {
                Vocabulary(stackAxes[i] as JsonObject, "stacking", Stackings, $"yAxes[{i}].stacking", errors);
            }
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
        Vocabulary(root, "projection", Projections, "projection", errors);
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

    /// <summary>
    /// version: missing means 1. A whole number (also "1", 1.0 or "1.0"); anything else is left out with a warning, and a
    /// newer version is read as this one (fields this version does not know are ignored, as always).
    /// </summary>
    private static void Version(JsonObject root, List<string> warnings)
    {
        if (!root.TryGetPropertyValue("version", out var node) || node is null)
        {
            return;
        }

        var number = node is JsonValue value
            ? value.TryGetValue<double>(out var d) ? d
            : value.TryGetValue<string>(out var text) && double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN
            : double.NaN;
        if (number >= 1 && number == Math.Floor(number) && number <= int.MaxValue)
        {
            var version = (int)number;
            root["version"] = version;
            if (version > FormatVersion)
            {
                warnings.Add($"version: {version} is newer than this RioCharts reads ({FormatVersion}); read as version {FormatVersion}, unknown fields are ignored.");
            }
        }
        else
        {
            root.Remove("version");
            warnings.Add($"version: '{node.ToJsonString()}' is not a format version (a whole number, {FormatVersion} for this format); left out.");
        }
    }

    /// <summary>
    /// The 1.x contract of the JSON format, one line per promise: every field (as a path), every chart type name and
    /// synonym, and every word an enum field reads. A test compares it with a committed list, so a removal is caught.
    /// </summary>
    internal static IEnumerable<string> ContractLines()
    {
        var lines = new SortedSet<string>(StringComparer.Ordinal);
        void Fields(JsonNode? schema, string path, int depth)
        {
            if (schema is not JsonObject obj || depth > 6)
            {
                return;
            }

            if (obj["properties"] is JsonObject properties)
            {
                foreach (var (name, child) in properties)
                {
                    lines.Add($"field {path}{name}");
                    Fields(child, $"{path}{name}.", depth + 1);
                }
            }

            if (obj["items"] is { } items)
            {
                Fields(items, path.TrimEnd('.') + "[].", depth + 1);
            }

            if (obj["type"] is JsonArray || obj["anyOf"] is JsonArray)
            {
                foreach (var option in (obj["anyOf"] as JsonArray) ?? [])
                {
                    Fields(option, path, depth + 1);
                }
            }
        }

        Fields(Schema(), "", 0);
        foreach (var (alias, target) in SeriesTypes)
        {
            lines.Add($"seriesType {alias} -> {target.Type}");
        }

        void Words(string field, Dictionary<string, string> words)
        {
            foreach (var (word, value) in words)
            {
                lines.Add($"word {field} {word} -> {value}");
            }
        }

        Words("series.curve", Curves);
        Words("series.dash", Dashes);
        Words("series.aggregate", Aggregates);
        Words("series.timeBucket", Buckets);
        Words("series.sort", Sorts);
        Words("series.filter.op", FilterOps);
        Words("axis.stacking", Stackings);
        Words("projection", Projections);
        void Enum<TEnum>(string field) where TEnum : struct, System.Enum
        {
            foreach (var name in System.Enum.GetNames<TEnum>())
            {
                lines.Add($"word {field} {JsonNamingPolicy.CamelCase.ConvertName(name)}");
            }
        }

        Enum<PointHighlight>("series.highlight");
        Enum<ScaleType>("axis.scale");
        Enum<AxisPosition>("axis.position");
        Enum<LegendMode>("legend.mode");
        Enum<LegendPosition>("legend.position");
        Enum<UnspecifiedTimes>("unspecifiedTimes");
        Enum<AnnotationType>("annotations.type");
        Enum<AnnotationAxis>("annotations.axis");
        Enum<LabelPosition>("annotations.labelPosition");
        Enum<AnnotationLayer>("annotations.layer");
        return lines;
    }

    /// <summary>Lower case without separators ("Stacked-Bar" → "stackedbar"); symbols such as "&gt;=" stay as they are.</summary>
    private static string Key(string value) =>
        new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray()) is { Length: > 0 } k ? k : value.Trim();

    /// <summary>
    /// Puts a series' axis setting on its axis: the yAxes entry its yAxisId names, or y. An axis that already has the
    /// setting keeps its own.
    /// </summary>
    private static void MoveToAxis(JsonObject root, JsonObject series, string property, JsonNode value, string path, List<string> warnings)
    {
        var id = series["yAxisId"] is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;
        var axis = id is null ? null : (root["yAxes"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(a => a["id"] is JsonValue aid && aid.TryGetValue<string>(out var t) && t == id);
        if (axis is null)
        {
            root["y"] ??= new JsonObject();
            axis = root["y"] as JsonObject;
        }

        if (axis is not null && !axis.ContainsKey(property))
        {
            axis[property] = value.DeepClone();
            warnings.Add($"{path}: moved to the series' axis ({(axis == root["y"] ? "y" : id)}.{property}).");
        }
    }

    private static void Vocabulary(JsonObject? obj, string property, Dictionary<string, string> words, string path, List<DefinitionError> errors)
    {
        if (obj?[property] is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return;
        }

        if (words.TryGetValue(Key(text), out var mapped) || words.TryGetValue(text.Trim(), out mapped))
        {
            obj[property] = mapped;
        }
        else
        {
            errors.Add(new DefinitionError(path, $"'{text}' is not valid; use one of: {string.Join(", ", words.Values.Distinct())}."));
        }
    }

    /// <summary>
    /// highlight: "last", "last, max" or ["last", "max"] (any letter case); written back as the
    /// comma-separated names the enum reads.
    /// </summary>
    private static void Highlights(JsonObject series, string path, List<DefinitionError> errors)
    {
        var node = series["highlight"];
        IEnumerable<string>? parts = node switch
        {
            JsonValue v when v.TryGetValue<string>(out var text) => text.Split([',', '|', ' '], StringSplitOptions.RemoveEmptyEntries),
            JsonArray list => list.Select(n => n is JsonValue item && item.TryGetValue<string>(out var t) ? t : "?"),
            _ => null,
        };
        if (parts is null)
        {
            return;
        }

        var names = new List<string>();
        foreach (var part in parts)
        {
            var match = Enum.GetNames<PointHighlight>().FirstOrDefault(n => Key(n) == Key(part));
            if (match is null)
            {
                errors.Add(new DefinitionError(path, $"'{part}' is not valid; use one or more of: last, first, min, max (e.g. \"last, max\")."));
                return;
            }

            names.Add(match);
        }

        series["highlight"] = names.Count == 0 ? "None" : string.Join(", ", names.Distinct());
    }

    private static void CheckEnum<TEnum>(JsonNode? parent, string property, string path, List<DefinitionError> errors)
        where TEnum : struct, Enum
    {
        if (parent is not JsonObject obj || obj[property] is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return;
        }

        var match = Enum.GetNames<TEnum>().FirstOrDefault(n => Key(n) == Key(text));
        if (match is null)
        {
            errors.Add(new DefinitionError(path, $"'{text}' is not valid; use one of: {string.Join(", ", Enum.GetNames<TEnum>().Select(JsonNamingPolicy.CamelCase.ConvertName))}."));
        }
        else
        {
            obj[property] = JsonNamingPolicy.CamelCase.ConvertName(match);
        }
    }
}

/// <summary>
/// Source-generated JSON metadata for definitions. The primitive types are for the <c>object?</c> values
/// in definitions built in code (inline data rows, annotation positions); parsed definitions hold JsonElements.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ChartDefinition))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(DateOnly))]
[JsonSerializable(typeof(TimeOnly))]
internal sealed partial class ChartDefinitionJsonContext : JsonSerializerContext;
