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
    public string? Fill { get; init; }
    public float? FillOpacity { get; init; }
    public string? Stroke { get; init; }
    public float? StrokeWidth { get; init; }
    public string? DashArray { get; init; }
    public float? Opacity { get; init; }
}

public sealed record TextStyleDef
{
    public float FontSize { get; init; } = 12;
    public int FontWeight { get; init; } = 400;
    public string? Fill { get; init; }
}

public sealed class StyleTable
{
    private readonly Dictionary<string, Style> _paint = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextStyleDef> _text = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, Style> Paint => _paint;
    public IReadOnlyDictionary<string, TextStyleDef> Text => _text;

    public StyleRef Add(string name, Style style)
    {
        _paint[name] = style;
        return new StyleRef(name);
    }

    public TextStyle Add(string name, TextStyleDef style)
    {
        _text[name] = style;
        return new TextStyle(name);
    }

    public Style Get(StyleRef reference) => _paint.TryGetValue(reference.Name, out var s) ? s : new Style();

    public TextStyleDef Get(TextStyle reference) => _text.TryGetValue(reference.Name, out var s) ? s : new TextStyleDef();
}

/// <summary>Theme token names. Renderers map them to CSS variables or concrete colors.</summary>
public static class ThemeTokens
{
    public const string Text = "--rio-text";
    public const string TextMuted = "--rio-text-muted";
    public const string Grid = "--rio-grid";
    public const string Axis = "--rio-axis";
    public const string Surface = "--rio-surface";

    /// <summary>Reference lines, bands and their labels (muted by default).</summary>
    public const string Annotation = "--rio-annotation";

    /// <summary>Data labels drawn on top of a bar's own color.</summary>
    public const string DataLabelInverse = "--rio-data-label-inverse";
    public const int SeriesColorCount = 10;

    /// <summary>Token for the n-th palette color (wraps around).</summary>
    public static string Series(int index) => "--rio-series-" + ((index % SeriesColorCount + SeriesColorCount) % SeriesColorCount + 1);

    public static bool IsToken(string? color) => color is not null && color.StartsWith("--", StringComparison.Ordinal);
}
