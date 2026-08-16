using System.Xml.Linq;

namespace BedMesh.Core.Tests;

public sealed class BoundaryAcceptanceTests
{
    [Fact]
    public void ScenarioSurfaceRejectsCoordinatesOutsideConfiguredBed()
    {
        SimulationScenario scenario = ScenarioCatalog.Create("complex", 250, 200);
        Assert.True(double.IsFinite(scenario.Surface.GetHeight(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => scenario.Surface.GetHeight(-0.001, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => scenario.Surface.GetHeight(125, 200.001));
        Assert.Throws<ArgumentOutOfRangeException>(() => scenario.Surface.GetHeight(double.NaN, 100));
    }
}

public sealed class ScenarioInterpolationAcceptanceTests
{
    [Fact]
    public void EveryScenarioAndMeshRunsWithBilinearAndIdw()
    {
        foreach (string name in ScenarioCatalog.Names)
        {
            SimulationScenario scenario = ScenarioCatalog.Create(name);
            foreach (int size in new[] { 3, 5, 7 })
            {
                ProbeGrid grid = new ProbeSimulator().Sample(scenario.Surface, scenario.Bed, size, size);
                foreach (ISurfaceInterpolator interpolator in new ISurfaceInterpolator[]
                {
                    new BilinearInterpolator(),
                    new InverseDistanceInterpolator(),
                })
                {
                    SurfaceEvaluation evaluation = new SurfaceEvaluator().Evaluate(
                        scenario.Surface,
                        grid,
                        interpolator,
                        new EvaluationGridOptions(7, 7));
                    ErrorMetrics metrics = ErrorMetricsCalculator.Calculate(evaluation);
                    Assert.True(double.IsFinite(metrics.RootMeanSquareError));
                    Assert.True(double.IsFinite(metrics.MaximumAbsoluteError));
                }
            }
        }
    }

    [Fact]
    public void FlatSurfaceIsExactForBilinearAndIdw()
    {
        SimulationScenario scenario = ScenarioCatalog.Create("flat");
        ProbeGrid grid = new ProbeSimulator().Sample(scenario.Surface, scenario.Bed, 5, 5);
        foreach (ISurfaceInterpolator interpolator in new ISurfaceInterpolator[]
        {
            new BilinearInterpolator(),
            new InverseDistanceInterpolator(),
        })
        {
            SurfaceEvaluation evaluation = new SurfaceEvaluator().Evaluate(
                scenario.Surface,
                grid,
                interpolator,
                new EvaluationGridOptions(17, 17));
            Assert.InRange(ErrorMetricsCalculator.Calculate(evaluation).RootMeanSquareError, 0, 1e-12);
        }
    }
}

public sealed class SvgArtifactTests
{
    private static readonly string[] ExpectedFiles =
    [
        "absolute-error.svg",
        "centerline-x.svg",
        "centerline-y.svg",
        "reconstructed-surface.svg",
        "sampled-mesh.svg",
        "signed-error.svg",
        "true-surface.svg",
    ];

    [Fact]
    public void SimulationWritesAllSevenDeterministicValidSvgDocuments()
    {
        string directory = TemporaryDirectory();
        try
        {
            SimulationScenario scenario = ScenarioCatalog.Create("hidden-bump");
            var request = new SimulationRequest(
                scenario,
                5,
                new BilinearInterpolator(),
                new EvaluationGridOptions(9, 9),
                directory,
                CommonErrorScale: 0.15);
            SimulationResult first = new SimulationRunner().Run(request);
            var firstContents = first.ArtifactPaths.ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText);

            Assert.Equal(ExpectedFiles, first.ArtifactPaths.Select(path => Path.GetFileName(path)!).Order().ToArray());
            Assert.All(first.ArtifactPaths, path =>
            {
                string svg = File.ReadAllText(path);
                XDocument document = XDocument.Parse(svg);
                Assert.Equal("svg", document.Root?.Name.LocalName);
                Assert.NotNull(document.Root?.Element(document.Root!.Name.Namespace + "title"));
                Assert.Contains("mm", svg);
                Assert.DoesNotContain("NaN", svg);
                Assert.DoesNotContain("Infinity", svg);
            });

            SimulationResult second = new SimulationRunner().Run(request);
            Assert.All(second.ArtifactPaths, path =>
                Assert.Equal(firstContents[Path.GetFileName(path)!], File.ReadAllText(path)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ComparisonChartFitsLegendAndIsDeterministicValidXml()
    {
        SimulationScenario scenario = ScenarioCatalog.Create("bowl");
        var entries = new List<MeshComparisonEntry>();
        foreach (int size in new[] { 3, 5, 7 })
        {
            ProbeGrid mesh = new ProbeSimulator().Sample(scenario.Surface, scenario.Bed, size, size);
            SurfaceEvaluation evaluation = new SurfaceEvaluator().Evaluate(
                scenario.Surface,
                mesh,
                new BilinearInterpolator(),
                new EvaluationGridOptions(9, 9));
            entries.Add(new MeshComparisonEntry(
                size,
                size * size,
                "Bilinear",
                ErrorMetricsCalculator.Calculate(evaluation)));
        }

        string first = MeshComparisonSvgRenderer.Render(entries);
        string second = MeshComparisonSvgRenderer.Render(entries);
        XDocument document = XDocument.Parse(first);
        XElement root = Assert.IsType<XElement>(document.Root);
        Assert.Equal("svg", root.Name.LocalName);
        Assert.Equal("960", root.Attribute("width")?.Value);
        Assert.Equal("0 0 960 620", root.Attribute("viewBox")?.Value);
        XNamespace svg = root.Name.Namespace;
        XElement[] legendLabels = root
            .Descendants(svg + "text")
            .Where(element => element.Value.EndsWith(" RMSE", StringComparison.Ordinal) ||
                element.Value.EndsWith(" max |error|", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(legendLabels);
        Assert.All(legendLabels, label =>
            Assert.True(960 - int.Parse(label.Attribute("x")!.Value) >= 250));
        Assert.Contains("RMSE", first);
        Assert.Contains("max |error|", first);
        Assert.DoesNotContain("NaN", first);
        Assert.DoesNotContain("Infinity", first);
        Assert.Equal(first, second);
    }

    private static string TemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "bedmesh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

public sealed class CliSmokeTests
{
    [Fact]
    public void RequiredCommandsCompleteAndWriteExpectedArtifacts()
    {
        string root = Path.Combine(Path.GetTempPath(), "bedmesh-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            Console.SetOut(output);
            Console.SetError(error);

            string flat = Path.Combine(root, "flat");
            Assert.Equal(0, global::CliApp.Run(
                ["simulate", "--scenario", "flat", "--mesh", "3", "--evaluation", "9", "--output", flat]));
            AssertSevenArtifacts(flat);

            string hidden = Path.Combine(root, "hidden");
            Assert.Equal(0, global::CliApp.Run(
                ["simulate", "--scenario", "hidden-bump", "--mesh", "5", "--evaluation", "9", "--output", hidden]));
            AssertSevenArtifacts(hidden);

            string complex = Path.Combine(root, "complex");
            Assert.Equal(0, global::CliApp.Run(
                ["simulate", "--scenario", "complex", "--mesh", "7", "--interpolation", "both", "--evaluation", "9", "--output", complex]));
            AssertSevenArtifacts(Path.Combine(complex, "bilinear"));
            AssertSevenArtifacts(Path.Combine(complex, "idw"));

            string compare = Path.Combine(root, "compare");
            Assert.Equal(0, global::CliApp.Run(
                ["compare", "--scenario", "bowl", "--evaluation", "9", "--output", compare]));
            Assert.True(File.Exists(Path.Combine(compare, "comparison.csv")));
            Assert.True(File.Exists(Path.Combine(compare, "mesh-comparison.svg")));
            XDocument.Load(Path.Combine(compare, "mesh-comparison.svg"));

            Assert.Equal(0, global::CliApp.Run(["list-scenarios"]));
            Assert.Contains("hidden-bump", output.ToString());
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InvalidCliInputReturnsValidationExitCode()
    {
        TextWriter originalError = Console.Error;
        try
        {
            using var error = new StringWriter();
            Console.SetError(error);
            Assert.Equal(2, global::CliApp.Run(["simulate", "--mesh", "4"]));
            Assert.Contains("--mesh must be 3, 5, or 7", error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    private static void AssertSevenArtifacts(string directory)
    {
        string[] files = Directory.GetFiles(directory, "*.svg").Select(Path.GetFileName).ToArray()!;
        Assert.Equal(7, files.Length);
        Assert.All(files, file => XDocument.Load(Path.Combine(directory, file)));
    }
}
