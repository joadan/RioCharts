using System.Text.Json;
using Bunit;
using Microsoft.JSInterop;

namespace RioCharts.Blazor.Tests;

/// <summary>
/// A bUnit context for &lt;RioChart&gt; with the JS module set up in strict mode: a JS call the
/// tests do not expect fails the test, so changes to the .NET/JS contract are noticed.
/// </summary>
public abstract class ChartTestBase : BunitContext
{
    public const string ModulePath = "./_content/RioCharts.Blazor/RioChart.razor.js";

    protected ChartTestBase()
    {
        // Like a chart on an interactive page (static rendering is in StaticRenderingTests).
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", isInteractive: true));
        JSInterop.Mode = JSRuntimeMode.Strict;
        Module = JSInterop.SetupModule(ModulePath);
        Init = Module.Setup<JsonElement>("init", _ => true);
        Init.SetResult(JsonSerializer.SerializeToElement(new { width = Width, widths = (double[]?)null, timeZone = "Europe/Stockholm" }));
        foreach (var name in new[] { "update", "paint", "dispose", "download", "downloadText" })
        {
            Module.SetupVoid(name, _ => true).SetVoidResult();
        }
    }

    /// <summary>The container width the fake JS module reports.</summary>
    protected const double Width = 600;

    protected BunitJSModuleInterop Module { get; }

    /// <summary>The JS module's init: what it reports about the container (a JsonElement, read without reflection).</summary>
    protected JSRuntimeInvocationHandler<JsonElement> Init { get; }

    /// <summary>A payload the chart sent to the JS module (UTF-8 JSON bytes), e.g. the model of the last update call.</summary>
    protected static JsonElement Payload(JSRuntimeInvocation call, int argument = 1) =>
        JsonDocument.Parse((byte[])call.Arguments[argument]!).RootElement;

    /// <summary>A field of a payload: true/false, a string or null.</summary>
    protected static object? Field(JsonElement model, string name) => model.TryGetProperty(name, out var v)
        ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetDouble(), _ => null }
        : null;

    /// <summary>The calls of a JS module function so far.</summary>
    protected IReadOnlyList<JSRuntimeInvocation> Calls(string identifier) => Module.Invocations[identifier];

    protected sealed record Reading(DateTime Time, double? Value);

    protected sealed record Alarm(string Store, double Minutes);

    protected static readonly DateTime Start = new(2026, 11, 16);

    protected static readonly Reading[] London =
        [new(Start, 19.5), new(Start.AddHours(1), 20.1), new(Start.AddHours(2), 20.8), new(Start.AddHours(3), 20.2)];

    protected static readonly Reading[] Toronto =
        [new(Start, 21.9), new(Start.AddHours(1), 22.3), new(Start.AddHours(2), 22.6), new(Start.AddHours(3), 22.1)];

    protected static readonly Alarm[] Alarms = [new("Store 1", 10), new("Store 2", 30), new("Store 1", 5)];
}
