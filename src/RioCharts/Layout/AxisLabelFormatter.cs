using System.Globalization;

namespace RioCharts.Layout;

/// <summary>Formats axis and tooltip values according to <see cref="AxisLabelFormat"/>.</summary>
public static class AxisLabelFormatter
{
    public static string Format(Axis? axis, double value, double step, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var format = axis?.LabelFormat ?? AxisLabelFormat.Auto;

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return value.ToString(culture);
        }

        try
        {
            return format switch
            {
                AxisLabelFormat.Number => value.ToString("N" + Decimals(step), culture),
                AxisLabelFormat.Currency => value.ToString("C" + Decimals(step), culture),
                AxisLabelFormat.Percentage => value.ToString("P" + Math.Max(0, Decimals(step * 100)), culture),
                AxisLabelFormat.Date => ToDate(value).ToString("d", culture),
                AxisLabelFormat.Time => ToDate(value).ToString("t", culture),
                AxisLabelFormat.DateTime => ToDate(value).ToString("g", culture),
                AxisLabelFormat.Custom when !string.IsNullOrEmpty(axis?.CustomFormatString) => FormatCustom(value, axis.CustomFormatString, culture),
                _ => FormatAuto(value, step, culture),
            };
        }
        catch (FormatException)
        {
            return FormatAuto(value, step, culture);
        }
    }

    /// <summary>Number of decimals needed to show values on a grid of the given step.</summary>
    public static int Decimals(double step)
    {
        if (step <= 0 || double.IsNaN(step) || double.IsInfinity(step))
        {
            return 2;
        }

        // Trim tiny floating point noise before deciding.
        var decimals = (int)Math.Ceiling(-Math.Log10(step) - 1e-9);
        var rounded = Math.Round(step, Math.Clamp(decimals, 0, 15));
        if (Math.Abs(rounded - step) > step * 1e-6)
        {
            decimals++; // e.g. step 2.5 needs one decimal
        }

        return Math.Clamp(decimals, 0, 10);
    }

    private static string FormatAuto(double value, double step, CultureInfo culture)
    {
        var abs = Math.Abs(value);
        if (abs >= 1e9 || (abs > 0 && abs < 1e-6 && step < 1e-6))
        {
            return value.ToString("0.###E+0", culture);
        }

        var decimals = step > 0 ? Decimals(step) : 2;
        return value.ToString("N" + decimals, culture);
    }

    private static string FormatCustom(double value, string format, CultureInfo culture)
    {
        // Date-like custom formats ("yyyy-MM", "HH:mm") apply to OLE Automation dates.
        if (LooksLikeDateFormat(format))
        {
            return ToDate(value).ToString(format, culture);
        }

        return value.ToString(format, culture);
    }

    private static bool LooksLikeDateFormat(string format) =>
        format.Contains("yy", StringComparison.Ordinal) || format.Contains("MM", StringComparison.Ordinal) ||
        format.Contains("dd", StringComparison.Ordinal) || format.Contains("HH", StringComparison.Ordinal) ||
        format.Contains("mm", StringComparison.Ordinal);

    private static DateTime ToDate(double value)
    {
        const double minOa = -657434.999;
        const double maxOa = 2958465.999;
        return DateTime.FromOADate(Math.Clamp(value, minOa, maxOa));
    }

    public static bool IsDateFormat(Axis? axis) => axis?.LabelFormat is AxisLabelFormat.Date or AxisLabelFormat.Time or AxisLabelFormat.DateTime
        || (axis?.LabelFormat == AxisLabelFormat.Custom && axis.CustomFormatString is { } f && LooksLikeDateFormat(f));
}
