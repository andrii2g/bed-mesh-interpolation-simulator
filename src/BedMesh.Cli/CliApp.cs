using System.Globalization;
using System.Text;
using BedMesh.Core;

public static class CliApp
{
    public static int Run(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
            {
                Usage();
                return 0;
            }

            Options options = Options.Parse(args[1..]);
            return args[0].ToLowerInvariant() switch
            {
                "simulate" => Simulate(options),
                "compare" => Compare(options),
                "list-scenarios" => ListScenarios(),
                _ => throw new ArgumentException($"Unknown command '{args[0]}'."),
            };
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 2;
        }
    }

    private static int Simulate(Options options)
    {
        string name = options.String("scenario", "hidden-bump");
        int mesh = options.Int("mesh", 5);
        if (mesh is not (3 or 5 or 7))
        {
            throw new ArgumentException("--mesh must be 3, 5, or 7.");
        }

        SimulationScenario scenario = MakeScenario(options, name);
        IReadOnlyList<ISurfaceInterpolator> interpolators = MakeInterpolators(options);
        string output = options.String("output", Path.Combine("output", $"{name}-{mesh}x{mesh}"));
        foreach (ISurfaceInterpolator interpolator in interpolators)
        {
            string directory = interpolators.Count == 1 ? output : Path.Combine(output, Slug(interpolator));
            SimulationResult result = new SimulationRunner().Run(new SimulationRequest(
                scenario,
                mesh,
                interpolator,
                new EvaluationGridOptions(EvaluationSize(options), EvaluationSize(options)),
                directory,
                options.Double("offset-x", 0),
                options.Double("offset-y", 0),
                options.OptionalDouble("common-error-scale")));
            Report(result);
        }

        return 0;
    }

    private static int Compare(Options options)
    {
        string name = options.String("scenario", "bowl");
        SimulationScenario scenario = MakeScenario(options, name);
        IReadOnlyList<ISurfaceInterpolator> interpolators = MakeInterpolators(options);
        int evaluation = EvaluationSize(options);
        string output = options.String("output", Path.Combine("output", $"compare-{name}"));
        Directory.CreateDirectory(output);
        var csv = new StringBuilder("mesh,probes,interpolation,rmse,mae,max_abs,max_positive,max_negative,worst_x,worst_y\n");
        var comparisonEntries = new List<MeshComparisonEntry>();
        foreach (int mesh in new[] { 3, 5, 7 })
        {
            foreach (ISurfaceInterpolator interpolator in interpolators)
            {
                string algorithm = Slug(interpolator);
                SimulationResult result = new SimulationRunner().Run(new SimulationRequest(
                    scenario,
                    mesh,
                    interpolator,
                    new EvaluationGridOptions(evaluation, evaluation),
                    Path.Combine(output, $"{mesh}x{mesh}-{algorithm}"),
                    CommonErrorScale: options.OptionalDouble("common-error-scale")));
                ErrorMetrics m = result.Metrics;
                comparisonEntries.Add(new MeshComparisonEntry(mesh, mesh * mesh, result.Request.Interpolator.Name, m));
                csv.AppendLine(FormattableString.Invariant(
                    $"{mesh}x{mesh},{mesh * mesh},{algorithm},{m.RootMeanSquareError:R},{m.MeanAbsoluteError:R},{m.MaximumAbsoluteError:R},{m.MaximumPositiveError:R},{m.MaximumNegativeError:R},{m.WorstErrorLocation.X:R},{m.WorstErrorLocation.Y:R}"));
                Report(result);
            }
        }

        string path = Path.Combine(output, "comparison.csv");
        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
        string chartPath = Path.Combine(output, "mesh-comparison.svg");
        MeshComparisonSvgRenderer.Write(chartPath, comparisonEntries);
        Console.WriteLine($"Comparison SVG: {Path.GetFullPath(chartPath)}");
        Console.WriteLine($"Comparison CSV: {Path.GetFullPath(path)}");
        return 0;
    }

    private static SimulationScenario MakeScenario(Options options, string name) =>
        ScenarioCatalog.Create(name, options.Double("width", 250), options.Double("depth", 250));

    private static int EvaluationSize(Options options)
    {
        int value = options.Int("evaluation", 251);
        return value is >= 2 and <= 2001 ? value : throw new ArgumentException("--evaluation must be between 2 and 2001.");
    }

    private static IReadOnlyList<ISurfaceInterpolator> MakeInterpolators(Options options)
    {
        double power = options.Double("idw-power", 2);
        return options.String("interpolation", "bilinear").ToLowerInvariant() switch
        {
            "bilinear" => [new BilinearInterpolator()],
            "idw" => [new InverseDistanceInterpolator(new InverseDistanceOptions(power))],
            "both" => [new BilinearInterpolator(), new InverseDistanceInterpolator(new InverseDistanceOptions(power))],
            _ => throw new ArgumentException("--interpolation must be bilinear, idw, or both."),
        };
    }

    private static int ListScenarios()
    {
        foreach (string name in ScenarioCatalog.Names)
        {
            SimulationScenario scenario = ScenarioCatalog.Create(name);
            Console.WriteLine($"{scenario.Name,-14} {scenario.Description}");
        }

        return 0;
    }

    private static void Report(SimulationResult result)
    {
        ErrorMetrics m = result.Metrics;
        Console.WriteLine();
        Console.WriteLine("Bed Mesh Interpolation Simulator");
        Console.WriteLine($"  Scenario              {result.Request.Scenario.Name}");
        Console.WriteLine($"  Bed                   {result.Request.Scenario.Bed.Width:0.###} x {result.Request.Scenario.Bed.Depth:0.###} mm");
        Console.WriteLine($"  Probe grid            {result.Mesh.Columns} x {result.Mesh.Rows} ({result.Mesh.Samples.Count} samples)");
        Console.WriteLine($"  Probe spacing         {result.Mesh.SpacingX:0.###} x {result.Mesh.SpacingY:0.###} mm");
        Console.WriteLine($"  Interpolation         {result.Request.Interpolator.Name}");
        Console.WriteLine($"  Evaluation            {result.Evaluation.Columns} x {result.Evaluation.Rows} ({result.Evaluation.Points.Count} samples)");
        Console.WriteLine($"  RMSE                  {m.RootMeanSquareError:0.000000} mm");
        Console.WriteLine($"  MAE                   {m.MeanAbsoluteError:0.000000} mm");
        Console.WriteLine($"  Maximum absolute      {m.MaximumAbsoluteError:0.000000} mm");
        Console.WriteLine($"  Maximum positive      {m.MaximumPositiveError:+0.000000;-0.000000;0.000000} mm");
        Console.WriteLine($"  Maximum negative      {m.MaximumNegativeError:+0.000000;-0.000000;0.000000} mm");
        Console.WriteLine($"  Worst location        X={m.WorstErrorLocation.X:0.###} mm, Y={m.WorstErrorLocation.Y:0.###} mm");
        Console.WriteLine($"  P50/P90/P95/P99       {m.P50AbsoluteError:0.000000} / {m.P90AbsoluteError:0.000000} / {m.P95AbsoluteError:0.000000} / {m.P99AbsoluteError:0.000000} mm");
        Console.WriteLine($"  Artifacts             {Path.GetFullPath(result.Request.OutputDirectory)}");
    }

    private static string Slug(ISurfaceInterpolator value) => value is BilinearInterpolator ? "bilinear" : "idw";

    private static void Usage() => Console.WriteLine(
        """
        Bed Mesh Interpolation Simulator

        Commands:
          simulate        Run a scenario and generate metrics plus seven SVG files.
          compare         Run 3x3, 5x5, and 7x7 and write comparison.csv.
          list-scenarios  List deterministic built-in surfaces.

        Options:
          --scenario <flat|tilt|bowl|saddle|hidden-bump|complex>
          --mesh <3|5|7>
          --interpolation <bilinear|idw|both>
          --evaluation <2..2001>
          --width <mm> --depth <mm>
          --offset-x <mm> --offset-y <mm>
          --idw-power <positive number>
          --output <directory>
          --common-error-scale <positive mm>
        """);

    private sealed class Options(Dictionary<string, string> values)
    {
        public static Options Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < args.Length; index += 2)
            {
                if (!args[index].StartsWith("--", StringComparison.Ordinal) ||
                    index + 1 >= args.Length ||
                    args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Invalid option near '{args[index]}'.");
                }

                if (!values.TryAdd(args[index][2..], args[index + 1]))
                {
                    throw new ArgumentException($"Option '{args[index]}' was supplied more than once.");
                }
            }

            return new Options(values);
        }

        public string String(string key, string fallback) => values.TryGetValue(key, out string? value) ? value : fallback;

        public int Int(string key, int fallback) =>
            int.TryParse(String(key, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : throw new ArgumentException($"--{key} must be an integer.");

        public double Double(string key, double fallback) =>
            ParseDouble(key, String(key, fallback.ToString("R", CultureInfo.InvariantCulture)));

        public double? OptionalDouble(string key) => values.TryGetValue(key, out string? value) ? ParseDouble(key, value) : null;

        private static double ParseDouble(string key, string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            {
                throw new ArgumentException($"--{key} must be a finite number.");
            }

            return value;
        }
    }
}
