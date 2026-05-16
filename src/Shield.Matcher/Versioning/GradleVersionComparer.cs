using NuGet.Versioning;
using Shield.Core.Domain;

namespace Shield.Matcher.Versioning;

public sealed class GradleVersionComparer : IVersionComparer
{
    public Ecosystem Ecosystem => Ecosystem.Gradle;

    public bool Satisfies(string version, VersionRange range)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(range);

        string normalizedCandidate = NormalizeMaven(version);
        if (!NuGetVersion.TryParse(normalizedCandidate, out NuGetVersion? candidate))
            return false;

        if (range.Exact is { Count: > 0 })
        {
            foreach (string exact in range.Exact)
            {
                if (NuGetVersion.TryParse(NormalizeMaven(exact), out NuGetVersion? exactVersion) &&
                    candidate == exactVersion)
                    return true;
            }
            return false;
        }

        if (range.GtOrEq is not null)
        {
            if (!NuGetVersion.TryParse(NormalizeMaven(range.GtOrEq), out NuGetVersion? lower))
                return false;
            if (candidate < lower)
                return false;
        }

        if (range.GtOrEqExclusive is not null)
        {
            if (!NuGetVersion.TryParse(NormalizeMaven(range.GtOrEqExclusive), out NuGetVersion? lowerExclusive))
                return false;
            if (candidate <= lowerExclusive)
                return false;
        }

        if (range.Lt is not null)
        {
            if (!NuGetVersion.TryParse(NormalizeMaven(range.Lt), out NuGetVersion? upper))
                return false;
            if (candidate >= upper)
                return false;
        }

        if (range.LtOrEq is not null)
        {
            if (!NuGetVersion.TryParse(NormalizeMaven(range.LtOrEq), out NuGetVersion? upperInclusive))
                return false;
            if (candidate > upperInclusive)
                return false;
        }

        return true;
    }

    // Maven `1.0-SNAPSHOT` → SemVer `1.0.0-SNAPSHOT`; bare `1.0` → `1.0.0`.
    private static string NormalizeMaven(string raw)
    {
        string trimmed = raw.Trim();
        int dashIndex = trimmed.IndexOf('-');
        string core = dashIndex >= 0 ? trimmed[..dashIndex] : trimmed;
        string suffix = dashIndex >= 0 ? trimmed[dashIndex..] : string.Empty;

        int dotCount = core.Count(c => c == '.');
        while (dotCount < 2)
        {
            core += ".0";
            dotCount++;
        }

        return core + suffix;
    }
}
