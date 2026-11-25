using System.Text.Json.Nodes;
using RioCharts.Core;
using RioCharts.Core.Definitions;

namespace RioCharts.Docs.Editor;

// What the editor knows that the schema does not: which charts there are from a user's point of view,
// which data each one needs (in plain words), and which options are worth showing for it. A first draft
// of the headless editor model planned for Core after 1.0 (docs/v1.md, "After 1.0"); not part of 1.0.

/// <summary>What the user wants to show: the first choice in the editor.</summary>
public sealed record ChartGoal(string Id, string Label, string Hint, string Icon);

/// <summary>A data slot of a chart: which definition property it sets, in words a user knows.</summary>
public sealed record ChartSlot(string Property, string Label, string Hint, FieldType[] Accepts, SlotPart Part = SlotPart.None, bool Required = true,
    string? NameHint = null, string? Needs = null, Func<DataField, bool>? Check = null)
{
    /// <summary>
    /// Whether a field may fill this slot on its own: the right type and, for slots like latitude, a fitting name or,
    /// for countries, fitting values.
    /// </summary>
    public bool Fits(DataField field) =>
        Accepts.Contains(field.Type)
        && (NameHint is null || NameHint.Split('|').Any(h => field.Name.Contains(h, StringComparison.OrdinalIgnoreCase)))
        && (Check is null || Check(field));

    /// <summary>What the slot needs in a user's words, for "Needs a time field": the field it takes, without the article.</summary>
    public string Need => Needs ?? (Accepts switch
    {
        [FieldType.Time] => "time field",
        [FieldType.Number] => "number field",
        [FieldType.Text] => "text field",
        _ => "field",
    });

    public bool IsList => Property == "path";
}

/// <summary>A second input next to a slot's field: how rows are combined, or how time is grouped.</summary>
public enum SlotPart
{
    None,
    /// <summary>"Sum of", "Average of"... (the series' aggregate).</summary>
    Aggregate,
    /// <summary>"per day", "per month"... (the series' timeBucket).</summary>
    TimeBucket,
}

/// <summary>A chart the user can pick: a chart type with the options that make it this variant.</summary>
public sealed record ChartKind(
    string Id,
    string Goal,
    string Label,
    string Type,
    ChartSlot[] Slots,
    string[] Sections,
    Func<ChartDefinition, SeriesOptions, ChartDataset?, bool> Is,
    Action<JsonObject, JsonObject>? Apply = null);

/// <summary>
/// A chart the picked fields can make: each field in its slot, and <paramref name="Counts"/> when the value slot
/// counts the rows because no number was picked.
/// </summary>
public sealed record ChartSuggestion(ChartKind Kind, IReadOnlyList<(ChartSlot Slot, DataField Field)> Placed, bool Counts)
{
    /// <summary>What it shows in a few words: "revenue by month", "count by channel".</summary>
    public string Caption
    {
        get
        {
            var value = Placed.FirstOrDefault(p => p.Slot.Property is "y" or "size").Field?.Name ?? (Counts ? "count" : null);
            var along = Placed.Where(p => p.Slot.Property is not ("y" or "size")).Select(p => p.Field.Name).ToList();
            return (value, along.Count) switch
            {
                (null, _) => string.Join(", ", along),
                (_, 0) => value,
                _ => $"{value} by {string.Join(" and ", along)}",
            };
        }
    }
}

/// <summary>An option in a fine-tune section, with a label and hint for users.</summary>
public sealed record EditorOption(OptionScope Scope, string Property, string Label, string Hint, OptionControl Control, (string Value, string Label)[]? Choices = null);

public enum OptionScope { Chart, Series, XAxis, YAxis, Legend }

public enum OptionControl { Text, Number, Choice, Toggle }

/// <summary>A fine-tune section: a few options under one heading, shown folded with a summary of what is set.</summary>
public sealed record EditorSection(string Id, string Label, EditorOption[] Options);

public static class ChartKinds
{
    private static readonly FieldType[] Time = [FieldType.Time];
    private static readonly FieldType[] Number = [FieldType.Number];
    private static readonly FieldType[] Text = [FieldType.Text];
    private static readonly FieldType[] Category = [FieldType.Text, FieldType.Number, FieldType.Time];
    // Slices, stages, spokes and groups: names, not times (a pie of 168 hours helps no one).
    private static readonly FieldType[] Names = [FieldType.Text];
    private static readonly FieldType[] AnyX = [FieldType.Time, FieldType.Number, FieldType.Text];

