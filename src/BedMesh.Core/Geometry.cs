namespace BedMesh.Core;

public readonly record struct Point2D
{
    public Point2D(double x, double y)
    {
        Numeric.RequireFinite(x, nameof(x));
        Numeric.RequireFinite(y, nameof(y));
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }
}

public readonly record struct SurfacePoint
{
    public SurfacePoint(double x, double y, double z)
    {
        Numeric.RequireFinite(x, nameof(x));
        Numeric.RequireFinite(y, nameof(y));
        Numeric.RequireFinite(z, nameof(z));
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }
}

public readonly record struct Bounds2D
{
    public Bounds2D(double minX, double maxX, double minY, double maxY)
    {
        Numeric.RequireFinite(minX, nameof(minX));
        Numeric.RequireFinite(maxX, nameof(maxX));
        Numeric.RequireFinite(minY, nameof(minY));
        Numeric.RequireFinite(maxY, nameof(maxY));
        if (maxX <= minX || maxY <= minY)
        {
            throw new ArgumentOutOfRangeException(nameof(maxX), "Maximum bounds must exceed minimum bounds.");
        }

        MinX = minX;
        MaxX = maxX;
        MinY = minY;
        MaxY = maxY;
    }

    public double MinX { get; }
    public double MaxX { get; }
    public double MinY { get; }
    public double MaxY { get; }

    public bool Contains(double x, double y) =>
        double.IsFinite(x) && double.IsFinite(y) &&
        x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
}

public sealed record BedGeometry
{
    public BedGeometry(double width, double depth)
    {
        Numeric.RequirePositiveFinite(width, nameof(width));
        Numeric.RequirePositiveFinite(depth, nameof(depth));
        Width = width;
        Depth = depth;
    }

    public double Width { get; }
    public double Depth { get; }
    public Bounds2D Bounds => new(0, Width, 0, Depth);
}

public static class Numeric
{
    public static bool NearlyEqual(double a, double b, double tolerance)
    {
        RequireFinite(a, nameof(a));
        RequireFinite(b, nameof(b));
        RequirePositiveFinite(tolerance, nameof(tolerance), allowZero: true);
        return Math.Abs(a - b) <= tolerance;
    }

    internal static void RequireFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be finite.");
        }
    }

    internal static void RequirePositiveFinite(double value, string parameterName, bool allowZero = false)
    {
        RequireFinite(value, parameterName);
        if (allowZero ? value < 0 : value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                allowZero ? "Value cannot be negative." : "Value must be positive.");
        }
    }
}
