namespace RioCharts.Core.Scene;

public readonly record struct PointF(float X, float Y);

public readonly record struct RectF(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public float CenterX => X + Width / 2;
    public float CenterY => Y + Height / 2;

    public bool Contains(float x, float y) => x >= X && x <= Right && y >= Y && y <= Bottom;
}

/// <summary>2D affine transform [a c e; b d f].</summary>
public readonly record struct Matrix(float A, float B, float C, float D, float E, float F)
{
    public static Matrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public static Matrix Translate(float x, float y) => new(1, 0, 0, 1, x, y);

    /// <summary>Rotation in degrees (clockwise in screen coordinates) around a point.</summary>
    public static Matrix Rotate(float degrees, float cx, float cy)
    {
        var r = degrees * MathF.PI / 180;
        var cos = MathF.Cos(r);
        var sin = MathF.Sin(r);
        return new Matrix(cos, sin, -sin, cos, cx - cos * cx + sin * cy, cy - sin * cx - cos * cy);
    }
}

public enum PathCommand : byte
{
    /// <summary>x, y</summary>
    MoveTo,

    /// <summary>x, y</summary>
    LineTo,

    /// <summary>x1, y1, x2, y2, x, y</summary>
    CubicTo,

    /// <summary>
    /// cx, cy, radius, startAngle, endAngle (radians, clockwise from 12 o'clock).
    /// Like canvas <c>arc()</c>: connects from the current point with a line, if there is one.
    /// </summary>
    Arc,

    /// <summary>no coordinates</summary>
    Close,
}

/// <summary>
/// Path geometry as commands plus float coordinates. Render-agnostic: each renderer converts
/// it (the SVG renderer writes a "d" attribute; a canvas renderer would replay the commands).
/// </summary>
public sealed class PathData
{
    internal PathData(PathCommand[] commands, float[] coords)
    {
        Commands = commands;
        Coords = coords;
    }

    public static PathData Empty { get; } = new([], []);

    public IReadOnlyList<PathCommand> Commands { get; }
    public IReadOnlyList<float> Coords { get; }
    public bool IsEmpty => Commands.Count == 0;

    public static int CoordCount(PathCommand command) => command switch
    {
        PathCommand.MoveTo or PathCommand.LineTo => 2,
        PathCommand.CubicTo => 6,
        PathCommand.Arc => 5,
        _ => 0,
    };
}

public sealed class PathBuilder
{
    private readonly List<PathCommand> _commands = [];
    private readonly List<float> _coords = [];

    public PathBuilder MoveTo(double x, double y) => Add(PathCommand.MoveTo, x, y);

    public PathBuilder LineTo(double x, double y) => Add(PathCommand.LineTo, x, y);

    public PathBuilder CubicTo(double x1, double y1, double x2, double y2, double x, double y) =>
        Add(PathCommand.CubicTo, x1, y1, x2, y2, x, y);

    public PathBuilder Arc(double cx, double cy, double radius, double startAngle, double endAngle) =>
        Add(PathCommand.Arc, cx, cy, radius, startAngle, endAngle);

    public PathBuilder Close() => Add(PathCommand.Close);

    public PathData Build() => _commands.Count == 0 ? PathData.Empty : new PathData([.. _commands], [.. _coords]);

    private PathBuilder Add(PathCommand command, params ReadOnlySpan<double> values)
    {
        _commands.Add(command);
        foreach (var v in values)
        {
            _coords.Add((float)v);
        }

        return this;
    }
}
