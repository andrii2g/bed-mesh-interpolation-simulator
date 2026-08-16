namespace BedMesh.Core;

public sealed record SimulationRequest(
    SimulationScenario Scenario,
    int MeshSize,
    ISurfaceInterpolator Interpolator,
    EvaluationGridOptions EvaluationOptions,
    string OutputDirectory,
    double OffsetX = 0,
    double OffsetY = 0,
    double? CommonErrorScale = null);

public sealed record SimulationResult(
    SimulationRequest Request,
    ProbeGrid Mesh,
    SurfaceEvaluation Evaluation,
    ErrorMetrics Metrics,
    IReadOnlyList<string> ArtifactPaths);

public sealed class SimulationRunner
{
    public SimulationResult Run(SimulationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MeshSize < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        var sampler = new ProbeSimulator();
        ProbeGrid mesh = sampler.Sample(
            request.Scenario.Surface,
            request.Scenario.Bed,
            request.MeshSize,
            request.MeshSize,
            request.OffsetX,
            request.OffsetY);
        SurfaceEvaluation evaluation = new SurfaceEvaluator().Evaluate(
            request.Scenario.Surface,
            mesh,
            request.Interpolator,
            request.EvaluationOptions);
        ErrorMetrics metrics = ErrorMetricsCalculator.Calculate(evaluation);
        IReadOnlyList<string> artifacts = SvgRenderer.WriteArtifacts(
            request.OutputDirectory,
            request.Scenario,
            mesh,
            evaluation,
            metrics,
            request.CommonErrorScale);
        return new SimulationResult(request, mesh, evaluation, metrics, artifacts);
    }
}