    public static readonly ChartGoal[] Goals =
    [
        new("trend", "Change over time", "A value over days, months or hours", "M3 17l5-6 4 3 6-8"),
        new("compare", "Compare", "Values side by side: per product, region, team", "M5 19V10M10 19V5M15 19v-7M20 19V8"),
        new("shares", "Parts of a whole", "Each part's share of the total", "M12 3a9 9 0 1 0 9 9h-9z M14 2.5V10h7.5A9 9 0 0 0 14 2.5z"),
        new("relation", "Relation", "How two numbers go together", "M3.5 17a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0 -3 0M7.5 11a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0 -3 0M11.5 14a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0 -3 0M15.5 7a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0 -3 0M17.5 15a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0 -3 0M8.5 18a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0 -3 0"),
        new("spread", "Spread and timelines", "How values spread, or when things happen", "M4 18h3V9H4zM9 18h3V5H9zM14 18h3v-6h-3zM19 18h2v-3h-2z"),
        new("map", "On a map", "Values per country, or places as bubbles", "M3 6l6-2 6 2 6-2v14l-6 2-6-2-6 2z M9 4v14 M15 6v14"),
        new("single", "One number", "A single value against a scale or goal", "M4 15a8 8 0 0 1 16 0 M12 15l4-4"),
    ];

    private static ChartSlot Value(string label, string hint) => new("y", label, hint, Number, SlotPart.Aggregate);
    private static ChartSlot SplitBy(string label, string hint) => new("groupBy", label, hint, Text, Required: false);

    private static readonly string[] Common = ["titles", "sort", "legend", "lines"];

