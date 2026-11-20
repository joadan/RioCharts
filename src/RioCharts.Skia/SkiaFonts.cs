using RioCharts.Core.Text;
using SkiaSharp;

namespace RioCharts.Skia;

/// <summary>
/// Picks the first installed font of a CSS-like family list and creates typefaces per weight.
/// Falls back to Skia's default font, so export works on servers without the UI fonts.
/// </summary>
public sealed class SkiaFonts
{
    private readonly Dictionary<int, SKTypeface> _typefaces = [];
    private readonly Lock _lock = new();

    public SkiaFonts(string familyList)
    {
        FamilyList = familyList;
        Family = familyList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.Trim('"', '\''))
            .FirstOrDefault(f => SKFontManager.Default.MatchFamily(f) is { } typeface && Dispose(typeface))
            ?? SKTypeface.Default.FamilyName;
    }

    /// <summary>The family list as given (written to SVG as font-family).</summary>
    public string FamilyList { get; }

    /// <summary>The family Skia found and uses for PNG and measurement.</summary>
    public string Family { get; }

    public SKTypeface Typeface(int weight)
    {
        lock (_lock)
        {
            if (!_typefaces.TryGetValue(weight, out var typeface))
            {
                typeface = SKTypeface.FromFamilyName(Family, new SKFontStyle((SKFontStyleWeight)weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright))
                    ?? SKTypeface.Default;
                _typefaces[weight] = typeface;
            }

            return typeface;
        }
    }

    private static bool Dispose(SKTypeface typeface)
    {
        typeface.Dispose();
        return true;
    }
}

/// <summary>Real text widths from Skia, so a server-side layout fits its labels like the browser does.</summary>
public sealed class SkiaTextMeasurer(SkiaFonts fonts) : ITextMeasurer
{
    private readonly Lock _lock = new();

    public double Measure(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        lock (_lock)
        {
            using var font = new SKFont(fonts.Typeface(400), (float)fontSize);
            return font.MeasureText(text);
        }
    }
}
