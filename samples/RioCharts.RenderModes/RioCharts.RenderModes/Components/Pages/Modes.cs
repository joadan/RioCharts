namespace RioCharts.RenderModes.Components.Pages;

/// <summary>The pages, one per render mode.</summary>
public static class Modes
{
    public static readonly (string Href, string Name)[] All =
    [
        ("static", "Static server rendering"),
        ("streaming", "Static server rendering, streaming"),
        ("server", "Interactive Server"),
        ("server-no-prerender", "Interactive Server, not prerendered"),
        ("wasm", "Interactive WebAssembly"),
        ("wasm-no-prerender", "Interactive WebAssembly, not prerendered"),
        ("auto", "Interactive Auto"),
    ];
}