    public static readonly ChartKind[] All =
    [
        // ----- Change over time -----
        new("line", "trend", "Line", "line",
            [new("x", "Time", "Along the bottom. Group the rows per hour, day or month, or show every row.", AnyX, SlotPart.TimeBucket),
             Value("Value", "What the line shows; rows in the same period are combined."),
             SplitBy("A line for each", "One line per value of this field, e.g. per sales channel.")],
            ["titles", "axis", "look", "legend", "lines"],
            (c, s, d) => s.Type == SeriesType.Line),
        new("area", "trend", "Area", "area",
            [new("x", "Time", "Along the bottom. Group the rows per hour, day or month, or show every row.", AnyX, SlotPart.TimeBucket),
             Value("Value", "How high the area reaches."),
             SplitBy("An area for each", "One area per value of this field.")],
            ["titles", "axis", "look", "legend", "lines"],
            (c, s, d) => s.Type == SeriesType.Area && s.Stack is null),
        new("stacked-area", "trend", "Stacked area", "area",
            [new("x", "Time", "Along the bottom. Group the rows per hour, day or month.", AnyX, SlotPart.TimeBucket),
             Value("Value", "Each area's height; the areas add up to the total."),
             new("groupBy", "Stack an area for each", "The parts that add up, e.g. per sales channel.", Text)],
            ["titles", "axis", "look", "legend", "lines"],
            (c, s, d) => s.Type == SeriesType.Area && s.Stack is not null,
            (c, s) => s["stack"] = "stack"),
        new("columns-time", "trend", "Columns", "bar",
            [new("x", "Time", "One column per period: group the rows per day, week or month.", Time, SlotPart.TimeBucket),
             Value("Column height", "Rows in the same period are combined."),
             SplitBy("Split by", "Columns in a different color for each value.")],
            ["titles", "axis", "legend", "lines"],
            (c, s, d) => s.Type == SeriesType.Bar && !c.Horizontal && s.Stack is null),
        new("calendar", "trend", "Calendar", "calendar",
            [new("x", "Day", "One square per day, laid out like a calendar.", Time),
             new("y", "Color by", "How dark each day is; leave empty to count the rows per day.", Number, SlotPart.Aggregate, Required: false)],
            ["titles"],
            (c, s, d) => s.Type == SeriesType.Calendar),

        // ----- Compare -----
        new("bars", "compare", "Bars", "bar",
            [new("x", "Categories", "One bar for each value.", Category, SlotPart.TimeBucket),
             Value("Bar height", "Rows with the same category are combined."),
             SplitBy("Split by", "Bars in a different color for each value, side by side.")],
            ["titles", "sort", "axis", "legend", "lines"],
            (c, s, d) => s.Type == SeriesType.Bar && !c.Horizontal && s.Stack is null),
        new("horizontal-bars", "compare", "Horizontal bars", "bar",
            [new("x", "Categories", "One bar for each value, listed down the side. Good for long names and rankings.", Category, SlotPart.TimeBucket),
             Value("Bar length", "Rows with the same category are combined."),
             SplitBy("Split by", "Bars in a different color for each value.")],
            ["titles", "sort", "axis", "legend", "lines"],
            (c, s, d) => s.Type == SeriesType.Bar && c.Horizontal,
            (c, s) => c["horizontal"] = true),
        new("stacked-bars", "compare", "Stacked bars", "bar",
            [new("x", "Categories", "One bar for each value.", Category, SlotPart.TimeBucket),
             Value("Bar height", "Each part's size; the parts add up to the bar."),
             new("groupBy", "Stack by", "The parts of each bar, e.g. per sales channel.", Text)],
            ["titles", "sort", "axis", "legend", "lines"],
            (c, s, d) => s.Type == SeriesType.Bar && s.Stack is not null && c.Y.Stacking != StackMode.Percent,
            (c, s) => s["stack"] = "stack"),
        new("percent-bars", "compare", "100 % bars", "bar",
            [new("x", "Categories", "One bar for each value, each filled to 100 %.", Category, SlotPart.TimeBucket),
             Value("Share of", "Each part's share of its bar."),
             new("groupBy", "Stack by", "The parts of each bar.", Text)],
            ["titles", "sort", "legend"],
            (c, s, d) => s.Type == SeriesType.Bar && c.Y.Stacking == StackMode.Percent,
            (c, s) => { s["stack"] = "stack"; ((JsonObject)(c["y"] ??= new JsonObject()))["stacking"] = "percent"; }),
        new("dumbbell", "compare", "Before and after", "dumbbell",
            [new("x", "Categories", "One row for each value.", Names),
             new("from", "Before", "The first value, drawn as a hollow dot.", Number),
             new("y", "After", "The second value; the line between shows the change.", Number)],
            ["titles", "sort", "axis", "lines"],
            (c, s, d) => s.Type == SeriesType.Dumbbell),
        new("waterfall", "compare", "Waterfall", "waterfall",
            [new("x", "Steps", "One bar per row, in the order of the rows.", Category),
             new("y", "Change", "How much each step adds (or takes away).", Number),
             new("total", "Total rows", "A field that marks the rows showing the running total (true, yes or 1).", Category, Required: false)],
            ["titles", "axis", "lines"],
            (c, s, d) => s.Type == SeriesType.Waterfall),
        new("radar", "compare", "Radar", "radar",
            [new("x", "Spokes", "One spoke for each value, e.g. per skill or measure.", Names),
             Value("Value", "How far out each point is."),
             SplitBy("A shape for each", "One shape per value, to compare them.")],
            ["titles", "legend"],
            (c, s, d) => s.Type == SeriesType.Radar),

        // ----- Parts of a whole -----
        new("donut", "shares", "Donut", "pie",
            [new("x", "Slices", "One slice for each value.", Names),
             Value("Slice size", "Rows with the same slice are combined.")],
            ["titles", "slices", "legend"],
            (c, s, d) => s.Type == SeriesType.Pie && s.InnerRadius is > 0,
            (c, s) => s["innerRadius"] = 0.6),
        new("pie", "shares", "Pie", "pie",
            [new("x", "Slices", "One slice for each value.", Names),
             Value("Slice size", "Rows with the same slice are combined.")],
            ["titles", "slices", "legend"],
            (c, s, d) => s.Type == SeriesType.Pie),
        new("treemap", "shares", "Treemap", "treemap",
            [new("path", "Groups", "Fields from the largest group to the smallest, e.g. region, then country.", Text),
             Value("Size", "Each rectangle's area.")],
            ["titles"],
            (c, s, d) => s.Type == SeriesType.Treemap),
        new("funnel", "shares", "Funnel", "funnel",
            [new("x", "Stages", "One stage per value, in the order of the rows.", Names),
             Value("Count", "How many are left at each stage.")],
            ["titles", "sort"],
            (c, s, d) => s.Type == SeriesType.Funnel,
            // Largest first: stages that only get smaller, as a funnel should, whatever the order of the rows (months
            // become a ranking, not a funnel that widens). Data that is a funnel already keeps its order.
            (c, s) => s["sort"] ??= "descending"),

        // ----- Relation -----
        new("scatter", "relation", "Scatter", "scatter",
            [new("x", "Across", "A number along the bottom.", Number),
             new("y", "Up", "A number up the side; each row is a dot.", Number),
             SplitBy("Color by", "Dots in a different color for each value.")],
            ["titles", "axis", "legend"],
            (c, s, d) => s.Type == SeriesType.Scatter && s.Size is null),
        new("bubbles", "relation", "Bubbles", "scatter",
            [new("x", "Across", "A number along the bottom.", Number),
             new("y", "Up", "A number up the side.", Number),
             new("size", "Bubble size", "A third number: each bubble's area.", Number),
             SplitBy("Color by", "Bubbles in a different color for each value.")],
            ["titles", "axis", "legend"],
            (c, s, d) => s.Type == SeriesType.Scatter && s.Size is not null),
        new("heatmap", "relation", "Heatmap", "heatmap",
            [new("x", "Columns", "One column for each value, e.g. the hour.", AnyX),
             new("groupBy", "Rows", "One row for each value, e.g. the weekday.", Names),
             Value("Color by", "How dark each cell is.")],
            ["titles"],
            (c, s, d) => s.Type == SeriesType.Heatmap),

        // ----- Spread and timelines -----
        new("histogram", "spread", "Histogram", "histogram",
            [new("x", "Values", "A number; the bars count how many rows fall in each range.", Number)],
            ["titles", "bins", "axis"],
            (c, s, d) => s.Type == SeriesType.Histogram),
        new("boxplot", "spread", "Box plot", "boxPlot",
            [new("x", "Groups", "One box for each value.", Names),
             new("y", "Values", "A number; each box shows how its rows spread.", Number)],
            ["titles", "axis"],
            (c, s, d) => s.Type == SeriesType.BoxPlot),
        new("timeline", "spread", "Timeline", "rangeBar",
            [new("row", "Rows", "One row for each value, e.g. the machine or task.", Text),
             new("start", "Starts", "When each bar begins.", Time),
             new("end", "Ends", "When each bar ends.", Time)],
            ["titles"],
            (c, s, d) => s.Type == SeriesType.RangeBar),

        // ----- On a map -----
        new("country-map", "map", "Countries", "map",
            [new("x", "Country", "Country codes (SE, DE) or names.", Text, Needs: "field with country names or codes", Check: HoldsCountries),
             Value("Color by", "How dark each country is.")],
            ["titles"],
            (c, s, d) => s.Type == SeriesType.Map),
        new("bubble-map", "map", "Places", "mapBubble",
            [new("x", "Place", "The name shown in the tooltip.", Text),
             new("latitude", "Latitude", "Degrees north.", Number, NameHint: "lat", Needs: "latitude field"),
             new("longitude", "Longitude", "Degrees east.", Number, NameHint: "lon|lng", Needs: "longitude field"),
             Value("Bubble size", "Each bubble's area.")],
            ["titles"],
            (c, s, d) => s.Type == SeriesType.MapBubble),

        // ----- One number -----
        new("gauge", "single", "Gauge", "gauge",
            [Value("Value", "The rows are combined into one number.")],
            ["titles", "scale"],
            (c, s, d) => s.Type == SeriesType.Gauge),
        new("bullet", "single", "Against a target", "bullet",
            [new("x", "For each", "One bar per value, each against its own target.", Category, Required: false),
             Value("Value", "The bar's length."),
             new("targetField", "Target", "A field with each row's target; or set one target under Scale and target.", Number, Required: false)],
            ["titles", "scale"],
            (c, s, d) => s.Type == SeriesType.Bullet),
    ];

