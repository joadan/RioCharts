namespace RioCharts.Core.Scene;

/// <summary>Reference to a paint style in the scene's <see cref="StyleTable"/>.</summary>
public readonly record struct StyleRef(string Name);

/// <summary>Reference to a text style in the scene's <see cref="StyleTable"/>.</summary>
public readonly record struct TextStyle(string Name);

/// <summary>
/// Fill and stroke. Colors are CSS-like strings or theme tokens ("--rio-series-1");
/// renderers resolve tokens (SVG through CSS variables, canvas through values read once).
/// </summary>
public sealed record Style
{
    /// <summary>Fill color; null for no fill.</summary>
    public string? Fill { get; init; }

    /// <summary>Fill opacity, 0 to 1; null for opaque.</summary>
    public float? FillOpacity { get; init; }

    /// <summary>Stroke color; null for no stroke.</summary>
    public string? Stroke { get; init; }

    /// <summary>Stroke width in pixels; null for the renderer's default.</summary>
    public float? StrokeWidth { get; init; }

    /// <summary>Dash pattern as in SVG <c>stroke-dasharray</c> (e.g. <c>"4 3"</c>); null for a solid line.</summary>
    public string? DashArray { get; init; }

    /// <summary>Overall opacity, 0 to 1; null for opaque.</summary>
    public float? Opacity { get; init; }
}

/// <summary>A text style: font size, weight and color.</summary>
public sealed record TextStyleDef
{
    /// <summary>Font size in pixels. Default 12.</summary>
    public float FontSize { get; init; } = 12;

    /// <summary>Font weight as in CSS (400 normal, 600 semibold). Default 400.</summary>
    public int FontWeight { get; init; } = 400;

    /// <summary>Text color (CSS color or theme token); null for the renderer's default.</summary>
    public string? Fill { get; init; }
}

/// <summary>Named paint and text styles of a <see cref="Scene"/>; nodes refer to them by name.</summary>
public sealed class StyleTable
{
    private readonly Dictionary<string, Style> _paint = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextStyleDef> _text = new(StringComparer.Ordinal);

    /// <summary>Paint styles by name.</summary>
    public IReadOnlyDictionary<string, Style> Paint => _paint;

    /// <summary>Text styles by name.</summary>
    public IReadOnlyDictionary<string, TextStyleDef> Text => _text;

    /// <summary>Adds or replaces a paint style and returns a reference to it.</summary>
    public StyleRef Add(string name, Style style)
    {
        _paint[name] = style;
        return new StyleRef(name);
    }

    /// <summary>Adds or replaces a text style and returns a reference to it.</summary>
    public TextStyle Add(string name, TextStyleDef style)
    {
        _text[name] = style;
        return new TextStyle(name);
    }

    /// <summary>The referenced paint style, or an empty <see cref="Style"/> when the name is unknown.</summary>
    public Style Get(StyleRef reference) => _paint.TryGetValue(reference.Name, out var s) ? s : new Style();

    /// <summary>The referenced text style, or a default <see cref="TextStyleDef"/> when the name is unknown.</summary>
    public TextStyleDef Get(TextStyle reference) => _text.TryGetValue(reference.Name, out var s) ? s : new TextStyleDef();
}

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

    /// <summary>Rising values, e.g. a candle that closes above its open.</summary>
    public const string Positive = "--rio-positive";

    /// <summary>Falling values, e.g. a candle that closes below its open.</summary>
    public const string Negative = "--rio-negative";

    /// <summary>Number of colors in the series palette.</summary>
    public const int SeriesColorCount = 10;

    /// <summary>Token for the n-th palette color (wraps around).</summary>
    public static string Series(int index) => "--rio-series-" + ((index % SeriesColorCount + SeriesColorCount) % SeriesColorCount + 1);

    /// <summary>True when the color is a theme token (starts with <c>--</c>) rather than a CSS color.</summary>
    public static bool IsToken(string? color) => color is not null && color.StartsWith("--", StringComparison.Ordinal);
}
