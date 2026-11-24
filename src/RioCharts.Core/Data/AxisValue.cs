using System.Globalization;
using System.Text.Json;

namespace RioCharts.Core.Data;

/// <summary>
/// Converts an annotation value (a number, a time, a category name, an ISO 8601 string or a
/// JSON element from a deserialized spec) to an axis position in data units.
/// </summary>
internal static class AxisValue
{
    /// <summary>
    /// The value on an axis of the given kind: a number, milliseconds for time axes, or the
    /// category index for category axes. NaN when it cannot be converted.
    /// </summary>
    public static double ToDouble(object? value, XKind kind, IReadOnlyList<string>? categories = null)
    {
        switch (value)
        {
            case null:
                return double.NaN;
            case JsonElement json:
                return json.ValueKind switch
                {
                    JsonValueKind.Number => json.GetDouble(),
                    JsonValueKind.String => ToDouble(json.GetString(), kind, categories),
                    _ => double.NaN,
                };
            case DateTime dt:
                return TimeValue.FromDateTime(dt);
            case DateTimeOffset dto:
                return TimeValue.FromDateTimeOffset(dto);
            case DateOnly d:
                return TimeValue.FromDateOnly(d);
            case string s:
                return FromString(s, kind, categories);
            case IConvertible convertible:
                try
                {
                    return convertible.ToDouble(CultureInfo.InvariantCulture);
                }
                catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
                {
                    return double.NaN;
                }

            default:
                return double.NaN;
        }
    }

    private static double FromString(string s, XKind kind, IReadOnlyList<string>? categories)
    {
        if (kind == XKind.Category)
        {
            if (categories is not null)
            {
                for (var i = 0; i < categories.Count; i++)
                {
                    if (string.Equals(categories[i], s, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }
            }

            return double.NaN;
        }

        if (kind == XKind.Time && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return TimeValue.FromDateTimeOffset(time); // wall clock as given (offset ignored), like data times
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : double.NaN;
    }
}
