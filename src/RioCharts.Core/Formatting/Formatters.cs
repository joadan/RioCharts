using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Scales;

namespace RioCharts.Core.Formatting;

/// <summary>
/// Formats numeric tick labels for one axis. Large values are compact (1.2k, 3.4M);
/// decimals follow the tick step so labels stay short and consistent.
/// </summary>
public sealed class NumberFormatter
{
    private static readonly (double Divisor, string Suffix)[] Scales =
    [
        (1e12, "T"), (1e9, "B"), (1e6, "M"), (1e3, "k"),
    ];

    private readonly double _divisor;
    private readonly string _suffix;
    private readonly string _format;
    private readonly CultureInfo _culture;

    private NumberFormatter(double divisor, string suffix, int decimals, CultureInfo culture)
    {
        _divisor = divisor;
        _suffix = suffix;

        // Plain numbers keep fixed decimals so they align; compact ones drop trailing zeros (1k, 1.5k).
        _format = suffix.Length == 0 ? "N" + decimals : "#,##0." + new string('#', decimals);
        _culture = culture;
    }

    /// <summary>A custom .NET format string (e.g. "P0", "C2") overrides the automatic format.</summary>
    public static Func<double, string> ForTicks(IReadOnlyList<double> ticks, double step, CultureInfo culture, string? customFormat = null)
    {
        if (!string.IsNullOrEmpty(customFormat))
        {
            return v => SafeFormat(v, customFormat, culture);
        }

        var maxAbs = ticks.Count == 0 ? 0 : ticks.Max(Math.Abs);
        var (divisor, suffix) = (1d, "");
        foreach (var scale in Scales)
        {
            if (maxAbs >= scale.Divisor)
            {
                (divisor, suffix) = scale;
                break;
            }
        }

        var formatter = new NumberFormatter(divisor, suffix, Decimals(step / divisor), culture);
        return formatter.Format;
    }

    /// <summary>Formats a tick value with this axis's compact suffix and decimals.</summary>
    public string Format(double value)
    {
        if (!double.IsFinite(value))
        {
            return "";
        }

        var scaled = value / _divisor;
        var text = scaled.ToString(_format, _culture);
        return _suffix.Length == 0 || scaled == 0 ? text : text + _suffix;
    }

    /// <summary>Decimals needed to tell apart values on a grid of this step.</summary>
    public static int Decimals(double step)
    {
        if (!(step > 0) || !double.IsFinite(step))
        {
            return 0;
        }

        var decimals = (int)Math.Ceiling(-Math.Log10(step) - 1e-9);
        if (Math.Abs(Math.Round(step, Math.Clamp(decimals, 0, 15)) - step) > step * 1e-6)
        {
            decimals++;
        }

        return Math.Clamp(decimals, 0, 10);
    }

    private static string SafeFormat(double value, string format, CultureInfo culture)
    {
        try
        {
            return value.ToString(format, culture);
        }
        catch (FormatException)
        {
            return value.ToString(culture);
        }
    }
}

/// <summary>Single values for tooltips and data labels (not tied to a tick grid).</summary>
public static class ValueFormatter
{
    /// <summary>
    /// Up to two decimals, trailing zeros dropped, group separators. Values from
    /// <paramref name="compactFrom"/> up are compact (12.3k). A custom format string wins.
    /// </summary>
    public static string Format(double value, CultureInfo culture, string? customFormat = null, double compactFrom = 1e6)
    {
        if (!double.IsFinite(value))
        {
            return "";
        }

        if (!string.IsNullOrEmpty(customFormat))
        {
            try
            {
                return value.ToString(customFormat, culture);
            }
            catch (FormatException)
            {
                // fall through to the automatic format
            }
        }

        var abs = Math.Abs(value);
        if (abs >= compactFrom)
        {
            foreach (var (divisor, suffix) in new[] { (1e12, "T"), (1e9, "B"), (1e6, "M"), (1e3, "k") })
            {
                if (abs >= divisor)
                {
                    return (value / divisor).ToString("#,##0.#", culture) + suffix;
                }
            }
        }

        var text = value.ToString(abs >= 100 ? "#,##0.#" : "#,##0.##", culture);
        return text == "-0" ? "0" : text;
    }

    /// <summary>An x value for tooltips: a time at the resolution of the data, a category or a number.</summary>
    public static Func<double, string> ForX(XKind kind, IEnumerable<double> values, CultureInfo culture, string? customFormat = null)
    {
        if (kind == XKind.Time)
        {
            var pattern = "d";
            foreach (var v in values)
            {
                var t = TimeValue.ToDateTime(v);
                if (t.Second != 0 || t.Millisecond != 0)
                {
                    pattern = "G";
                    break;
                }

                if (t.TimeOfDay != TimeSpan.Zero)
                {
                    pattern = "g";
                }
            }

            return ms =>
            {
                var t = TimeValue.ToDateTime(ms);
                if (!string.IsNullOrEmpty(customFormat))
                {
                    try
                    {
                        return t.ToString(customFormat, culture);
                    }
                    catch (FormatException)
                    {
                    }
                }

                return t.ToString(pattern, culture);
            };
        }

        return v => Format(v, culture, customFormat, compactFrom: double.PositiveInfinity);
    }
}

/// <summary>Time tick labels that adapt to the interval: times, days, months or years.</summary>
public static class TimeFormatter
{
    /// <summary>Formats a time tick label.</summary>
    /// <param name="milliseconds">The time as an x value (see <see cref="Data.TimeValue"/>).</param>
    /// <param name="interval">The tick interval; picks how much of the time to show.</param>
    /// <param name="culture">The culture for month and day names.</param>
    /// <param name="customFormat">A .NET date format that replaces the adaptive one.</param>
    public static string Format(double milliseconds, TimeInterval interval, CultureInfo culture, string? customFormat = null)
    {
        var t = TimeValue.ToDateTime(milliseconds);
        if (!string.IsNullOrEmpty(customFormat))
        {
            try
            {
                return t.ToString(customFormat, culture);
            }
            catch (FormatException)
            {
                // fall through to the automatic format
            }
        }

        var f = culture.DateTimeFormat;
        var midnight = t.TimeOfDay == TimeSpan.Zero;
        return interval.Unit switch
        {
            TimeUnit.Second => t.ToString(f.LongTimePattern, culture),
            TimeUnit.Minute or TimeUnit.Hour => midnight ? t.ToString(ShortMonthDay(culture), culture) : t.ToString(f.ShortTimePattern, culture),
            TimeUnit.Day or TimeUnit.Week => t is { Month: 1, Day: 1 } ? t.ToString("yyyy", culture) : t.ToString(ShortMonthDay(culture), culture),
            TimeUnit.Month => t.Month == 1 ? t.ToString("yyyy", culture) : t.ToString("MMM", culture),
            _ => t.ToString("yyyy", culture),
        };
    }

    /// <summary>The culture's month/day pattern with an abbreviated month ("MMM d", "d MMM").</summary>
    public static string ShortMonthDay(CultureInfo culture) =>
        culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM", StringComparison.Ordinal);
}
