using System.Globalization;
using System.Security;
using System.Text;

namespace BedMesh.Core;

public static class SvgRenderer
{
    private const int Width = 760;
    private const int Height = 620;
    private const double Left = 80;
    private const double Top = 50;
    private const double PlotWidth = 500;
    private const double PlotHeight = 500;

    public static IReadOnlyList<string> WriteArtifacts(
        string outputDirectory,
        SimulationScenario scenario,
        ProbeGrid mesh,
        SurfaceEvaluation evaluation,
        ErrorMetrics metrics,
        double? commonErrorScale = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(metrics);
        if (commonErrorScale is not null)
        {
            Numeric.RequirePositiveFinite(commonErrorScale.Value, nameof(commonErrorScale));
        }

        Directory.CreateDirectory(outputDirectory);
        double minZ = evaluation.Points.Min(static point => Math.Min(point.TrueZ, point.EstimatedZ));
        double maxZ = evaluation.Points.Max(static point => Math.Max(point.TrueZ, point.EstimatedZ));
        double errorExtent = commonErrorScale ??
            Math.Max(
                Math.Abs(evaluation.Points.Min(static point => point.Error)),
                Math.Abs(evaluation.Points.Max(static point => point.Error)));
        if (errorExtent == 0)
        {
            errorExtent = 1e-12;
        }

        var artifacts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["true-surface.svg"] = RenderHeatmap(
                "True analytical surface",
                evaluation,
                static point => point.TrueZ,
                minZ,
                maxZ,
                "Height (mm)",
                signed: false),
            ["sampled-mesh.svg"] = RenderProbeMesh(mesh),
            ["reconstructed-surface.svg"] = RenderHeatmap(
                "Interpolated reconstruction",
                evaluation,
                static point => point.EstimatedZ,
                minZ,
                maxZ,
                "Height (mm)",
                signed: false),
            ["signed-error.svg"] = RenderHeatmap(
                "Signed reconstruction error",
                evaluation,
                static point => point.Error,
                -errorExtent,
                errorExtent,
                "Error (mm), reconstructed - true",
                signed: true),
            ["absolute-error.svg"] = RenderHeatmap(
                "Absolute reconstruction error",
                evaluation,
                static point => Math.Abs(point.Error),
                0,
                errorExtent,
                "Absolute error (mm)",
                signed: false),
            ["centerline-x.svg"] = RenderCrossSection("X centerline", evaluation, alongX: true),
            ["centerline-y.svg"] = RenderCrossSection("Y centerline", evaluation, alongX: false),
        };

        var paths = new List<string>(artifacts.Count);
        foreach ((string fileName, string contents) in artifacts)
        {
            string path = Path.Combine(outputDirectory, fileName);
            File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            paths.Add(path);
        }

