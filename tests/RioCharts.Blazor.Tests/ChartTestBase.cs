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
        Init = Module.Setup<ChartContainerInfo>("init", _ => true);
        Init.SetResult(new ChartContainerInfo(Width, null, "Europe/Stockholm"));
        foreach (var name in new[] { "update", "paint", "dispose", "download", "downloadText" })
        {
            Module.SetupVoid(name, _ => true).SetVoidResult();
        }
    }

    /// <summary>The container width the fake JS module reports.</summary>
    protected const double Width = 600;

    protected BunitJSModuleInterop Module { get; }

    // private protected: ChartContainerInfo is internal to the library (visible to this project).
    private protected JSRuntimeInvocationHandler<ChartContainerInfo> Init { get; }

    /// <summary>The calls of a JS module function so far.</summary>
    protected IReadOnlyList<JSRuntimeInvocation> Calls(string identifier) => Module.Invocations[identifier];

    protected sealed record Reading(DateTime Time, double? Value);

    protected sealed record Alarm(string Unit, double Minutes);

    protected static readonly DateTime Start = new(2026, 11, 16);

    protected static readonly Reading[] Supply =
        [new(Start, 19.5), new(Start.AddHours(1), 20.1), new(Start.AddHours(2), 20.8), new(Start.AddHours(3), 20.2)];

    protected static readonly Reading[] Extract =
        [new(Start, 21.9), new(Start.AddHours(1), 22.3), new(Start.AddHours(2), 22.6), new(Start.AddHours(3), 22.1)];

    protected static readonly Alarm[] Alarms = [new("AHU 1", 10), new("AHU 2", 30), new("AHU 1", 5)];
}
