namespace BedMesh.Core;

public sealed class BoundedSurface : IBedSurface
{
    public BoundedSurface(IBedSurface innerSurface, Bounds2D bounds)
    {
        ArgumentNullException.ThrowIfNull(innerSurface);
        InnerSurface = innerSurface;
        Bounds = bounds;
    }

    public IBedSurface InnerSurface { get; }
    public Bounds2D Bounds { get; }

    public double GetHeight(double x, double y)
    {
        Numeric.RequireFinite(x, nameof(x));
        Numeric.RequireFinite(y, nameof(y));
        if (!Bounds.Contains(x, y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                "Surface coordinates must remain inside the bed bounds.");
        }

        return InnerSurface.GetHeight(x, y);
    }
}
