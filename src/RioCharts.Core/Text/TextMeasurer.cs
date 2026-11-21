namespace RioCharts.Core.Text;

/// <summary>Measures text for layout (label sizes decide margins and tick density).</summary>
public interface ITextMeasurer
{
    /// <summary>Width of the text in pixels at the given font size.</summary>
    double Measure(string text, double fontSize);
}

/// <summary>
/// Approximation from per-character width classes. Used for prerendering and server-side
/// layout when no real font metrics are available; close enough for common UI fonts.
/// </summary>
public sealed class ApproximateTextMeasurer : ITextMeasurer
{
    /// <summary>The shared instance.</summary>
    public static ApproximateTextMeasurer Instance { get; } = new();

    /// <inheritdoc />
    public double Measure(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        double em = 0;
        foreach (var c in text)
        {
            em += CharWidth(c);
        }

        return em * fontSize;
    }

    private static double CharWidth(char c) => c switch
    {
        >= '0' and <= '9' => 0.56,
        'i' or 'l' or 'j' or 'I' or '!' or '|' or '\'' => 0.25,
        '.' or ',' or ':' or ';' or ' ' => 0.28,
        'f' or 't' or 'r' or '(' or ')' or '[' or ']' or '-' => 0.36,
        'm' or 'w' => 0.82,
        'M' or 'W' => 0.88,
        >= 'A' and <= 'Z' => 0.66,
        >= 'a' and <= 'z' => 0.53,
        '%' => 0.85,
        _ when char.IsWhiteSpace(c) => 0.28,
        _ => 0.6,
    };
}

/// <summary>
/// Measurer built from real font metrics: a width per printable ASCII character
/// (as a fraction of the font size), measured once in the browser and cached.
/// Other characters use the average width.
/// </summary>
public sealed class WidthTableTextMeasurer : ITextMeasurer
{
    private const int First = 32;
    private readonly double[] _widths;
    private readonly double _average;

    /// <param name="widths">Widths for characters 32..126, each relative to a font size of 1.</param>
    public WidthTableTextMeasurer(IReadOnlyList<double> widths)
    {
        _widths = widths.ToArray();
        _average = _widths.Length > 0 ? _widths.Average() : 0.55;
    }

    /// <inheritdoc />
    public double Measure(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        double em = 0;
        foreach (var c in text)
        {
            var i = c - First;
            em += i >= 0 && i < _widths.Length ? _widths[i] : _average;
        }

        return em * fontSize;
    }
}
