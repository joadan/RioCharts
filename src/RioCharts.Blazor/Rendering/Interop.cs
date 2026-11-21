using System.Text.Json;
using System.Text.Json.Serialization;
using RioCharts.Core.Layout;

namespace RioCharts.Blazor.Rendering;

// What the chart sends to its JS module. Payloads go as UTF-8 JSON bytes, serialized with
// source-generated metadata: Blazor's own interop JSON uses reflection, which breaks in trimmed
// apps (the trimmer removes the constructor parameter names of anonymous types) and native AOT.
// Property names are camelCase, matching what RioChart.razor.js reads.

/// <summary>The interaction model for <c>update</c>: tooltip data, zoom, drags, keyboard.</summary>
internal sealed record UpdateModel(
    IReadOnlyList<DragModel> Drags,
    PlotModel Plot,
    bool Tooltip,
    bool Template,
    bool Shared,
    float Band,
    bool Click,
    bool Keys,
    string? Pos,
    bool Zoom,
    bool Zoomed,
    string? Group,
    ScaleModel? Xscale,
    IReadOnlyList<SeriesModel> Series,
    IReadOnlyList<SliceModel> Slices,
    InsetsModel? Need = null,
    InsetsModel? At = null,
    string? Select = null,
    bool Sel = false);

/// <summary>Plot insets: what the chart's labels need, or the group minimum it was laid out with.</summary>
internal sealed record InsetsModel(double L, double R);

internal sealed record DragModel(string Id, bool X, double Pos, ScaleModel S);

internal sealed record PlotModel(float X, float Y, float W, float H);

internal sealed record ScaleModel(double D0, double D1, double R0, double R1);

internal sealed record SeriesModel(
    string Id, string Name, string Color, bool Bar, bool Step, float Snap, float BarWidth, float BarOffset,
    float[] X, float[] Y, int[] Index, string[] Xl, string[] V, bool Dots = false, float Ch = 0, float[]? Cw = null,
    string[]? Pl = null, string[][]? Pv = null);

internal sealed record SliceModel(
    string Id, int Index, string Header, string Label, string Value, string Color,
    float Cx, float Cy, float R0, float R1, float A0, float A1, float End, string[]? Pl = null, string[]? Pv = null);

/// <summary>The canvas layer's draw operations for <c>paint</c>.</summary>
internal sealed record CanvasModel(float[]? Clip, IReadOnlyList<CanvasOp> Ops);

internal sealed record CanvasOp(CanvasShape Shape, string? Fill, double? Fo, string? Stroke, double Sw, string? Dash, double? Op);

/// <summary>A path (t = "p", d) or a rectangle (t = "r", x, y, w, h, rx).</summary>
internal sealed record CanvasShape(string T, string? D = null, float? X = null, float? Y = null, float? W = null, float? H = null, float? Rx = null);

/// <summary>Options for image export (<c>download</c>, <c>exportSvg</c>, <c>exportPng</c>).</summary>
internal sealed record ImageModel(double Scale, string? Background);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(UpdateModel))]
[JsonSerializable(typeof(CanvasModel))]
[JsonSerializable(typeof(ImageModel))]
internal sealed partial class InteropJson : JsonSerializerContext
{
    public static byte[] Bytes(UpdateModel model) => JsonSerializer.SerializeToUtf8Bytes(model, Default.UpdateModel);

    public static byte[] Bytes(CanvasModel model) => JsonSerializer.SerializeToUtf8Bytes(model, Default.CanvasModel);

    public static byte[] Bytes(ImageModel model) => JsonSerializer.SerializeToUtf8Bytes(model, Default.ImageModel);
}

/// <summary>What the JS module's init reports: the container width, font metrics and the viewer's time zone.</summary>
internal sealed record ChartContainerInfo(double Width, double[]? Widths, string? TimeZone)
{
    /// <summary>Reads init's result without reflection (a JsonElement needs no type metadata).</summary>
    public static ChartContainerInfo From(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return new ChartContainerInfo(0, null, null);
        }

        var width = e.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetDouble() : 0;
        double[]? widths = e.TryGetProperty("widths", out var ws) && ws.ValueKind == JsonValueKind.Array
            ? ws.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetDouble() : 0).ToArray()
            : null;
        var zone = e.TryGetProperty("timeZone", out var z) && z.ValueKind == JsonValueKind.String ? z.GetString() : null;
        return new ChartContainerInfo(width, widths, zone);
    }
}
