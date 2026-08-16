namespace BedMesh.Core;

public readonly record struct ProbeSample
{
    public ProbeSample(int column, int row, double x, double y, double z)
    {
        if (column < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        if (row < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        Numeric.RequireFinite(x, nameof(x));
        Numeric.RequireFinite(y, nameof(y));
        Numeric.RequireFinite(z, nameof(z));
        Column = column;
        Row = row;
        X = x;
        Y = y;
        Z = z;
    }

    public int Column { get; }
    public int Row { get; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
}

public sealed class ProbeGrid
{
    private readonly IReadOnlyList<ProbeSample> _samples;

    public ProbeGrid(int columns, int rows, BedGeometry bed, IEnumerable<ProbeSample> samples)
    {
        if (columns < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), "At least two columns are required.");
        }

        if (rows < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), "At least two rows are required.");
        }

        ArgumentNullException.ThrowIfNull(bed);
        ArgumentNullException.ThrowIfNull(samples);
        ProbeSample[] copy = samples.ToArray();
        if (copy.Length != columns * rows)
        {
            throw new ArgumentException("The sample count must equal columns times rows.", nameof(samples));
        }

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                ProbeSample sample = copy[(row * columns) + column];
                if (sample.Column != column || sample.Row != row)
                {
                    throw new ArgumentException("Samples must be in row-major grid order.", nameof(samples));
                }
            }
        }

        Columns = columns;
        Rows = rows;
        Bed = bed;
        _samples = Array.AsReadOnly(copy);
        Bounds = new Bounds2D(this[0, 0].X, this[columns - 1, 0].X, this[0, 0].Y, this[0, rows - 1].Y);
    }

    public int Columns { get; }
    public int Rows { get; }
    public BedGeometry Bed { get; }
    public Bounds2D Bounds { get; }
    public IReadOnlyList<ProbeSample> Samples => _samples;
    public double SpacingX => (Bounds.MaxX - Bounds.MinX) / (Columns - 1);
    public double SpacingY => (Bounds.MaxY - Bounds.MinY) / (Rows - 1);

    public ProbeSample this[int column, int row]
    {
        get
        {
            if ((uint)column >= (uint)Columns)
            {
                throw new ArgumentOutOfRangeException(nameof(column));
            }

            if ((uint)row >= (uint)Rows)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            return _samples[(row * Columns) + column];
        }
    }
}

public sealed class ProbeSimulator
{
    public ProbeGrid Sample(
        IBedSurface surface,
        BedGeometry bed,
        int columns,
        int rows,
        double offsetX = 0,
        double offsetY = 0)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(bed);
        if (columns < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }

        if (rows < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        Numeric.RequirePositiveFinite(offsetX, nameof(offsetX), allowZero: true);
        Numeric.RequirePositiveFinite(offsetY, nameof(offsetY), allowZero: true);
        if ((2 * offsetX) >= bed.Width || (2 * offsetY) >= bed.Depth)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetX), "Offsets must leave a non-empty probed area.");
        }

        double spanX = bed.Width - (2 * offsetX);
        double spanY = bed.Depth - (2 * offsetY);
        var samples = new List<ProbeSample>(columns * rows);
        for (int row = 0; row < rows; row++)
        {
            double y = offsetY + (row * spanY / (rows - 1));
            for (int column = 0; column < columns; column++)
            {
                double x = offsetX + (column * spanX / (columns - 1));
                samples.Add(new ProbeSample(column, row, x, y, surface.GetHeight(x, y)));
            }
        }

        return new ProbeGrid(columns, rows, bed, samples);
    }
}
