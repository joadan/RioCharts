using System.Globalization;
using RioCharts.AI;
using RioCharts.Core;
using RioCharts.Core.Layout;
using RioCharts.Core.Specs;
using RioCharts.Skia;

// Runs the code paths that use JSON, reflection or native code, and checks the results. Exit code 0: all passed.
var failures = new List<string>();

void Check(bool ok, string what)
{
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
    if (!ok)
    {
        failures.Add(what);
    }
}

var start = new DateTime(2026, 11, 16);
var readings = Enumerable.Range(0, 48).Select(i => new Reading(start.AddHours(i), 19 + Math.Sin(i / 4.0), 22 + Math.Cos(i / 5.0))).ToList();
var weather = ChartDataset.FromItems("weather", readings, "Weather stations, hourly");
Check(weather.Fields.Count == 3, "dataset from objects (reflection over public properties)");

// JSON spec: tolerant reading, enums, defaults of properties missing from the JSON.
var parsed = ChartSpecJson.Parse("""
    {
      "title": "London and Toronto",
      "dataset": "weather",
      "series": [
        { "type": "spline", "x": "time", "y": "london", "format": "0.0 °C", "highlight": "last, max" },
        { "type": "line", "x": "time", "y": "toronto", "format": "0.0 °C" }
      ],
      "annotations": [ { "type": "band", "from": 21.5, "to": 22.5, "label": "Normal" } ],
      "tooltip": { "shared": false }
    }
    """);
Check(parsed.Success, "parse a JSON spec");
var spec = parsed.Spec!;
Check(spec.Series[0].Curve == CurveType.Monotone && spec.Series[0].Highlight == (PointHighlight.Last | PointHighlight.Max), "enums and synonyms");
Check(spec.Annotations[0].Axis == AnnotationAxis.Y && spec.Tooltip.Enabled && !spec.Tooltip.Shared && spec.Y is not null, "defaults kept for missing properties");

var json = ChartSpecJson.Serialize(spec);
Check(json.Contains("\"monotone\"", StringComparison.Ordinal) && !json.Contains("\"legend\"", StringComparison.Ordinal), "serialize compactly with camelCase enums");
Check(ChartSpecJson.Parse(json).Spec is { } again && again.Series.Count == 2, "round trip");

var schema = ChartSpecJson.Schema().ToJsonString();
Check(schema.Contains("\"description\"", StringComparison.Ordinal) && schema.Contains("\"series\"", StringComparison.Ordinal), "JSON schema with descriptions");

// Binding, layout, texts, Skia rendering.
var bound = ChartSpecBinder.Bind(spec, new Dictionary<string, ChartDataset> { ["weather"] = weather });
Check(bound.Errors.Count == 0 && bound.Data.Count == 2, "bind fields to the dataset");

var layout = ChartLayout.Compute(bound.Spec, bound.Data, new LayoutOptions { Culture = CultureInfo.GetCultureInfo("sv-SE") });
Check(layout.Scene.Description.StartsWith("Diagram \"London and Toronto\" med 2 serier.", StringComparison.Ordinal), "layout with Swedish texts");

var svg = ChartExport.ToSvg(bound.Spec, bound.Data, new ExportOptions { Width = 600, Height = 300 });
Check(svg.StartsWith("<svg", StringComparison.Ordinal) && svg.Contains("London and Toronto", StringComparison.Ordinal), "SVG export");

var png = ChartExport.ToPng(bound.Spec, bound.Data, new ExportOptions { Width = 600, Height = 300, Scale = 1 });
Check(png.Length > 1000 && png[1] == (byte)'P' && png[2] == (byte)'N' && png[3] == (byte)'G', "PNG export (SkiaSharp native code)");

// The AI tool: validation, rendering, result JSON.
var tool = new ChartTool(new InMemoryDataSource([weather]));
var result = await tool.CreateChartAsync("""{ "dataset": "weather", "series": [ { "type": "line", "x": "time", "y": "london" } ] }""");
Check(result.Success && result.Png is { Length: > 0 }, "create_chart tool");
var contents = ChartTool.ToAIContents(result);
Check(contents.Count == 2, "tool result as AI content (source-generated result JSON)");
var bad = await tool.CreateChartAsync("""{ "dataset": "weather", "series": [ { "type": "line", "x": "time", "y": "londn" } ] }""");
Check(!bad.Success && bad.Errors.Any(e => e.Contains("london", StringComparison.Ordinal)), "actionable errors for a wrong field");

Console.WriteLine(failures.Count == 0 ? "All checks passed." : $"{failures.Count} checks failed.");
return failures.Count == 0 ? 0 : 1;

internal sealed record Reading(DateTime Time, double London, double Toronto);