    /// <summary>Every property a kind's Apply sets; switching kind removes them first.</summary>
    private static readonly (OptionScope Scope, string Property)[] Variants =
        [(OptionScope.Chart, "horizontal"), (OptionScope.Series, "stack"), (OptionScope.YAxis, "stacking"), (OptionScope.Series, "innerRadius")];

    public static readonly EditorSection[] Sections =
    [
        new("titles", "Titles and labels",
        [
            new(OptionScope.Chart, "title", "Chart title", "Shown above the chart.", OptionControl.Text),
            new(OptionScope.Series, "name", "Series name", "In the legend and tooltip. Empty: the field's name.", OptionControl.Text),
            new(OptionScope.YAxis, "title", "Value axis title", "E.g. the unit: €, orders, ms.", OptionControl.Text),
            new(OptionScope.XAxis, "title", "Category axis title", "Usually not needed.", OptionControl.Text),
            new(OptionScope.Series, "format", "Number format", ".NET format: #,0 for 1,234, 0.0 for one decimal, P0 for percent, #,0 € for an amount.", OptionControl.Text),
            new(OptionScope.Series, "dataLabels", "Values on the chart", "Write each value next to its bar, point or slice.", OptionControl.Toggle),
        ]),
        new("sort", "Sort and filter",
        [
            new(OptionScope.Series, "sort", "Order", "Rank the categories by their value.", OptionControl.Choice,
                [("", "As in the data"), ("descending", "Largest first"), ("ascending", "Smallest first")]),
            new(OptionScope.Series, "limit", "Show only the first", "E.g. 10 for a top 10 (after sorting).", OptionControl.Number),
        ]),
        new("axis", "Axis",
        [
            new(OptionScope.YAxis, "min", "Lowest value", "Where the value axis starts. Empty: zero, or fitted to the data.", OptionControl.Number),
            new(OptionScope.YAxis, "max", "Highest value", "Where the value axis ends. Empty: fitted to the data.", OptionControl.Number),
            new(OptionScope.YAxis, "scale", "Scale", "Logarithmic suits values that span several powers of ten.", OptionControl.Choice,
                [("", "Linear"), ("log", "Logarithmic")]),
            new(OptionScope.YAxis, "format", "Axis number format", "How the axis labels are written, e.g. #,0 or 0 %.", OptionControl.Text),
        ]),
        new("look", "Line style",
        [
            new(OptionScope.Series, "curve", "Shape", "How the line goes from point to point.", OptionControl.Choice,
                [("", "Straight"), ("monotone", "Smooth"), ("step", "Steps")]),
            new(OptionScope.Series, "highlight", "Mark points", "Put a dot on the last, lowest or highest value.", OptionControl.Choice,
                [("", "None"), ("last", "Last value"), ("min,max", "Lowest and highest"), ("last,min,max", "Last, lowest and highest")]),
            new(OptionScope.Series, "color", "Color", "A CSS color such as #3b5fc7. Empty: the next color of the theme.", OptionControl.Text),
        ]),
        new("slices", "Slices",
        [
            new(OptionScope.Series, "groupSmallSlices", "Group small slices", "Slices under this share become one \"Other\" slice, e.g. 0.03 for 3 %.", OptionControl.Number),
            new(OptionScope.Series, "dataLabels", "Labels", "Write each slice's value next to it.", OptionControl.Choice,
                [("", "Automatic"), ("true", "Show"), ("false", "Hide")]),
        ]),
        new("bins", "Bins",
        [
            new(OptionScope.Series, "binWidth", "Bin width", "The range each bar covers, e.g. 5. Empty: a nice width from the data.", OptionControl.Number),
            new(OptionScope.Series, "binCount", "About how many bins", "Used when no width is set.", OptionControl.Number),
        ]),
        new("scale", "Scale and target",
        [
            new(OptionScope.Series, "min", "Start of the scale", "Empty: 0.", OptionControl.Number),
            new(OptionScope.Series, "max", "End of the scale", "E.g. 100 for percent. Empty: a nice value above the data.", OptionControl.Number),
            new(OptionScope.Series, "target", "Target", "A goal, marked on the scale.", OptionControl.Number),
            new(OptionScope.Series, "format", "Number format", "E.g. 0 % or #,0 €.", OptionControl.Text),
        ]),
        new("legend", "Legend",
        [
            new(OptionScope.Legend, "mode", "Show the legend", "Automatic: only when there is more than one series.", OptionControl.Choice,
                [("", "Automatic"), ("show", "Always"), ("hide", "Never")]),
            new(OptionScope.Legend, "position", "Where", "", OptionControl.Choice,
                [("", "Top"), ("bottom", "Bottom"), ("right", "Right")]),
        ]),
    ];

