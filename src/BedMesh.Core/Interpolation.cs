namespace BedMesh.Core;

public interface ISurfaceInterpolator
{
    string Name { get; }
    double Interpolate(ProbeGrid grid, double x, double y);
}

public sealed class BilinearInterpolator : ISurfaceInterpolator
{
    public string Name => "Bilinear";

    public double Interpolate(ProbeGrid grid, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ValidateQuery(grid, x, y);

        double relativeX = (x - grid.Bounds.MinX) / grid.SpacingX;
        double relativeY = (y - grid.Bounds.MinY) / grid.SpacingY;
        int column = Math.Clamp((int)Math.Floor(relativeX), 0, grid.Columns - 2);
        int row = Math.Clamp((int)Math.Floor(relativeY), 0, grid.Rows - 2);

        ProbeSample z11 = grid[column, row];
        ProbeSample z21 = grid[column + 1, row];
        ProbeSample z12 = grid[column, row + 1];
        ProbeSample z22 = grid[column + 1, row + 1];

        if (x == z11.X && y == z11.Y)
        {
            return z11.Z;
        }

        if (x == z21.X && y == z21.Y)
        {
            return z21.Z;
        }

        if (x == z12.X && y == z12.Y)
        {
            return z12.Z;
        }

        if (x == z22.X && y == z22.Y)
        {
            return z22.Z;
        }

        double u = (x - z11.X) / (z21.X - z11.X);
        double v = (y - z11.Y) / (z12.Y - z11.Y);
        double bottom = ((1 - u) * z11.Z) + (u * z21.Z);
        double top = ((1 - u) * z12.Z) + (u * z22.Z);
        return ((1 - v) * bottom) + (v * top);
    }

    internal static void ValidateQuery(ProbeGrid grid, double x, double y)
    {
        Numeric.RequireFinite(x, nameof(x));
        Numeric.RequireFinite(y, nameof(y));
        if (!grid.Bounds.Contains(x, y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Interpolation queries must remain inside probed bounds.");
        }
    }
}

public sealed record InverseDistanceOptions
{
    public InverseDistanceOptions(double power = 2.0, double exactNodeTolerance = 1e-12)
    {
        Numeric.RequirePositiveFinite(power, nameof(power));
        Numeric.RequirePositiveFinite(exactNodeTolerance, nameof(exactNodeTolerance), allowZero: true);
        Power = power;
        ExactNodeTolerance = exactNodeTolerance;
    }

    public double Power { get; }
    public double ExactNodeTolerance { get; }
}

public sealed class InverseDistanceInterpolator : ISurfaceInterpolator
{
    private readonly InverseDistanceOptions _options;

    public InverseDistanceInterpolator(InverseDistanceOptions? options = null)
    {
        _options = options ?? new InverseDistanceOptions();
    }

    public string Name => $"IDW (p={_options.Power:G})";

    public double Interpolate(ProbeGrid grid, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(grid);
        BilinearInterpolator.ValidateQuery(grid, x, y);
        double weighted = 0;
        double weights = 0;
        foreach (ProbeSample sample in grid.Samples)
        {
            double distance = Math.Hypot(x - sample.X, y - sample.Y);
            if (distance <= _options.ExactNodeTolerance)
            {
                return sample.Z;
            }

            double weight = 1 / Math.Pow(distance, _options.Power);
            weighted += weight * sample.Z;
            weights += weight;
        }

        double result = weighted / weights;
        if (!double.IsFinite(result))
        {
            throw new ArithmeticException("IDW produced a non-finite result.");
        }

        return result;
    }
}
