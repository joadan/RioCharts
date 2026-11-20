using RioCharts.Core.Scene;

namespace RioCharts.Core.Paths;

/// <summary>
/// Builds line and area paths from pixel coordinates. NaN in y (or x) splits the data into
/// segments, so missing values show as gaps instead of dropping to zero.
/// </summary>
public static class LineGenerator
{
    public static PathData Line(ReadOnlySpan<double> x, ReadOnlySpan<double> y, CurveType curve)
    {
        var builder = new PathBuilder();
        foreach (var (start, end) in Segments(x, y))
        {
            AddCurve(builder, x, y, start, end, curve, moveFirst: true);
        }

        return builder.Build();
    }

    /// <summary>Closed area between the line and a horizontal baseline, one shape per segment.</summary>
    public static PathData Area(ReadOnlySpan<double> x, ReadOnlySpan<double> y, double baselineY, CurveType curve)
    {
        var builder = new PathBuilder();
        foreach (var (start, end) in Segments(x, y))
        {
            builder.MoveTo(x[start], baselineY);
            builder.LineTo(x[start], y[start]);
            AddCurve(builder, x, y, start, end, curve, moveFirst: false);
            builder.LineTo(x[end - 1], baselineY);
            builder.Close();
        }

        return builder.Build();
    }

    /// <summary>Runs of consecutive finite points as [start, end) index pairs.</summary>
    public static List<(int Start, int End)> Segments(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
    {
        var segments = new List<(int, int)>();
        var start = -1;
        for (var i = 0; i < y.Length; i++)
        {
            var ok = double.IsFinite(x[i]) && double.IsFinite(y[i]);
            if (ok && start < 0)
            {
                start = i;
            }
            else if (!ok && start >= 0)
            {
                segments.Add((start, i));
                start = -1;
            }
        }

        if (start >= 0)
        {
            segments.Add((start, y.Length));
        }

        return segments;
    }

    private static void AddCurve(PathBuilder b, ReadOnlySpan<double> x, ReadOnlySpan<double> y, int start, int end, CurveType curve, bool moveFirst)
    {
        if (moveFirst)
        {
            b.MoveTo(x[start], y[start]);
        }

        var count = end - start;
        if (count == 1)
        {
            // A lone point between gaps: a zero-length segment shows as a dot with round caps.
            b.LineTo(x[start], y[start]);
            return;
        }

        switch (curve)
        {
            case CurveType.Step:
                // Step-after: each value holds until the next point (states, setpoints).
                for (var i = start; i < end - 1; i++)
                {
                    b.LineTo(x[i + 1], y[i]);
                    b.LineTo(x[i + 1], y[i + 1]);
                }

                break;
            case CurveType.Monotone when count > 2:
                AddMonotone(b, x, y, start, end);
                break;
            default:
                for (var i = start + 1; i < end; i++)
                {
                    b.LineTo(x[i], y[i]);
                }

                break;
        }
    }

    /// <summary>
    /// Monotone cubic interpolation in x (Fritsch–Carlson / Steffen style, as d3.curveMonotoneX):
    /// smooth, but never overshoots the data between points.
    /// </summary>
    private static void AddMonotone(PathBuilder b, ReadOnlySpan<double> x, ReadOnlySpan<double> y, int start, int end)
    {
        var n = end - start;
        var tangents = new double[n];
        for (var i = 1; i < n - 1; i++)
        {
            tangents[i] = InteriorTangent(x, y, start + i);
        }

        tangents[0] = EndTangent(x, y, start, start + 1, tangents[Math.Min(1, n - 1)]);
        tangents[n - 1] = EndTangent(x, y, end - 2, end - 1, tangents[Math.Max(0, n - 2)]);

        for (var i = 0; i < n - 1; i++)
        {
            var i0 = start + i;
            var i1 = i0 + 1;
            var dx = (x[i1] - x[i0]) / 3;
            b.CubicTo(x[i0] + dx, y[i0] + dx * tangents[i], x[i1] - dx, y[i1] - dx * tangents[i + 1], x[i1], y[i1]);
        }
    }

    private static double InteriorTangent(ReadOnlySpan<double> x, ReadOnlySpan<double> y, int i)
    {
        var h0 = x[i] - x[i - 1];
        var h1 = x[i + 1] - x[i];
        var s0 = h0 != 0 ? (y[i] - y[i - 1]) / h0 : 0;
        var s1 = h1 != 0 ? (y[i + 1] - y[i]) / h1 : 0;
        var p = h0 + h1 != 0 ? (s0 * h1 + s1 * h0) / (h0 + h1) : 0;
        return (Math.Sign(s0) + Math.Sign(s1)) * Math.Min(Math.Min(Math.Abs(s0), Math.Abs(s1)), 0.5 * Math.Abs(p));
    }

    private static double EndTangent(ReadOnlySpan<double> x, ReadOnlySpan<double> y, int i0, int i1, double neighbour)
    {
        var h = x[i1] - x[i0];
        return h != 0 ? (3 * (y[i1] - y[i0]) / h - neighbour) / 2 : neighbour;
    }
}
