using System.Text.RegularExpressions;
using Shield.Core.Domain;
using Shield.Feeds.Osv.Models;

namespace Shield.Feeds.Osv;

internal static class OsvSeverityMapper
{
    private static readonly Regex CvssScorePattern = new(@"/(?<score>\d+(?:\.\d+)?)(?:/|$)", RegexOptions.Compiled);

    public static (Severity Severity, double? Cvss) Map(OsvVulnerability vuln)
    {
        double? cvss = TryParseCvssScore(vuln.Severity);
        if (cvss.HasValue)
            return (FromCvss(cvss.Value), cvss);

        string? databaseSpecific = vuln.DatabaseSpecific?.Severity;
        if (!string.IsNullOrWhiteSpace(databaseSpecific))
            return (FromString(databaseSpecific), null);

        return (Severity.Low, null);
    }

    private static double? TryParseCvssScore(IReadOnlyList<OsvSeverity>? entries)
    {
        if (entries is null || entries.Count == 0) return null;
        foreach (OsvSeverity entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Score)) continue;

            if (double.TryParse(entry.Score, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double direct))
                return direct;

            Match match = CvssScorePattern.Match(entry.Score);
            if (match.Success && double.TryParse(match.Groups["score"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                return parsed;
        }
        return null;
    }

    private static Severity FromCvss(double score) => score switch
    {
        >= 9.0 => Severity.Critical,
        >= 7.0 => Severity.High,
        >= 4.0 => Severity.Medium,
        _ => Severity.Low
    };

    private static Severity FromString(string label) => label.Trim().ToUpperInvariant() switch
    {
        "CRITICAL" => Severity.Critical,
        "HIGH" => Severity.High,
        "MODERATE" or "MEDIUM" => Severity.Medium,
        _ => Severity.Low
    };
}
