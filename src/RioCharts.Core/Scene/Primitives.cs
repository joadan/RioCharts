namespace RioCharts.Core.Scene;

/// <summary>A point in pixels (y grows downwards).</summary>
/// <param name="X">Horizontal position.</param>
/// <param name="Y">Vertical position.</param>
public readonly record struct PointF(float X, float Y);

/// <summary>A rectangle in pixels (y grows downwards).</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct RectF(float X, float Y, float Width, float Height)
{
    /// <summary>Right edge.</summary>
    public float Right => X + Width;

    /// <summary>Bottom edge.</summary>
    public float Bottom => Y + Height;

    /// <summary>Horizontal center.</summary>
    public float CenterX => X + Width / 2;

    /// <summary>Vertical center.</summary>
    public float CenterY => Y + Height / 2;

    /// <summary>True when the point lies inside the rectangle or on its edge.</summary>
    public bool Contains(float x, float y) => x >= X && x <= Right && y >= Y && y <= Bottom;
}

/// <summary>2D affine transform [a c e; b d f].</summary>
public readonly record struct Matrix(float A, float B, float C, float D, float E, float F)
{
    /// <summary>The transform that changes nothing.</summary>
    public static Matrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    /// <summary>A translation by <paramref name="x"/>, <paramref name="y"/> pixels.</summary>
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

/// <summary>A path command in <see cref="PathData"/>; each takes a fixed number of coordinates (<see cref="PathData.CoordCount"/>).</summary>
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

    /// <summary>A path with no commands.</summary>
    public static PathData Empty { get; } = new([], []);

    /// <summary>The commands in order.</summary>
    public IReadOnlyList<PathCommand> Commands { get; }

    /// <summary>All coordinates in pixels, concatenated in command order (see <see cref="CoordCount"/>).</summary>
    public IReadOnlyList<float> Coords { get; }

    /// <summary>True when the path has no commands.</summary>
    public bool IsEmpty => Commands.Count == 0;

    /// <summary>Number of coordinates a command consumes from <see cref="Coords"/>.</summary>
    public static int CoordCount(PathCommand command) => command switch
    {
        PathCommand.MoveTo or PathCommand.LineTo => 2,
        PathCommand.CubicTo => 6,
        PathCommand.Arc => 5,
        _ => 0,
    };
}

/// <summary>Builds a <see cref="PathData"/> with chained calls. Coordinates are in pixels and stored as floats.</summary>
public sealed class PathBuilder
{
    private readonly List<PathCommand> _commands = [];
    private readonly List<float> _coords = [];

    /// <summary>Starts a new subpath at the point.</summary>
    public PathBuilder MoveTo(double x, double y) => Add(PathCommand.MoveTo, x, y);

    /// <summary>Draws a straight line to the point.</summary>
    public PathBuilder LineTo(double x, double y) => Add(PathCommand.LineTo, x, y);

    /// <summary>Draws a cubic Bézier curve to (<paramref name="x"/>, <paramref name="y"/>) with two control points.</summary>
    public PathBuilder CubicTo(double x1, double y1, double x2, double y2, double x, double y) =>
        Add(PathCommand.CubicTo, x1, y1, x2, y2, x, y);

    /// <summary>
    /// Draws a circular arc; angles in radians, clockwise from 12 o'clock. Connects from the
    /// current point with a line, if there is one (see <see cref="PathCommand.Arc"/>).
    /// </summary>
    public PathBuilder Arc(double cx, double cy, double radius, double startAngle, double endAngle) =>
        Add(PathCommand.Arc, cx, cy, radius, startAngle, endAngle);

    /// <summary>Closes the current subpath.</summary>
    public PathBuilder Close() => Add(PathCommand.Close);

    /// <summary>The finished path; <see cref="PathData.Empty"/> when nothing was added.</summary>
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