    public static ChartGoal Goal(string id) => Goals.First(g => g.Id == id);

    public static EditorSection Section(string id) => Sections.First(s => s.Id == id);

    /// <summary>The kinds offered under a goal.</summary>
    public static IEnumerable<ChartKind> Of(string goal) => All.Where(k => k.Goal == goal);

    /// <summary>
    /// The kind a series is, from the definition as read (synonyms such as column or donut resolved); null when none
    /// fits. Some charts fit more than one goal (bars over time are Columns under Change over time and Bars under
    /// Compare): the one in <paramref name="goal"/>, the goal the user picked, wins.
    /// </summary>
    public static ChartKind? Find(ChartDefinition? definition, int series, ChartDataset? dataset, string? goal = null)
    {
        if (definition is null || series >= definition.Series.Count)
        {
            return null;
        }

        var s = definition.Series[series];
        return All.Where(k => k.Is(definition, s, dataset)).OrderBy(k => k.Goal == goal ? 0 : 1).FirstOrDefault();
    }

    /// <summary>
    /// Makes a series this kind: sets its type and variant options, drops the fields of slots the new kind does
    /// not have, and fills empty required slots with the first field of a fitting type, so the chart draws at once
    /// (unless <paramref name="fill"/> is false: a suggestion has placed its fields already).
    /// </summary>
    public static void Apply(ChartKind kind, ChartKind? previous, JsonObject chart, JsonObject series, ChartDataset? dataset, bool fill = true)
    {
        foreach (var (scope, property) in Variants)
        {
            Target(chart, series, scope, create: false)?.Remove(property);
        }

        if (previous is not null)
        {
            foreach (var slot in previous.Slots.Where(p => kind.Slots.All(n => n.Property != p.Property)))
            {
                series.Remove(slot.Property);
                if (slot.Part == SlotPart.TimeBucket)
                {
                    series.Remove("timeBucket");
                }
            }
        }

        series["type"] = kind.Type;
        // Ranking only where the chart shows the Sort section: a line or a calendar sorted by value is out of order.
        if (!kind.Sections.Contains("sort"))
        {
            series.Remove("sort");
        }

        kind.Apply?.Invoke(chart, series);

        // Combining rows only where a slot says how (a scatter shows every row).
        if (kind.Slots.All(s => s.Part != SlotPart.Aggregate))
        {
            series.Remove("aggregate");
        }

        if (dataset is null)
        {
            return;
        }

        // A field the new chart cannot use (a time field as a histogram's values) is replaced below.
        foreach (var slot in kind.Slots.Where(s => !s.IsList && series[s.Property]?.ToString() is { } f && dataset.Field(f) is { } field && !s.Accepts.Contains(field.Type)))
        {
            series.Remove(slot.Property);
        }

        if (fill)
        {
            Fill(kind, series, dataset);
        }

        // Time grouping only where the chart has a time axis that groups.
        var timeX = kind.Slots.Any(s => s.Property == "x" && s.Part == SlotPart.TimeBucket) && series["x"]?.ToString() is { } x
            && dataset.Field(x)?.Type == FieldType.Time;
        if (!timeX)
        {
            series.Remove("timeBucket");
        }
        else if (series["timeBucket"] is null)
        {
            series["timeBucket"] = "auto";
        }
    }

