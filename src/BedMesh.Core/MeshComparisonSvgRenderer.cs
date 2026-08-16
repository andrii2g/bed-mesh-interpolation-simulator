using System.Globalization;
using System.Security;
using System.Text;

namespace BedMesh.Core;

public sealed record MeshComparisonEntry(
    int MeshSize,
    int ProbeCount,
    string Interpolation,
    ErrorMetrics Metrics);

public static class MeshComparisonSvgRenderer
{
    private const double DocumentWidth = 960;
    private const double DocumentHeight = 620;
    private const double Left = 85;
    private const double Top = 55;
    private const double PlotWidth = 540;
    private const double PlotHeight = 450;

    public static void Write(string path, IReadOnlyList<MeshComparisonEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Render(entries), new UTF8Encoding(false));
    }

    public static string Render(IReadOnlyList<MeshComparisonEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            throw new ArgumentException("At least one comparison entry is required.", nameof(entries));
        }

        MeshComparisonEntry[] ordered = entries
            .OrderBy(static entry => entry.Interpolation, StringComparer.Ordinal)
            .ThenBy(static entry => entry.ProbeCount)
            .ToArray();
        if (ordered.Any(static entry =>
            entry.MeshSize < 2 ||
            entry.ProbeCount <= 0 ||
            string.IsNullOrWhiteSpace(entry.Interpolation) ||
            !double.IsFinite(entry.Metrics.RootMeanSquareError) ||
            !double.IsFinite(entry.Metrics.MaximumAbsoluteError) ||
            entry.Metrics.RootMeanSquareError < 0 ||
            entry.Metrics.MaximumAbsoluteError < 0))
        {
            throw new ArgumentException("Comparison entries contain invalid values.", nameof(entries));
        }

        int[] probeCounts = ordered.Select(static entry => entry.ProbeCount).Distinct().Order().ToArray();
        double maximum = ordered.Max(static entry => entry.Metrics.MaximumAbsoluteError);
        if (maximum == 0)
        {
            maximum = 1e-12;
        }

        var builder = new StringBuilder(4096);
        builder.AppendLine("""<?xml version="1.0" encoding="utf-8"?>""");
        builder.AppendLine(FormattableString.Invariant(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{DocumentWidth:0}\" height=\"{DocumentHeight:0}\" viewBox=\"0 0 {DocumentWidth:0} {DocumentHeight:0}\" role=\"img\">"));
        builder.AppendLine("  <title>Mesh interpolation error comparison</title>");
        builder.AppendLine(FormattableString.Invariant(
            $"  <text x=\"{DocumentWidth / 2:0.###}\" y=\"28\" text-anchor=\"middle\" font-size=\"18\" font-family=\"sans-serif\">Mesh interpolation error comparison</text>"));

        for (int tick = 0; tick <= 5; tick++)
        {
            double value = maximum * tick / 5;
            double y = Top + PlotHeight - (PlotHeight * tick / 5);
            builder.AppendLine(FormattableString.Invariant(
                $"  <line x1=\"{Left:0.###}\" y1=\"{y:0.###}\" x2=\"{Left + PlotWidth:0.###}\" y2=\"{y:0.###}\" stroke=\"#d1d5db\"/>"));
            builder.AppendLine(FormattableString.Invariant(
                $"  <text x=\"{Left - 10:0.###}\" y=\"{y + 4:0.###}\" text-anchor=\"end\" font-size=\"11\">{value:0.0000}</text>"));
        }

        builder.AppendLine(FormattableString.Invariant(
            $"  <line x1=\"{Left}\" y1=\"{Top}\" x2=\"{Left}\" y2=\"{Top + PlotHeight}\" stroke=\"#333\"/>"));
        builder.AppendLine(FormattableString.Invariant(
            $"  <line x1=\"{Left}\" y1=\"{Top + PlotHeight}\" x2=\"{Left + PlotWidth}\" y2=\"{Top + PlotHeight}\" stroke=\"#333\"/>"));

        for (int index = 0; index < probeCounts.Length; index++)
        {
            double x = X(index, probeCounts.Length);
            int mesh = ordered.First(entry => entry.ProbeCount == probeCounts[index]).MeshSize;
            builder.AppendLine(FormattableString.Invariant(
                $"  <text x=\"{x:0.###}\" y=\"{Top + PlotHeight + 23:0.###}\" text-anchor=\"middle\" font-size=\"11\">{mesh}x{mesh}</text>"));
            builder.AppendLine(FormattableString.Invariant(
                $"  <text x=\"{x:0.###}\" y=\"{Top + PlotHeight + 39:0.###}\" text-anchor=\"middle\" font-size=\"10\">{probeCounts[index]} probes</text>"));
        }

        string[] colors = ["#2563eb", "#dc2626", "#059669", "#7c3aed"];
        string[] algorithms = ordered.Select(static entry => entry.Interpolation).Distinct(StringComparer.Ordinal).ToArray();
        for (int algorithmIndex = 0; algorithmIndex < algorithms.Length; algorithmIndex++)
        {
            string algorithm = algorithms[algorithmIndex];
            MeshComparisonEntry[] series = ordered
                .Where(entry => string.Equals(entry.Interpolation, algorithm, StringComparison.Ordinal))
                .OrderBy(static entry => entry.ProbeCount)
                .ToArray();
            string color = colors[algorithmIndex % colors.Length];
            AppendSeries(builder, series, probeCounts, maximum, color, maximumAbsolute: false);
            AppendSeries(builder, series, probeCounts, maximum, color, maximumAbsolute: true);

            double legendY = 85 + (algorithmIndex * 48);
            builder.AppendLine(FormattableString.Invariant(
                $"  <line x1=\"650\" y1=\"{legendY}\" x2=\"680\" y2=\"{legendY}\" stroke=\"{color}\" stroke-width=\"2\"/>"));
            builder.AppendLine($"  <text x=\"688\" y=\"{F(legendY + 4)}\" font-size=\"11\">{Escape(algorithm)} RMSE</text>");
            builder.AppendLine(FormattableString.Invariant(
                $"  <line x1=\"650\" y1=\"{legendY + 20}\" x2=\"680\" y2=\"{legendY + 20}\" stroke=\"{color}\" stroke-width=\"2\" stroke-dasharray=\"5 4\"/>"));
            builder.AppendLine($"  <text x=\"688\" y=\"{F(legendY + 24)}\" font-size=\"11\">{Escape(algorithm)} max |error|</text>");
        }

        builder.AppendLine("""  <text x="355" y="590" text-anchor="middle" font-size="13">Probe mesh density</text>""");
        builder.AppendLine("""  <text x="20" y="280" transform="rotate(-90 20 280)" text-anchor="middle" font-size="13">Error (mm)</text>""");
        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static void AppendSeries(
        StringBuilder builder,
        IReadOnlyList<MeshComparisonEntry> series,
        IReadOnlyList<int> probeCounts,
        double maximum,
        string color,
        bool maximumAbsolute)
    {
        var path = new StringBuilder();
        foreach (MeshComparisonEntry entry in series)
        {
            int index = IndexOf(probeCounts, entry.ProbeCount);
            double x = X(index, probeCounts.Count);
            double value = maximumAbsolute ? entry.Metrics.MaximumAbsoluteError : entry.Metrics.RootMeanSquareError;
            double y = Top + PlotHeight - (value / maximum * PlotHeight);
            path.Append(path.Length == 0 ? "M " : " L ").Append(F(x)).Append(' ').Append(F(y));
        }

        string dash = maximumAbsolute ? " stroke-dasharray=\"5 4\"" : string.Empty;
        builder.AppendLine($"  <path d=\"{path}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\"{dash}/>");
        foreach (MeshComparisonEntry entry in series)
        {
            int index = IndexOf(probeCounts, entry.ProbeCount);
            double value = maximumAbsolute ? entry.Metrics.MaximumAbsoluteError : entry.Metrics.RootMeanSquareError;
            builder.AppendLine(FormattableString.Invariant(
                $"  <circle cx=\"{X(index, probeCounts.Count):0.###}\" cy=\"{Top + PlotHeight - (value / maximum * PlotHeight):0.###}\" r=\"3\" fill=\"{color}\"/>"));
        }
    }

    private static int IndexOf(IReadOnlyList<int> values, int value)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (values[index] == value)
            {
                return index;
            }
        }

        throw new InvalidOperationException("Probe count was not found.");
    }

    private static double X(int index, int count) =>
        count == 1 ? Left + (PlotWidth / 2) : Left + (index * PlotWidth / (count - 1));

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
}
