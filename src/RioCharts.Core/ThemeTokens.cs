namespace RioCharts.Core;

/// <summary>Theme token names. Renderers map them to CSS variables or concrete colors.</summary>
public static class ThemeTokens
{
    /// <summary>Primary text (title, values).</summary>
    public const string Text = "--rio-text";

    /// <summary>Secondary text (axis labels and titles, empty state).</summary>
    public const string TextMuted = "--rio-text-muted";

    /// <summary>Grid lines.</summary>
    public const string Grid = "--rio-grid";

    /// <summary>Axis lines and pie leader lines.</summary>
    public const string Axis = "--rio-axis";

    /// <summary>The chart background; also used for outlines that separate marks from each other.</summary>
    public const string Surface = "--rio-surface";

    /// <summary>Reference lines, bands and their labels (muted by default).</summary>
    public const string Annotation = "--rio-annotation";

    /// <summary>Data labels drawn on top of a bar's own color.</summary>
    public const string DataLabelInverse = "--rio-data-label-inverse";

    /// <summary>Data labels drawn on top of a light fill (the light palette colors), dark in both themes.</summary>
    public const string DataLabelDark = "--rio-data-label-dark";

    /// <summary>Rising values, e.g. a candle that closes above its open.</summary>
    public const string Positive = "--rio-positive";

    /// <summary>Falling values, e.g. a candle that closes below its open.</summary>
    public const string Negative = "--rio-negative";

    /// <summary>Number of colors in the series palette.</summary>
    public const int SeriesColorCount = 10;

    /// <summary>Token for the n-th palette color (wraps around).</summary>
    public static string Series(int index) => "--rio-series-" + ((index % SeriesColorCount + SeriesColorCount) % SeriesColorCount + 1);

    /// <summary>
    /// True for the built-in palette's light colors (yellow, teal, pink, light blue, grey: series
    /// 2, 4, 6, 8 and 10), where a label on top reads better in <see cref="DataLabelDark"/>.
    /// </summary>
    public static bool IsLightSeries(string? color) =>
        color is "--rio-series-2" or "--rio-series-4" or "--rio-series-6" or "--rio-series-8" or "--rio-series-10";

    /// <summary>True when the color is a theme token (starts with <c>--</c>) rather than a CSS color.</summary>
    public static bool IsToken(string? color) => color is not null && color.StartsWith("--", StringComparison.Ordinal);
}