    private static void Fill(ChartKind kind, JsonObject series, ChartDataset dataset)
    {
        var used = new HashSet<string>(kind.Slots.Select(s => series[s.Property]?.ToString()).OfType<string>());
        var empty = kind.Slots.Where(s => s.Required && series[s.Property] is null).ToArray();
        // All the empty slots at once when the fields left allow (a line's time slot must not take the only number).
        // When the fields kept stand in the way (the previous chart's value took the latitude a map of places
        // needs), all the required slots afresh, and optional ones lose a field a required one now uses. Otherwise
        // each slot the first fitting field left, so the chart fills in what it can.
        var fill = Assign(empty, dataset, used);
        if (fill is null && Assign([.. kind.Slots.Where(s => s.Required)], dataset, new HashSet<string>()) is { } fresh)
        {
            fill = fresh;
            var taken = fresh.Select(f => f.Field.Name).ToHashSet();
            foreach (var slot in kind.Slots.Where(s => !s.Required && series[s.Property]?.ToString() is { } f && taken.Contains(f)))
            {
                series.Remove(slot.Property);
            }
        }

        foreach (var (slot, field) in fill ?? FillEach(empty, dataset, used))
        {
            series[slot.Property] = slot.IsList ? new JsonArray(field.Name) : field.Name;
            if (slot.Part == SlotPart.Aggregate && series["aggregate"] is null)
            {
                series["aggregate"] = "sum";
            }
        }
    }

    // ----- Data first: the charts a set of picked fields can make -----

    /// <summary>The slot properties of every kind: what a suggestion clears before it places its fields.</summary>
    private static readonly string[] SlotProperties = [.. All.SelectMany(k => k.Slots).Select(s => s.Property).Distinct()];

    /// <summary>
    /// The fields a series shows, in slot order (x, y, groupBy...), from the dataset: the fields a user picked, as
    /// far as the editor can tell.
    /// </summary>
    public static IReadOnlyList<DataField> FieldsOf(JsonObject? series, ChartDataset? dataset)
    {
        if (series is null || dataset is null)
        {
            return [];
        }

        var names = SlotProperties.SelectMany(p => series[p] switch
        {
            JsonArray list => list.Select(n => n?.ToString()),
            JsonValue value => [value.ToString()],
            _ => [],
        });
        return [.. names.OfType<string>().Distinct(StringComparer.Ordinal).Select(dataset.Field).OfType<DataField>()];
    }

    /// <summary>
    /// The charts that show exactly the picked fields: each field in a slot of its type, every required slot
    /// filled. A value slot may count the rows instead (orders per channel with only the channel picked). Best
    /// fit first: fields of the type a slot takes first (a time along a line before a time as bar categories),
    /// then the editor's order of charts.
    /// </summary>
    public static IReadOnlyList<ChartSuggestion> Suggest(IReadOnlyList<DataField> picked)
    {
        if (picked.Count == 0)
        {
            return [];
        }

        var found = new List<(ChartSuggestion Suggestion, int Cost, int Order)>();
        for (var i = 0; i < All.Length; i++)
        {
            if (Place(All[i], picked) is { } best)
            {
                found.Add((new ChartSuggestion(All[i], best.Placed, best.Counts), best.Cost, i));
            }
        }

        return [.. found.OrderBy(f => f.Cost).ThenBy(f => Specialist.Contains(f.Suggestion.Kind.Id)).ThenBy(f => f.Order).Select(f => f.Suggestion)];
    }

    // Charts for a particular story (a funnel, a running total, a radar): after the everyday ones that fit as well,
    // so channel and revenue suggest bars and a donut before a waterfall.
    private static readonly HashSet<string> Specialist = ["waterfall", "radar", "funnel", "dumbbell", "percent-bars", "bullet", "boxplot", "treemap"];

