using RioCharts.Core.Scene;

namespace RioCharts.Skia;

/// <summary>
/// Concrete colors for the theme tokens (<see cref="ThemeTokens"/>), since an exported image has
/// no CSS. The built-in light and dark themes match the Blazor component's defaults.
/// </summary>
/// <example><code>var theme = ExportTheme.Light.With(ThemeTokens.Series(0), "#0b6e4f");</code></example>
public sealed class ExportTheme
{
    private readonly Dictionary<string, string> _colors;

    private ExportTheme(Dictionary<string, string> colors) => _colors = colors;

    /// <summary>The built-in light theme.</summary>
    public static ExportTheme Light { get; } = new(new(StringComparer.Ordinal)
    {
        [ThemeTokens.Text] = "#1f2328",
        [ThemeTokens.TextMuted] = "#6b7280",
        [ThemeTokens.Grid] = "#e9ebef",
        [ThemeTokens.Axis] = "#c3c8d0",
        [ThemeTokens.Surface] = "#ffffff",
        [ThemeTokens.Annotation] = "#7c828c",
        [ThemeTokens.Positive] = "#059669",
        [ThemeTokens.Negative] = "#e11d48",
        [ThemeTokens.DataLabelInverse] = "#ffffff",
        [ThemeTokens.DataLabelDark] = "#1f2328",
        ["--rio-series-1"] = "#4269d0",
        ["--rio-series-2"] = "#efb118",
        ["--rio-series-3"] = "#ff725c",
        ["--rio-series-4"] = "#6cc5b0",
        ["--rio-series-5"] = "#3ca951",
        ["--rio-series-6"] = "#ff8ab7",
        ["--rio-series-7"] = "#a463f2",
        ["--rio-series-8"] = "#97bbf5",
        ["--rio-series-9"] = "#9c6b4e",
        ["--rio-series-10"] = "#9498a0",
    });

    /// <summary>The built-in dark theme.</summary>
    public static ExportTheme Dark { get; } = new(new(StringComparer.Ordinal)
    {
        [ThemeTokens.Text] = "#e6e8eb",
        [ThemeTokens.TextMuted] = "#9aa1ab",
        [ThemeTokens.Grid] = "#2b3038",
        [ThemeTokens.Axis] = "#4a515c",
        [ThemeTokens.Surface] = "#16191d",
        [ThemeTokens.Annotation] = "#8f96a0",
        [ThemeTokens.Positive] = "#34d399",
        [ThemeTokens.Negative] = "#fb7185",
        [ThemeTokens.DataLabelInverse] = "#ffffff",
        [ThemeTokens.DataLabelDark] = "#1f2328",
        ["--rio-series-1"] = "#6b8ff0",
        ["--rio-series-2"] = "#f2c14e",
        ["--rio-series-3"] = "#ff8a76",
        ["--rio-series-4"] = "#7fd3c0",
        ["--rio-series-5"] = "#5cc06e",
        ["--rio-series-6"] = "#ff9fc6",
        ["--rio-series-7"] = "#b98af7",
        ["--rio-series-8"] = "#a9c7f7",
        ["--rio-series-9"] = "#c08c6b",
        ["--rio-series-10"] = "#a8acb4",
    });

    /// <summary>A copy with one token set to a CSS color, e.g. <c>With("--rio-series-1", "#0b6e4f")</c>.</summary>
    public ExportTheme With(string token, string color)
    {
        var copy = new Dictionary<string, string>(_colors, StringComparer.Ordinal) { [token] = color };
        return new ExportTheme(copy);
    }

    /// <summary>The token's color (or the color itself when it is not a token).</summary>
    public string Resolve(string? color)
    {
        if (string.IsNullOrEmpty(color))
        {
            return "none";
        }

        return ThemeTokens.IsToken(color) ? _colors.GetValueOrDefault(color, "#808080") : color;
    }

    /// <summary>The theme's colors by token name.</summary>
    public IReadOnlyDictionary<string, string> Colors => _colors;
}