        return paths.AsReadOnly();
    }

    public static string RenderHeatmap(
        string title,
        SurfaceEvaluation evaluation,
        Func<EvaluationPoint, double> selector,
        double minimum,
        double maximum,
        string legendLabel,
        bool signed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(selector);
        Numeric.RequireFinite(minimum, nameof(minimum));
        Numeric.RequireFinite(maximum, nameof(maximum));
        if (maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum));
        }

        var builder = StartDocument(title);
        double cellWidth = PlotWidth / (evaluation.Columns - 1);
        double cellHeight = PlotHeight / (evaluation.Rows - 1);
        builder.AppendLine("""  <g id="plot">""");
        for (int row = 0; row < evaluation.Rows - 1; row++)
        {
            for (int column = 0; column < evaluation.Columns - 1; column++)
            {
                double value = selector(evaluation[column, row]);
                double t = maximum == minimum ? 0.5 : Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);
                string color = signed ? SignedColor(t) : SequentialColor(t);
                builder.AppendLine(FormattableString.Invariant(
                    $"    <rect x=\"{Left + (column * cellWidth):0.###}\" y=\"{Top + ((evaluation.Rows - 2 - row) * cellHeight):0.###}\" width=\"{cellWidth + 0.02:0.###}\" height=\"{cellHeight + 0.02:0.###}\" fill=\"{color}\"/>"));
            }
        }

        builder.AppendLine(FormattableString.Invariant(
            $"    <rect x=\"{Left:0.###}\" y=\"{Top:0.###}\" width=\"{PlotWidth:0.###}\" height=\"{PlotHeight:0.###}\" fill=\"none\" stroke=\"#333\"/>"));
        builder.AppendLine("  </g>");
        AppendAxes(builder, evaluation);
        AppendLegend(builder, minimum, maximum, legendLabel, signed);
        return EndDocument(builder);
    }

    public static string RenderProbeMesh(ProbeGrid mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var builder = StartDocument($"Sampled probe mesh ({mesh.Columns}x{mesh.Rows})");
        builder.AppendLine("""  <g id="grid-lines" stroke="#888" stroke-width="1">""");
        for (int column = 0; column < mesh.Columns; column++)
        {
            double x = MapX(mesh[column, 0].X, mesh.Bounds);
            builder.AppendLine(FormattableString.Invariant(
                $"    <line x1=\"{x:0.###}\" y1=\"{Top:0.###}\" x2=\"{x:0.###}\" y2=\"{Top + PlotHeight:0.###}\"/>"));
        }

        for (int row = 0; row < mesh.Rows; row++)
        {
            double y = MapY(mesh[0, row].Y, mesh.Bounds);
            builder.AppendLine(FormattableString.Invariant(
                $"    <line x1=\"{Left:0.###}\" y1=\"{y:0.###}\" x2=\"{Left + PlotWidth:0.###}\" y2=\"{y:0.###}\"/>"));
        }

        builder.AppendLine("  </g>");
        builder.AppendLine("""  <g id="probe-points" fill="#f59e0b" stroke="#1f2937">""");
        foreach (ProbeSample sample in mesh.Samples)
        {
            double x = MapX(sample.X, mesh.Bounds);
            double y = MapY(sample.Y, mesh.Bounds);
            builder.AppendLine(FormattableString.Invariant(
                $"    <circle cx=\"{x:0.###}\" cy=\"{y:0.###}\" r=\"5\"/>"));
            builder.AppendLine(FormattableString.Invariant(
                $"    <text x=\"{x + 7:0.###}\" y=\"{y - 7:0.###}\" font-size=\"10\" fill=\"#111\">{sample.Z:+0.000;-0.000;0.000}</text>"));
        }

        builder.AppendLine("  </g>");
        AppendPhysicalAxes(builder, mesh.Bounds);
        builder.AppendLine(FormattableString.Invariant(
            $"  <text x=\"610\" y=\"90\" font-size=\"13\">Min: {mesh.Samples.Min(static sample => sample.Z):+0.0000;-0.0000;0.0000} mm</text>"));
        builder.AppendLine(FormattableString.Invariant(
            $"  <text x=\"610\" y=\"115\" font-size=\"13\">Max: {mesh.Samples.Max(static sample => sample.Z):+0.0000;-0.0000;0.0000} mm</text>"));
        return EndDocument(builder);
    }

    public static string RenderCrossSection(string title, SurfaceEvaluation evaluation, bool alongX)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(evaluation);
        int count = alongX ? evaluation.Columns : evaluation.Rows;
        int fixedIndex = (alongX ? evaluation.Rows : evaluation.Columns) / 2;
        var points = new EvaluationPoint[count];
        for (int index = 0; index < count; index++)
        {
            points[index] = alongX ? evaluation[index, fixedIndex] : evaluation[fixedIndex, index];
        }

        double minZ = points.Min(static point => Math.Min(point.TrueZ, point.EstimatedZ));
        double maxZ = points.Max(static point => Math.Max(point.TrueZ, point.EstimatedZ));
        ExpandConstantRange(ref minZ, ref maxZ);
        double minCoordinate = alongX ? points[0].X : points[0].Y;
        double maxCoordinate = alongX ? points[^1].X : points[^1].Y;

        var builder = StartDocument(title);
        builder.AppendLine(FormattableString.Invariant(
            $"  <line x1=\"{Left}\" y1=\"{Top + PlotHeight}\" x2=\"{Left + PlotWidth}\" y2=\"{Top + PlotHeight}\" stroke=\"#333\"/>"));
        builder.AppendLine(FormattableString.Invariant(
            $"  <line x1=\"{Left}\" y1=\"{Top}\" x2=\"{Left}\" y2=\"{Top + PlotHeight}\" stroke=\"#333\"/>"));
        builder.AppendLine($"  <path d=\"{BuildPath(points, true, alongX, minCoordinate, maxCoordinate, minZ, maxZ)}\" fill=\"none\" stroke=\"#2563eb\" stroke-width=\"2\"/>");
        builder.AppendLine($"  <path d=\"{BuildPath(points, false, alongX, minCoordinate, maxCoordinate, minZ, maxZ)}\" fill=\"none\" stroke=\"#dc2626\" stroke-width=\"2\"/>");
        builder.AppendLine("""  <line x1="610" y1="88" x2="635" y2="88" stroke="#2563eb" stroke-width="2"/><text x="642" y="93" font-size="13">True</text>""");
        builder.AppendLine("""  <line x1="610" y1="118" x2="635" y2="118" stroke="#dc2626" stroke-width="2"/><text x="642" y="123" font-size="13">Reconstructed</text>""");
        builder.AppendLine(FormattableString.Invariant(
            $"  <text x=\"{Left + (PlotWidth / 2):0.###}\" y=\"600\" text-anchor=\"middle\" font-size=\"13\">{(alongX ? "X" : "Y")} (mm)</text>"));
        builder.AppendLine("""  <text x="20" y="300" transform="rotate(-90 20 300)" text-anchor="middle" font-size="13">Height (mm)</text>""");
        builder.AppendLine(FormattableString.Invariant(
            $"  <text x=\"{Left}\" y=\"570\" font-size=\"11\">{minCoordinate:0.###}</text>"));
        builder.AppendLine(FormattableString.Invariant(
            $"  <text x=\"{Left + PlotWidth}\" y=\"570\" text-anchor=\"end\" font-size=\"11\">{maxCoordinate:0.###}</text>"));
        builder.AppendLine(FormattableString.Invariant(
            $"  <text x=\"70\" y=\"{Top + 5}\" text-anchor=\"end\" font-size=\"11\">{maxZ:+0.000;-0.000;0.000}</text>"));
        builder.AppendLine(FormattableString.Invariant(
            $"  <text x=\"70\" y=\"{Top + PlotHeight}\" text-anchor=\"end\" font-size=\"11\">{minZ:+0.000;-0.000;0.000}</text>"));
        return EndDocument(builder);
    }

    private static StringBuilder StartDocument(string title)
    {
        var builder = new StringBuilder(4096);
        builder.AppendLine("""<?xml version="1.0" encoding="utf-8"?>""");
        builder.AppendLine(FormattableString.Invariant(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{Height}\" viewBox=\"0 0 {Width} {Height}\" role=\"img\">"));
        builder.AppendLine($"  <title>{Escape(title)}</title>");
        builder.AppendLine("""  <rect width="100%" height="100%" fill="#ffffff"/>""");
        builder.AppendLine($"  <text x=\"{Width / 2}\" y=\"28\" text-anchor=\"middle\" font-size=\"18\" font-family=\"sans-serif\">{Escape(title)}</text>");
        return builder;
    }

    private static string EndDocument(StringBuilder builder)
    {
        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static void AppendAxes(StringBuilder builder, SurfaceEvaluation evaluation)
    {
        Bounds2D bounds = new(
            evaluation[0, 0].X,
            evaluation[evaluation.Columns - 1, 0].X,
            evaluation[0, 0].Y,
            evaluation[0, evaluation.Rows - 1].Y);
        AppendPhysicalAxes(builder, bounds);
    }

    private static void AppendPhysicalAxes(StringBuilder builder, Bounds2D bounds)
    {
        builder.AppendLine(FormattableString.Invariant(
            $"  <text x=\"{Left + (PlotWidth / 2):0.###}\" y=\"600\" text-anchor=\"middle\" font-size=\"13\">X (mm), {bounds.MinX:0.###} to {bounds.MaxX:0.###}</text>"));
        builder.AppendLine(FormattableString.Invariant(
            $"  <text x=\"20\" y=\"{Top + (PlotHeight / 2):0.###}\" transform=\"rotate(-90 20 {Top + (PlotHeight / 2):0.###})\" text-anchor=\"middle\" font-size=\"13\">Y (mm), {bounds.MinY:0.###} to {bounds.MaxY:0.###}</text>"));
    }

    private static void AppendLegend(
        StringBuilder builder,
        double minimum,
        double maximum,
        string label,
        bool signed)
    {
        const double legendX = 620;
        const double legendY = 165;
        const double legendWidth = 28;
        const double legendHeight = 300;
        for (int index = 0; index < 20; index++)
        {
            double t = index / 19.0;
            string color = signed ? SignedColor(1 - t) : SequentialColor(1 - t);
            builder.AppendLine(FormattableString.Invariant(
                $"  <rect x=\"{legendX}\" y=\"{legendY + (index * legendHeight / 20):0.###}\" width=\"{legendWidth}\" height=\"{legendHeight / 20 + 0.1:0.###}\" fill=\"{color}\"/>"));
        }

        builder.AppendLine(FormattableString.Invariant(
            $"  <text x=\"655\" y=\"{legendY + 5}\" font-size=\"11\">{maximum:+0.000;-0.000;0.000}</text>"));
        builder.AppendLine(FormattableString.Invariant(
            $"  <text x=\"655\" y=\"{legendY + legendHeight}\" font-size=\"11\">{minimum:+0.000;-0.000;0.000}</text>"));
        builder.AppendLine($"  <text x=\"610\" y=\"495\" font-size=\"12\">{Escape(label)}</text>");
    }

    private static string BuildPath(
        IReadOnlyList<EvaluationPoint> points,
        bool trueSurface,
        bool alongX,
        double minimumCoordinate,
        double maximumCoordinate,
        double minimumZ,
        double maximumZ)
    {
        var builder = new StringBuilder(points.Count * 18);
        for (int index = 0; index < points.Count; index++)
        {
            EvaluationPoint point = points[index];
            double coordinate = alongX ? point.X : point.Y;
            double z = trueSurface ? point.TrueZ : point.EstimatedZ;
            double x = Left + ((coordinate - minimumCoordinate) / (maximumCoordinate - minimumCoordinate) * PlotWidth);
            double y = Top + ((maximumZ - z) / (maximumZ - minimumZ) * PlotHeight);
            builder.Append(index == 0 ? "M " : " L ");
            builder.Append(F(x)).Append(' ').Append(F(y));
        }

        return builder.ToString();
    }

    private static double MapX(double x, Bounds2D bounds) =>
        Left + ((x - bounds.MinX) / (bounds.MaxX - bounds.MinX) * PlotWidth);

    private static double MapY(double y, Bounds2D bounds) =>
        Top + ((bounds.MaxY - y) / (bounds.MaxY - bounds.MinY) * PlotHeight);

    private static string SequentialColor(double t) =>
        Mix((239, 246, 255), (29, 78, 216), t);

    private static string SignedColor(double t) =>
        t <= 0.5
            ? Mix((37, 99, 235), (250, 250, 250), t * 2)
            : Mix((250, 250, 250), (220, 38, 38), (t - 0.5) * 2);

    private static string Mix((int R, int G, int B) start, (int R, int G, int B) end, double t)
    {
        int r = (int)Math.Round(start.R + ((end.R - start.R) * t));
        int g = (int)Math.Round(start.G + ((end.G - start.G) * t));
        int b = (int)Math.Round(start.B + ((end.B - start.B) * t));
        return FormattableString.Invariant($"#{r:X2}{g:X2}{b:X2}");
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private static void ExpandConstantRange(ref double minimum, ref double maximum)
    {
        if (maximum == minimum)
        {
            double padding = Math.Max(Math.Abs(minimum) * 0.05, 1e-6);
            minimum -= padding;
            maximum += padding;
        }
    }
}