    // The cheapest way to put every picked field in its own slot of the kind; null when there is none.
    private static (List<(ChartSlot Slot, DataField Field)> Placed, bool Counts, int Cost)? Place(ChartKind kind, IReadOnlyList<DataField> picked)
    {
        if (picked.Count > kind.Slots.Length)
        {
            return null;
        }

        (List<(ChartSlot, DataField)> Placed, bool Counts, int Cost)? best = null;
        var placed = new List<(ChartSlot Slot, DataField Field)>();
        // A picked number is the value: counting rows instead would show it along an axis (a line of row counts by revenue).
        var numberPicked = picked.Any(f => f.Type == FieldType.Number);
        Next(0, 0);
        return best;

        void Next(int i, int cost)
        {
            if (best is { } b && cost >= b.Cost)
            {
                return;
            }

            if (i == picked.Count)
            {
                // Required slots left empty: only a value slot, which then counts the rows (once, and at a price,
                // so a picked number wins).
                var open = kind.Slots.Where(s => s.Required && placed.All(p => p.Slot != s)).ToList();
                if (open.Count == 0)
                {
                    best = ([.. placed], false, cost);
                }
                else if (open is [{ Part: SlotPart.Aggregate }] && !numberPicked && cost + 2 < (best?.Cost ?? int.MaxValue))
                {
                    best = ([.. placed], true, cost + 2);
                }

                return;
            }

            var field = picked[i];
            foreach (var slot in kind.Slots.Where(s => s.Fits(field) && placed.All(p => p.Slot != s)))
            {
                placed.Add((slot, field));
                Next(i + 1, cost + Array.IndexOf(slot.Accepts, field.Type));
                placed.RemoveAt(placed.Count - 1);
            }
        }
    }

    /// <summary>Makes a series the suggested chart with its fields in their slots; the series' other options stay.</summary>
    public static void Use(ChartSuggestion suggestion, ChartKind? previous, JsonObject chart, JsonObject series, ChartDataset? dataset)
    {
        foreach (var property in SlotProperties)
        {
            series.Remove(property);
        }

        // How rows combine stays as the user chose it (an average stays an average), except a count, which only
        // stands for a value that was not picked.
        var aggregate = series["aggregate"]?.ToString() is { Length: > 0 } a && a != "count" ? a : "sum";
        series.Remove("aggregate");
        foreach (var (slot, field) in suggestion.Placed)
        {
            series[slot.Property] = slot.IsList ? new JsonArray(field.Name) : field.Name;
            if (slot.Part == SlotPart.Aggregate)
            {
                series["aggregate"] = aggregate;
            }
        }

        if (suggestion.Counts)
        {
            series["aggregate"] = "count";
        }

        Apply(suggestion.Kind, previous, chart, series, dataset, fill: false);
    }

    /// <summary>
    /// What a dataset lacks for a kind, in a user's words ("a text field and two time fields"); null when its fields
    /// can fill every required slot, each field once. Null without a dataset: there is nothing to judge.
    /// </summary>
    public static string? Missing(ChartKind kind, ChartDataset? dataset)
    {
        if (dataset is null)
        {
            return null;
        }

        var cache = Fit.GetOrCreateValue(dataset);
        lock (cache)
        {
            if (!cache.TryGetValue(kind.Id, out var missing))
            {
                cache[kind.Id] = missing = Lacking([.. kind.Slots.Where(s => s.Required)], dataset);
            }

            return missing;
        }
    }

    // What is lacking is what the most slots the fields can fill leave over: a timeline on data with a text field
    // and one time field needs "a time field", not all three again.
    private static string? Lacking(ChartSlot[] required, ChartDataset dataset)
    {
        if (Assign(required, dataset, new HashSet<string>()) is not null)
        {
            return null;
        }

        for (var size = required.Length - 1; size > 0; size--)
        {
            foreach (var kept in Combinations(required, size))
            {
                if (Assign(kept, dataset, new HashSet<string>()) is not null)
                {
                    return Describe(required.Except(kept), kept);
                }
            }
        }

        return Describe(required, []);
    }

    private static IEnumerable<ChartSlot[]> Combinations(ChartSlot[] slots, int size, int start = 0)
    {
        if (size == 0)
        {
            yield return [];
            yield break;
        }

        for (var i = start; i <= slots.Length - size; i++)
        {
            foreach (var rest in Combinations(slots, size - 1, i + 1))
            {
                yield return [slots[i], .. rest];
            }
        }
    }

    // Per dataset (a loaded one lives as long as the page), so a render only looks up what fits.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ChartDataset, Dictionary<string, string?>> Fit = new();

    /// <summary>A field for every slot, each field once and none in <paramref name="used"/>; earlier fields first; null when none.</summary>
    private static List<(ChartSlot Slot, DataField Field)>? Assign(IReadOnlyList<ChartSlot> slots, ChartDataset dataset, ISet<string> used)
    {
        var taken = new HashSet<string>(used, StringComparer.Ordinal);
        var result = new List<(ChartSlot, DataField)>();
        return Next(0) ? result : null;

        bool Next(int i)
        {
            if (i == slots.Count)
            {
                return true;
            }

            foreach (var field in dataset.Fields.Where(f => slots[i].Fits(f) && !taken.Contains(f.Name)))
            {
                taken.Add(field.Name);
                result.Add((slots[i], field));
                if (Next(i + 1))
                {
                    return true;
                }

                taken.Remove(field.Name);
                result.RemoveAt(result.Count - 1);
            }

            return false;
        }
    }

