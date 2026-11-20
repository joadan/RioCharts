using System.Globalization;
using System.Text.Json.Nodes;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Specs;

namespace RioCharts.Tests;

public class Milestone6Tests
{
    private static readonly ChartDataset Ahu = ChartDataset.FromRows("ahu",
        Enumerable.Range(0, 48).Select(h => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["timestamp"] = new DateTime(2026, 11, 16).AddHours(h),
            ["unit"] = h % 2 == 0 ? "AHU 1" : "AHU 2",
            ["supplyTemp"] = 19 + h % 5,
            ["energy"] = 10.0 + h,
        }),
        "Air handling units, hourly");

    private static readonly Dictionary<string, ChartDataset> Datasets = new() { ["ahu"] = Ahu };

    private static SpecBindResult Bind(string json)
    {
        var parsed = ChartSpecJson.Parse(json);
        Assert.True(parsed.Success, string.Join("; ", parsed.Errors));
        return ChartSpecBinder.Bind(parsed.Spec!, Datasets);
    }

    // ---------- Parsing ----------

    [Fact]
    public void The_minimal_spec_from_the_design_renders_a_finished_chart()
    {
        var result = Bind("""{ "dataset": "ahu", "series": [{ "type": "line", "x": "timestamp", "y": "supplyTemp" }] }""");

        Assert.True(result.Success);
        var series = Assert.Single(result.Spec.Series);
        Assert.Equal("Supply temp", series.Name);
        Assert.Equal(48, result.Data[series.Id].Count);
        var layout = ChartLayout.Compute(result.Spec, result.Data, new LayoutOptions { Culture = CultureInfo.InvariantCulture });
        Assert.True(layout.HasData);
        Assert.Equal(XKind.Time, layout.XKind);
    }

    [Fact]
    public void Parsing_is_tolerant_of_case_synonyms_unknown_fields_and_shapes()
    {
        var parsed = ChartSpecJson.Parse("""
            {
              "Dataset": "ahu", "comment": "ignored", // comments too
              "series": { "TYPE": "Doughnut", "x": "unit", "y": "energy", "aggregate": "total" },
              "legend": { "position": "RIGHT" },
            }
            """);

        Assert.True(parsed.Success, string.Join("; ", parsed.Errors));
        var s = Assert.Single(parsed.Spec!.Series);
        Assert.Equal(SeriesType.Pie, s.Type);
        Assert.Equal(0.6, s.InnerRadius);
        Assert.Equal(Aggregate.Sum, s.Aggregate);
        Assert.Equal(LegendPosition.Right, parsed.Spec.Legend.Position);
        Assert.Contains(parsed.Warnings, w => w.Contains("doughnut", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("""{ "series": [{ "type": "scatter", "x": "a", "y": "b" }] }""", "series[0].type", "valid: line, area, bar")]
    [InlineData("""{ "series": [{ "x": "a", "y": "b", "timeBucket": "fortnight" }] }""", "series[0].timeBucket", "hour")]
    [InlineData("""{ "legend": { "mode": "sometimes" } }""", "legend.mode", "auto, show, hide")]
    [InlineData("""[1, 2]""", "$", "a spec is a JSON object")]
    [InlineData("""{ "series": [ """, "$", "not valid JSON")]
    public void Invalid_specs_get_actionable_errors(string json, string path, string hint)
    {
        var parsed = ChartSpecJson.Parse(json);

        Assert.False(parsed.Success);
        var error = Assert.Single(parsed.Errors);
        Assert.Equal(path, error.Path);
        Assert.Contains(hint, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Too_many_series_or_inline_rows_are_rejected()
    {
        var series = string.Join(",", Enumerable.Range(0, 25).Select(i => $$"""{"x":"a","y":"b{{i}}"}"""));
        var rows = string.Join(",", Enumerable.Range(0, 1001).Select(i => $$"""{"a":{{i}}}"""));

        Assert.Contains(ChartSpecJson.Parse($$"""{"series":[{{series}}]}""").Errors, e => e.Path == "series");
        Assert.Contains(ChartSpecJson.Parse($$"""{"data":[{{rows}}],"series":[{"x":"a","y":"a"}]}""").Errors, e => e.Path == "data");
    }

    [Fact]
    public void Specs_round_trip_compactly()
    {
        var spec = new ChartSpec
        {
            Title = "Energy",
            Dataset = "ahu",
            Series = [new SeriesSpec { Type = SeriesType.Bar, X = "timestamp", Y = "energy", TimeBucket = TimeBucket.Day, Aggregate = Aggregate.Sum }],
        };

        var json = ChartSpecJson.Serialize(spec);
        var back = ChartSpecJson.Parse(json).Spec!;

        Assert.DoesNotContain("legend", json); // defaults are left out
        Assert.DoesNotContain("yAxes", json);
        Assert.Contains("\"type\": \"bar\"", json);
        Assert.Contains("\"timeBucket\": \"day\"", json);
        Assert.Equal(spec.Series[0], back.Series[0]);
        Assert.Equal(spec.Title, back.Title);
    }

    [Fact]
    public void Schema_describes_every_property_with_enums_as_strings()
    {
        var schema = ChartSpecJson.Schema();
        var series = schema["properties"]!["series"]!;
        var item = series["items"]!;
        var type = item["properties"]!["type"]!;

        Assert.Equal("RioCharts chart spec", (string?)schema["title"]);
        Assert.NotNull(series["description"]);
        Assert.Contains("auto", type["enum"]!.AsArray().Select(n => (string?)n));
        Assert.NotNull(type["description"]);
        Assert.NotNull(item["properties"]!["timeBucket"]!["description"]);
    }

    // ---------- Binding ----------

    [Fact]
    public void Unknown_fields_and_datasets_list_what_is_available()
    {
        var parsed = ChartSpecJson.Parse("""{ "dataset": "ahu", "series": [{ "x": "timestamp", "y": "supplyTemps" }, { "dataset": "nope", "x": "a", "y": "b" }] }""");
        var result = ChartSpecBinder.Bind(parsed.Spec!, Datasets);

        Assert.False(result.Success);
        var field = result.Errors.Single(e => e.Path == "series[0].y");
        Assert.Contains("did you mean 'supplyTemp'", field.Message);
        Assert.Contains("timestamp (time)", field.Message);
        Assert.Contains(result.Errors, e => e.Path == "series[1].dataset" && e.Message.Contains("available: ahu", StringComparison.Ordinal));
    }

    [Fact]
    public void Y_must_be_numeric()
    {
        var parsed = ChartSpecJson.Parse("""{ "dataset": "ahu", "series": [{ "x": "timestamp", "y": "unit" }] }""");

        var error = Assert.Single(ChartSpecBinder.Bind(parsed.Spec!, Datasets).Errors);

        Assert.Equal("series[0].y", error.Path);
        Assert.Contains("number fields: supplyTemp, energy", error.Message);
    }

    [Fact]
    public void Group_by_filter_bucket_and_aggregate_build_one_series_per_value()
    {
        var result = Bind("""
            { "dataset": "ahu", "series": [{ "type": "bar", "x": "timestamp", "y": "energy", "groupBy": "unit",
              "timeBucket": "day", "aggregate": "sum", "filter": [{ "field": "energy", "op": ">=", "value": 20 }] }] }
            """);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(["AHU 1", "AHU 2"], result.Spec.Series.Select(s => s.Name));
        var first = result.Data[result.Spec.Series[0].Id];
        Assert.Equal(2, first.Count); // two days
        // AHU 1 has even hours; energy >= 20 keeps hours 10..46 → day 1: 10+12+...+22, day 2: 24+26+...+46 (+10 each)
        Assert.Equal(Enumerable.Range(10, 14).Where(h => h % 2 == 0).Sum(h => 10.0 + h), first.Y[0]);
    }

    [Fact]
    public void Inline_data_and_inferred_fields_work_without_a_data_source()
    {
        var result = Bind("""
            { "data": [ { "system": "Heating", "share": 45 }, { "system": "Ventilation", "share": 30 }, { "system": "Lighting", "share": 25 } ],
              "series": [{ "type": "auto" }] }
            """);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var s = Assert.Single(result.Spec.Series);
        Assert.Equal(SeriesType.Pie, s.Type); // shares of 100: auto picks a donut
        Assert.Equal(0.6, s.InnerRadius);
        Assert.Contains(result.Warnings, w => w.StartsWith("series[0].x", StringComparison.Ordinal));
    }

    [Fact]
    public void Auto_picks_line_for_time_and_bar_for_categories_and_sort_limit_rank_them()
    {
        var line = Bind("""{ "dataset": "ahu", "series": [{ "type": "auto", "x": "timestamp", "y": "supplyTemp" }] }""");
        var ranked = Bind("""{ "dataset": "ahu", "series": [{ "type": "auto", "x": "unit", "y": "energy", "aggregate": "max", "sort": "desc", "limit": 1 }] }""");

        Assert.Equal(SeriesType.Line, line.Spec.Series[0].Type);
        Assert.Equal(SeriesType.Bar, ranked.Spec.Series[0].Type);
        var data = ranked.Data[ranked.Spec.Series[0].Id];
        Assert.Equal(["AHU 2"], data.Categories);
        Assert.Equal(57, data.Y[0]);
    }

    [Fact]
    public async Task Binding_loads_datasets_from_a_data_source()
    {
        var source = new InMemoryDataSource([Ahu]);
        var spec = ChartSpecJson.Parse("""{ "dataset": "AHU", "series": [{ "x": "timestamp", "y": "energy", "aggregate": "count", "timeBucket": "day" }] }""").Spec!;

        var result = await ChartSpecBinder.BindAsync(spec, source);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal([24.0, 24.0], result.Data[result.Spec.Series[0].Id].Y);
    }

    [Fact]
    public void Datasets_infer_field_types_from_items_and_json()
    {
        var items = ChartDataset.FromItems("items", new[] { new { Time = new DateTime(2026, 1, 1), Name = "A", Value = 1.5m } });
        var json = ChartDataset.FromJson("json", System.Text.Json.JsonDocument.Parse("""[{"t":"2026-01-01T10:00:00+01:00","v":"3","n":2}]""").RootElement);

        Assert.Equal([FieldType.Time, FieldType.Text, FieldType.Number], items.Fields.Select(f => f.Type));
        Assert.Equal(["time", "name", "value"], items.Fields.Select(f => f.Name));
        Assert.Equal(FieldType.Time, json.Field("t")!.Type);
        Assert.IsType<DateTimeOffset>(json.Field("t")!.Values[0]);
        Assert.Equal(FieldType.Text, json.Field("v")!.Type); // a quoted number stays text
        Assert.Equal(FieldType.Number, json.Field("N")!.Type); // case-insensitive lookup
    }
}
