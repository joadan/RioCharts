namespace RioCharts;

/// <summary>Built-in color palettes.</summary>
public static class ChartPalette
{
    /// <summary>Balanced categorical palette.</summary>
    public static IReadOnlyList<string> Default { get; } =
    [
        "#4E79A7", "#F28E2B", "#E15759", "#76B7B2", "#59A14F",
        "#EDC948", "#B07AA1", "#FF9DA7", "#9C755F", "#BAB0AC",
    ];

    /// <summary>Color-blind safe, high-contrast palette (Okabe–Ito, without yellow).</summary>
    public static IReadOnlyList<string> HighContrast { get; } =
    [
        "#000000", "#0072B2", "#D55E00", "#009E73", "#CC79A7", "#E69F00", "#56B4E9",
    ];

    /// <summary>Returns the palette color for an index, wrapping around.</summary>
    public static string Pick(IReadOnlyList<string>? palette, int index)
    {
        if (palette is null || palette.Count == 0)
        {
            palette = Default;
        }

        return palette[((index % palette.Count) + palette.Count) % palette.Count];
    }
}