    private static List<(ChartSlot Slot, DataField Field)> FillEach(IEnumerable<ChartSlot> slots, ChartDataset dataset, ISet<string> used)
    {
        var taken = new HashSet<string>(used, StringComparer.Ordinal);
        var result = new List<(ChartSlot, DataField)>();
        foreach (var slot in slots)
        {
            if (dataset.Fields.FirstOrDefault(f => slot.Fits(f) && !taken.Contains(f.Name)) is { } field)
            {
                taken.Add(field.Name);
                result.Add((slot, field));
            }
        }

        return result;
    }

    // "a text field and two time fields": what the slots left over take, counted, specific needs first. "Another"
    // and "more" where the data has some of that kind already (a scatter on data with one number).
    private static string Describe(IEnumerable<ChartSlot> slots, IEnumerable<ChartSlot> filled)
    {
        string[] counts = ["", "a", "two", "three", "four"];
        var some = filled.Select(s => s.Need).ToHashSet();
        var parts = slots.GroupBy(s => s.Need).OrderBy(g => g.Key == "field" ? 1 : 0)
            .Select(g => (g.Key, Count: g.Count(), More: g.Key == "field" || some.Contains(g.Key)) switch
            {
                (_, 1, true) => $"another {g.Key}",
                (_, var n, true) => $"{counts[n]} more {g.Key.Replace("field", "fields")}",
                (_, 1, false) => $"{(g.Key[0] is 'a' or 'e' or 'i' or 'o' or 'u' ? "an" : "a")} {g.Key}",
                (_, var n, false) => $"{counts[n]} {g.Key.Replace("field", "fields")}",
            })
            .ToList();
        return parts.Count == 1 ? parts[0] : string.Join(", ", parts[..^1]) + " and " + parts[^1];
    }

    // A text field holds countries when most of its values (a sample) are country names or codes the map knows.
    private static bool HoldsCountries(DataField field)
    {
        var values = field.Values.OfType<string>().Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(200).ToList();
        return values.Count > 0 && values.Count(v => RioCharts.Core.Geo.WorldMap.Find(v) is not null) * 2 >= values.Count;
    }

    /// <summary>Makes a series a chart it has been before: its fields as they were then, and the chart's variant options.</summary>
    public static void Restore(ChartKind kind, JsonObject chart, JsonObject series, JsonObject earlier)
    {
        foreach (var (scope, property) in Variants)
        {
            Target(chart, series, scope, create: false)?.Remove(property);
        }

        // The chart's own variant options (horizontal, percent stacking) from the kind; the series' (a stack name,
        // an inner radius) as they were.
        kind.Apply?.Invoke(chart, series);
        series.Clear();
        foreach (var (name, value) in earlier)
        {
            series[name] = value?.DeepClone();
        }

        series["type"] = kind.Type;
    }

    /// <summary>The JSON object an option lives in; with <paramref name="create"/> a missing axis or legend object is added.</summary>
    public static JsonObject? Target(JsonObject chart, JsonObject? series, OptionScope scope, bool create)
    {
        return scope switch
        {
            OptionScope.Chart => chart,
            OptionScope.Series => series,
            OptionScope.XAxis => DefinitionProperties.Object(chart, "x", create),
            OptionScope.YAxis => DefinitionProperties.Object(chart, "y", create),
            OptionScope.Legend => DefinitionProperties.Object(chart, "legend", create),
            _ => null,
        };
    }

    /// <summary>Fields of the dataset a slot accepts, best fit first; the slot's current field is kept even when it does not fit.</summary>
    public static IEnumerable<DataField> FieldsFor(ChartSlot slot, ChartDataset dataset) =>
        dataset.Fields.Where(f => slot.Accepts.Contains(f.Type));

    // Words the parser reads as another (synonyms), so a choice shows the right label: "desc" is Largest first.
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["desc"] = "descending", ["asc"] = "ascending", ["smooth"] = "monotone", ["spline"] = "monotone", ["straight"] = "linear",
    };

    /// <summary>The word a choice is listed under, for a value written with a synonym.</summary>
    public static string Canonical(string value) => Synonyms.GetValueOrDefault(value, value);

    public static readonly (string Value, string Label)[] Aggregates =
    [
        ("", "Value of"), ("sum", "Sum of"), ("average", "Average of"), ("min", "Lowest"), ("max", "Highest"), ("last", "Last"), ("count", "Count rows"),
    ];

    public static readonly (string Value, string Label)[] TimeBuckets =
    [
        ("", "every row"), ("auto", "grouped automatically"), ("minute", "per minute"), ("hour", "per hour"), ("day", "per day"),
        ("week", "per week"), ("month", "per month"), ("quarter", "per quarter"), ("year", "per year"),
    ];
}
