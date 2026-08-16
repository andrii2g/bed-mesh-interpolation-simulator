namespace BedMesh.Core;

public sealed record EvaluationGridOptions
{
    public EvaluationGridOptions(int columns = 251, int rows = 251)
    {
        if (columns < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }

        if (rows < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        Columns = columns;
        Rows = rows;
    }

    public int Columns { get; }
    public int Rows { get; }
}

public readonly record struct EvaluationPoint(
    double X,
    double Y,
    double TrueZ,
    double EstimatedZ,
    double Error);

public sealed class SurfaceEvaluation
{
    private readonly IReadOnlyList<EvaluationPoint> _points;

    public SurfaceEvaluation(int columns, int rows, IEnumerable<EvaluationPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        EvaluationPoint[] copy = points.ToArray();
        if (columns < 2 || rows < 2 || copy.Length != columns * rows)
        {
            throw new ArgumentException("Evaluation dimensions and point count are inconsistent.", nameof(points));
        }

        if (copy.Any(static point =>
            !double.IsFinite(point.X) ||
            !double.IsFinite(point.Y) ||
            !double.IsFinite(point.TrueZ) ||
            !double.IsFinite(point.EstimatedZ) ||
            !double.IsFinite(point.Error)))
        {
            throw new ArgumentException("Evaluation points must be finite.", nameof(points));
        }

        Columns = columns;
        Rows = rows;
        _points = Array.AsReadOnly(copy);
    }

    public int Columns { get; }
    public int Rows { get; }
    public IReadOnlyList<EvaluationPoint> Points => _points;

    public EvaluationPoint this[int column, int row] => _points[(row * Columns) + column];
}

public sealed class SurfaceEvaluator
{
    public SurfaceEvaluation Evaluate(
        IBedSurface trueSurface,
        ProbeGrid mesh,
        ISurfaceInterpolator interpolator,
        EvaluationGridOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(trueSurface);
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(interpolator);
        options ??= new EvaluationGridOptions();

        var points = new List<EvaluationPoint>(options.Columns * options.Rows);
        for (int row = 0; row < options.Rows; row++)
        {
            double y = mesh.Bounds.MinY +
                (row * (mesh.Bounds.MaxY - mesh.Bounds.MinY) / (options.Rows - 1));
            for (int column = 0; column < options.Columns; column++)
            {
                double x = mesh.Bounds.MinX +
                    (column * (mesh.Bounds.MaxX - mesh.Bounds.MinX) / (options.Columns - 1));
                double trueZ = trueSurface.GetHeight(x, y);
                double estimatedZ = interpolator.Interpolate(mesh, x, y);
                points.Add(new EvaluationPoint(x, y, trueZ, estimatedZ, estimatedZ - trueZ));
            }
        }

        return new SurfaceEvaluation(options.Columns, options.Rows, points);
    }
}

public sealed record ErrorMetrics(
    double RootMeanSquareError,
    double MeanAbsoluteError,
    double MaximumAbsoluteError,
    double MaximumPositiveError,
    double MaximumNegativeError,
    Point2D WorstErrorLocation,
    double WorstErrorSignedValue,
    double P50AbsoluteError,
    double P90AbsoluteError,
    double P95AbsoluteError,
    double P99AbsoluteError);

public static class ErrorMetricsCalculator
{
    public static ErrorMetrics Calculate(SurfaceEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        return Calculate(evaluation.Points);
    }

    public static ErrorMetrics Calculate(IReadOnlyList<EvaluationPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            throw new ArgumentException("At least one evaluation point is required.", nameof(points));
        }

        double squaredTotal = 0;
        double absoluteTotal = 0;
        double maximumPositive = double.NegativeInfinity;
        double maximumNegative = double.PositiveInfinity;
        int worstIndex = 0;
        var absoluteErrors = new double[points.Count];

        for (int index = 0; index < points.Count; index++)
        {
            double error = points[index].Error;
            Numeric.RequireFinite(error, nameof(points));
            double absolute = Math.Abs(error);
            squaredTotal += error * error;
            absoluteTotal += absolute;
            absoluteErrors[index] = absolute;
            maximumPositive = Math.Max(maximumPositive, error);
            maximumNegative = Math.Min(maximumNegative, error);
            if (absolute > absoluteErrors[worstIndex])
            {
                worstIndex = index;
            }
        }

        Array.Sort(absoluteErrors);
        EvaluationPoint worst = points[worstIndex];
        return new ErrorMetrics(
            Math.Sqrt(squaredTotal / points.Count),
            absoluteTotal / points.Count,
            absoluteErrors[^1],
            maximumPositive,
            maximumNegative,
            new Point2D(worst.X, worst.Y),
            worst.Error,
            Percentile(absoluteErrors, 0.50),
            Percentile(absoluteErrors, 0.90),
            Percentile(absoluteErrors, 0.95),
            Percentile(absoluteErrors, 0.99));
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        int rank = (int)Math.Ceiling(percentile * sortedValues.Count);
        return sortedValues[Math.Clamp(rank - 1, 0, sortedValues.Count - 1)];
    }
}
