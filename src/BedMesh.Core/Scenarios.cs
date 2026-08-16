namespace BedMesh.Core;

public sealed record SimulationScenario(
    string Name,
    string Description,
    BedGeometry Bed,
    IBedSurface Surface);

public static class ScenarioCatalog
{
    private static readonly string[] ScenarioNames =
        ["flat", "tilt", "bowl", "saddle", "hidden-bump", "complex"];

    public static IReadOnlyList<string> Names => ScenarioNames;

    public static SimulationScenario Create(string name, double width = 250, double depth = 250)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var bed = new BedGeometry(width, depth);
        double centerX = width / 2;
        double centerY = depth / 2;
        IBedSurface surface;
        string description;

        switch (name.Trim().ToLowerInvariant())
        {
            case "flat":
                description = "Perfectly flat numerical baseline.";
                surface = new FlatSurface();
                break;
            case "tilt":
                description = "A plane rising 0.12 mm across X and falling 0.08 mm across Y.";
                surface = new TiltSurface(centerX, centerY, 0, 0.12 / width, -0.08 / depth);
                break;
            case "bowl":
                description = "Smooth elliptical paraboloid.";
                surface = new BowlSurface(0.18, centerX, centerY, width / 2, depth / 2);
                break;
            case "saddle":
                description = "Hyperbolic paraboloid with opposite principal curvatures.";
                surface = new SaddleSurface(0.15, centerX, centerY, width / 2, depth / 2);
                break;
            case "hidden-bump":
                description = "A narrow localized bump that sparse probes can miss.";
                surface = new GaussianBumpSurface(
                    0.15,
                    width * (171.0 / 250.0),
                    depth * (94.0 / 250.0),
                    width * (12.0 / 250.0),
                    depth * (12.0 / 250.0));
                break;
            case "complex":
                description = "Tilt, bowl, local bump, and local depression combined.";
                surface = new CompositeSurface(
                    new TiltSurface(centerX, centerY, 0, 0.08 / width, -0.05 / depth),
                    new BowlSurface(0.10, centerX, centerY, width / 2, depth / 2),
                    new GaussianBumpSurface(
                        0.09,
                        width * (176.0 / 250.0),
                        depth * (82.0 / 250.0),
                        width * (16.0 / 250.0),
                        depth * (18.0 / 250.0)),
                    new GaussianBumpSurface(
                        -0.07,
                        width * (72.0 / 250.0),
                        depth * (182.0 / 250.0),
                        width * (20.0 / 250.0),
                        depth * (14.0 / 250.0)));
                break;
            default:
                throw new ArgumentException(
                    $"Unknown scenario '{name}'. Available scenarios: {string.Join(", ", ScenarioNames)}.",
                    nameof(name));
        }

        return new SimulationScenario(
            name.Trim().ToLowerInvariant(),
            description,
            bed,
            new BoundedSurface(surface, bed.Bounds));
    }
}
