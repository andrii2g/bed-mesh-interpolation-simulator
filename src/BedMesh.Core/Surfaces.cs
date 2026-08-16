namespace BedMesh.Core;

public interface IBedSurface
{
    double GetHeight(double x, double y);
}

public sealed record FlatSurface : IBedSurface
{
    public FlatSurface(double height = 0)
    {
        Numeric.RequireFinite(height, nameof(height));
        Height = height;
    }

    public double Height { get; }

    public double GetHeight(double x, double y)
    {
        ValidateCoordinates(x, y);
        return Height;
    }

    internal static void ValidateCoordinates(double x, double y)
    {
        Numeric.RequireFinite(x, nameof(x));
        Numeric.RequireFinite(y, nameof(y));
    }
}

public sealed record TiltSurface : IBedSurface
{
    public TiltSurface(double centerX, double centerY, double heightAtCenter, double slopeX, double slopeY)
    {
        Numeric.RequireFinite(centerX, nameof(centerX));
        Numeric.RequireFinite(centerY, nameof(centerY));
        Numeric.RequireFinite(heightAtCenter, nameof(heightAtCenter));
        Numeric.RequireFinite(slopeX, nameof(slopeX));
        Numeric.RequireFinite(slopeY, nameof(slopeY));
        CenterX = centerX;
        CenterY = centerY;
        HeightAtCenter = heightAtCenter;
        SlopeX = slopeX;
        SlopeY = slopeY;
    }

    public double CenterX { get; }
    public double CenterY { get; }
    public double HeightAtCenter { get; }
    public double SlopeX { get; }
    public double SlopeY { get; }

    public double GetHeight(double x, double y)
    {
        FlatSurface.ValidateCoordinates(x, y);
        return HeightAtCenter + (SlopeX * (x - CenterX)) + (SlopeY * (y - CenterY));
    }
}

public sealed record BowlSurface : IBedSurface
{
    public BowlSurface(double amplitude, double centerX, double centerY, double radiusX, double radiusY)
    {
        Numeric.RequireFinite(amplitude, nameof(amplitude));
        Numeric.RequireFinite(centerX, nameof(centerX));
        Numeric.RequireFinite(centerY, nameof(centerY));
        Numeric.RequirePositiveFinite(radiusX, nameof(radiusX));
        Numeric.RequirePositiveFinite(radiusY, nameof(radiusY));
        Amplitude = amplitude;
        CenterX = centerX;
        CenterY = centerY;
        RadiusX = radiusX;
        RadiusY = radiusY;
    }

    public double Amplitude { get; }
    public double CenterX { get; }
    public double CenterY { get; }
    public double RadiusX { get; }
    public double RadiusY { get; }

    public double GetHeight(double x, double y)
    {
        FlatSurface.ValidateCoordinates(x, y);
        double nx = (x - CenterX) / RadiusX;
        double ny = (y - CenterY) / RadiusY;
        return Amplitude * ((nx * nx) + (ny * ny));
    }
}

public sealed record SaddleSurface : IBedSurface
{
    public SaddleSurface(double amplitude, double centerX, double centerY, double radiusX, double radiusY)
    {
        Numeric.RequireFinite(amplitude, nameof(amplitude));
        Numeric.RequireFinite(centerX, nameof(centerX));
        Numeric.RequireFinite(centerY, nameof(centerY));
        Numeric.RequirePositiveFinite(radiusX, nameof(radiusX));
        Numeric.RequirePositiveFinite(radiusY, nameof(radiusY));
        Amplitude = amplitude;
        CenterX = centerX;
        CenterY = centerY;
        RadiusX = radiusX;
        RadiusY = radiusY;
    }

    public double Amplitude { get; }
    public double CenterX { get; }
    public double CenterY { get; }
    public double RadiusX { get; }
    public double RadiusY { get; }

    public double GetHeight(double x, double y)
    {
        FlatSurface.ValidateCoordinates(x, y);
        double nx = (x - CenterX) / RadiusX;
        double ny = (y - CenterY) / RadiusY;
        return Amplitude * ((nx * nx) - (ny * ny));
    }
}

public sealed record GaussianBumpSurface : IBedSurface
{
    public GaussianBumpSurface(double amplitude, double centerX, double centerY, double sigmaX, double sigmaY)
    {
        Numeric.RequireFinite(amplitude, nameof(amplitude));
        Numeric.RequireFinite(centerX, nameof(centerX));
        Numeric.RequireFinite(centerY, nameof(centerY));
        Numeric.RequirePositiveFinite(sigmaX, nameof(sigmaX));
        Numeric.RequirePositiveFinite(sigmaY, nameof(sigmaY));
        Amplitude = amplitude;
        CenterX = centerX;
        CenterY = centerY;
        SigmaX = sigmaX;
        SigmaY = sigmaY;
    }

    public double Amplitude { get; }
    public double CenterX { get; }
    public double CenterY { get; }
    public double SigmaX { get; }
    public double SigmaY { get; }

    public double GetHeight(double x, double y)
    {
        FlatSurface.ValidateCoordinates(x, y);
        double dx = x - CenterX;
        double dy = y - CenterY;
        double exponent = -(((dx * dx) / (2 * SigmaX * SigmaX)) + ((dy * dy) / (2 * SigmaY * SigmaY)));
        return Amplitude * Math.Exp(exponent);
    }
}

public sealed class CompositeSurface : IBedSurface
{
    private readonly IReadOnlyList<IBedSurface> _surfaces;

    public CompositeSurface(params IBedSurface[] surfaces)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        if (surfaces.Length == 0 || surfaces.Any(static surface => surface is null))
        {
            throw new ArgumentException("At least one non-null surface is required.", nameof(surfaces));
        }

        _surfaces = Array.AsReadOnly((IBedSurface[])surfaces.Clone());
    }

    public IReadOnlyList<IBedSurface> Surfaces => _surfaces;

    public double GetHeight(double x, double y)
    {
        FlatSurface.ValidateCoordinates(x, y);
        double total = 0;
        foreach (IBedSurface surface in _surfaces)
        {
            total += surface.GetHeight(x, y);
        }

        return total;
    }
}
