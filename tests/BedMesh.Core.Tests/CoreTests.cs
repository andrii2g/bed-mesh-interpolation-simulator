using System.Xml.Linq;

namespace BedMesh.Core.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void BedGeometryRejectsInvalidDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedGeometry(0, 250));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedGeometry(250, double.NaN));
    }

    [Fact]
    public void BoundsContainsFiniteCoordinatesOnly()
    {
        var bounds = new Bounds2D(0, 10, 0, 20);
        Assert.True(bounds.Contains(0, 20));
        Assert.False(bounds.Contains(-0.1, 2));
        Assert.False(bounds.Contains(double.NaN, 2));
    }
}

public sealed class SurfaceModelTests
{
    [Fact]
    public void AnalyticalModelsMatchKnownValues()
    {
        Assert.Equal(0.25, new FlatSurface(0.25).GetHeight(12, 34));
        Assert.Equal(-0.03, new TiltSurface(0, 0, 0, 0.001, -0.002).GetHeight(10, 20), 12);
        Assert.Equal(0, new BowlSurface(0.18, 125, 125, 125, 125).GetHeight(125, 125));
        Assert.True(new SaddleSurface(0.15, 125, 125, 125, 125).GetHeight(250, 125) > 0);
        Assert.True(new SaddleSurface(0.15, 125, 125, 125, 125).GetHeight(125, 250) < 0);
    }

    [Fact]
    public void GaussianAndCompositeMatchDefinitions()
    {
        var gaussian = new GaussianBumpSurface(0.15, 10, 20, 4, 6);
        Assert.Equal(0.15, gaussian.GetHeight(10, 20), 12);
        Assert.Equal(0.15 * Math.Exp(-0.5), gaussian.GetHeight(14, 20), 12);

        var composite = new CompositeSurface(new FlatSurface(1), new TiltSurface(0, 0, 0, 0.1, 0.2));
        Assert.Equal(1.5, composite.GetHeight(1, 2), 12);
    }
}

public sealed class ProbeSimulatorTests
{
    [Fact]
    public void FiveByFiveGridHasExpectedCoordinatesAndValues()
    {
        var surface = new TiltSurface(0, 0, 0, 0.001, -0.002);
        ProbeGrid grid = new ProbeSimulator().Sample(surface, new BedGeometry(250, 250), 5, 5);
        Assert.Equal(25, grid.Samples.Count);
        Assert.Equal(new[] { 0, 62.5, 125, 187.5, 250 }, Enumerable.Range(0, 5).Select(column => grid[column, 0].X));
        Assert.All(grid.Samples, sample => Assert.Equal(surface.GetHeight(sample.X, sample.Y), sample.Z, 12));
    }

    [Fact]
    public void OffsetsRemainInsideBed()
    {
        ProbeGrid grid = new ProbeSimulator().Sample(new FlatSurface(), new BedGeometry(250, 200), 3, 3, 10, 20);
        Assert.Equal(new Bounds2D(10, 240, 20, 180), grid.Bounds);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProbeSimulator().Sample(new FlatSurface(), new BedGeometry(250, 200), 3, 3, 125, 0));
    }
}

public sealed class BilinearInterpolatorTests
{
    private static readonly BedGeometry Bed = new(10, 10);

    [Fact]
    public void ReproducesEveryProbeNode()
    {
        ProbeGrid grid = new ProbeSimulator().Sample(
            new GaussianBumpSurface(0.2, 4, 7, 2, 3),
            Bed,
            5,
            5);
        var interpolator = new BilinearInterpolator();
        Assert.All(grid.Samples, sample => Assert.Equal(sample.Z, interpolator.Interpolate(grid, sample.X, sample.Y)));
    }

    [Fact]
    public void ReconstructsPlaneAndConstant()
    {
        var plane = new TiltSurface(0, 0, 0.4, 0.03, -0.02);
        ProbeGrid planeGrid = new ProbeSimulator().Sample(plane, Bed, 3, 3);
        ProbeGrid flatGrid = new ProbeSimulator().Sample(new FlatSurface(0.7), Bed, 3, 3);
        var interpolator = new BilinearInterpolator();

        foreach ((double x, double y) in new[] { (0.0, 0.0), (1.2, 7.8), (5.0, 5.0), (10.0, 10.0) })
        {
            Assert.Equal(plane.GetHeight(x, y), interpolator.Interpolate(planeGrid, x, y), 10);
            Assert.Equal(0.7, interpolator.Interpolate(flatGrid, x, y), 12);
        }
    }

    [Fact]
    public void HandCalculatedCellMidpointIsThree()
    {
        var samples = new[]
        {
            new ProbeSample(0, 0, 0, 0, 0),
            new ProbeSample(1, 0, 2, 0, 2),
            new ProbeSample(0, 1, 0, 2, 4),
            new ProbeSample(1, 1, 2, 2, 6),
        };
        var grid = new ProbeGrid(2, 2, new BedGeometry(2, 2), samples);
        Assert.Equal(3, new BilinearInterpolator().Interpolate(grid, 1, 1), 12);
    }

    [Fact]
    public void IsContinuousAcrossInternalCellBoundary()
    {
        ProbeGrid grid = new ProbeSimulator().Sample(new BowlSurface(0.2, 5, 5, 5, 5), Bed, 3, 3);
        var interpolator = new BilinearInterpolator();
        double left = interpolator.Interpolate(grid, 5 - 1e-10, 3.7);
        double edge = interpolator.Interpolate(grid, 5, 3.7);
        double right = interpolator.Interpolate(grid, 5 + 1e-10, 3.7);
        Assert.InRange(Math.Abs(left - edge), 0, 1e-10);
        Assert.InRange(Math.Abs(right - edge), 0, 1e-10);
    }
}

public sealed class InverseDistanceInterpolatorTests
{
    [Fact]
    public void ReproducesNodesAndSymmetricMidpoint()
    {
        var samples = new[]
        {
            new ProbeSample(0, 0, 0, 0, 0),
            new ProbeSample(1, 0, 2, 0, 2),
            new ProbeSample(0, 1, 0, 2, 0),
            new ProbeSample(1, 1, 2, 2, 2),
        };
        var grid = new ProbeGrid(2, 2, new BedGeometry(2, 2), samples);
        var interpolator = new InverseDistanceInterpolator();
        Assert.Equal(0, interpolator.Interpolate(grid, 0, 0));
        Assert.Equal(1, interpolator.Interpolate(grid, 1, 1), 12);
    }

    [Fact]
    public void RejectsInvalidPower()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InverseDistanceOptions(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InverseDistanceOptions(double.PositiveInfinity));
    }
}

public sealed class SurfaceEvaluatorTests
{
    [Fact]
    public void ErrorUsesEstimatedMinusTrueConvention()
    {
        ProbeGrid grid = new ProbeSimulator().Sample(new FlatSurface(), new BedGeometry(2, 2), 2, 2);
        SurfaceEvaluation evaluation = new SurfaceEvaluator().Evaluate(
            new FlatSurface(),
            grid,
            new ConstantInterpolator(0.1),
            new EvaluationGridOptions(3, 3));
        Assert.All(evaluation.Points, point => Assert.Equal(0.1, point.Error, 12));
    }

    private sealed class ConstantInterpolator(double value) : ISurfaceInterpolator
    {
        public string Name => "Constant";
        public double Interpolate(ProbeGrid grid, double x, double y) => value;
    }
}

public sealed class ErrorMetricsTests
{
    [Fact]
    public void CalculatesHandVerifiedFixture()
    {
        double[] errors = [-2, -1, 1, 2];
        EvaluationPoint[] points = errors
            .Select((error, index) => new EvaluationPoint(index, 0, 0, error, error))
            .ToArray();
        ErrorMetrics metrics = ErrorMetricsCalculator.Calculate(points);
        Assert.Equal(Math.Sqrt(2.5), metrics.RootMeanSquareError, 12);
        Assert.Equal(1.5, metrics.MeanAbsoluteError, 12);
        Assert.Equal(2, metrics.MaximumAbsoluteError);
        Assert.Equal(2, metrics.MaximumPositiveError);
        Assert.Equal(-2, metrics.MaximumNegativeError);
    }

    [Fact]
    public void UsesNearestRankPercentiles()
    {
        EvaluationPoint[] points = Enumerable.Range(1, 5)
            .Select(value => new EvaluationPoint(value, 0, 0, value, value))
            .ToArray();
        ErrorMetrics metrics = ErrorMetricsCalculator.Calculate(points);
        Assert.Equal(3, metrics.P50AbsoluteError);
        Assert.Equal(5, metrics.P90AbsoluteError);
        Assert.Equal(5, metrics.P99AbsoluteError);
    }
}

public sealed class ScenarioTests
{
    [Fact]
    public void AllScenariosRunForSupportedMeshSizes()
    {
        foreach (string name in ScenarioCatalog.Names)
        {
            SimulationScenario scenario = ScenarioCatalog.Create(name);
            foreach (int size in new[] { 3, 5, 7 })
            {
                ProbeGrid grid = new ProbeSimulator().Sample(scenario.Surface, scenario.Bed, size, size);
                SurfaceEvaluation evaluation = new SurfaceEvaluator().Evaluate(
                    scenario.Surface,
                    grid,
                    new BilinearInterpolator(),
                    new EvaluationGridOptions(9, 9));
                ErrorMetrics metrics = ErrorMetricsCalculator.Calculate(evaluation);
                Assert.True(double.IsFinite(metrics.RootMeanSquareError));
            }
        }
    }

    [Fact]
    public void FlatAndTiltMeetExactnessExpectations()
    {
        foreach (string name in new[] { "flat", "tilt" })
        {
            SimulationScenario scenario = ScenarioCatalog.Create(name);
            ProbeGrid grid = new ProbeSimulator().Sample(scenario.Surface, scenario.Bed, 3, 3);
            SurfaceEvaluation evaluation = new SurfaceEvaluator().Evaluate(
                scenario.Surface,
                grid,
                new BilinearInterpolator(),
                new EvaluationGridOptions(17, 17));
            Assert.InRange(ErrorMetricsCalculator.Calculate(evaluation).RootMeanSquareError, 0, 1e-10);
        }
    }
}

public sealed class SvgRendererTests
{
    [Fact]
    public void OutputIsDeterministicValidXml()
    {
        SimulationScenario scenario = ScenarioCatalog.Create("hidden-bump");
        ProbeGrid mesh = new ProbeSimulator().Sample(scenario.Surface, scenario.Bed, 3, 3);
        SurfaceEvaluation evaluation = new SurfaceEvaluator().Evaluate(
            scenario.Surface,
            mesh,
            new BilinearInterpolator(),
            new EvaluationGridOptions(9, 9));

        string first = SvgRenderer.RenderHeatmap(
            "Signed reconstruction error",
            evaluation,
            point => point.Error,
            -0.15,
            0.15,
            "Error (mm), reconstructed - true",
            signed: true);
        string second = SvgRenderer.RenderHeatmap(
            "Signed reconstruction error",
            evaluation,
            point => point.Error,
            -0.15,
            0.15,
            "Error (mm), reconstructed - true",
            signed: true);

        XDocument document = XDocument.Parse(first);
        Assert.Equal("svg", document.Root?.Name.LocalName);
        Assert.Contains("reconstructed - true", first);
        Assert.DoesNotContain("NaN", first);
        Assert.DoesNotContain("Infinity", first);
        Assert.Equal(first, second);
    }
}
