using Semver;
using Shield.Core.Domain;

namespace Shield.Matcher.Versioning;

public sealed class SemverVersionComparer : IVersionComparer
{
    public Ecosystem Ecosystem { get; }

    public SemverVersionComparer(Ecosystem ecosystem)
    {
        Ecosystem = ecosystem;
    }

    public bool Satisfies(string version, VersionRange range)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(range);

        if (!TryParse(version, out SemVersion candidate))
            return false;

        if (range.Exact is { Count: > 0 })
        {
            foreach (string exact in range.Exact)
            {
                if (
                    TryParse(exact, out SemVersion exactVersion)
                    && candidate.ComparePrecedenceTo(exactVersion) == 0
                )
                    return true;
            }
            return false;
        }

        if (range.GtOrEq is not null)
        {
            if (!TryParse(range.GtOrEq, out SemVersion lower))
                return false;
            if (candidate.ComparePrecedenceTo(lower) < 0)
                return false;
        }

        if (range.GtOrEqExclusive is not null)
        {
            if (!TryParse(range.GtOrEqExclusive, out SemVersion lowerExclusive))
                return false;
            if (candidate.ComparePrecedenceTo(lowerExclusive) <= 0)
                return false;
        }

        if (range.Lt is not null)
        {
            if (!TryParse(range.Lt, out SemVersion upper))
                return false;
            if (candidate.ComparePrecedenceTo(upper) >= 0)
                return false;
        }

        if (range.LtOrEq is not null)
        {
            if (!TryParse(range.LtOrEq, out SemVersion upperInclusive))
                return false;
            if (candidate.ComparePrecedenceTo(upperInclusive) > 0)
                return false;
        }

        return true;
    }

    private static bool TryParse(string raw, out SemVersion parsed)
    {
        string trimmed = raw.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
            trimmed = trimmed[1..];

        return SemVersion.TryParse(trimmed, SemVersionStyles.Any, out parsed!);
    }
}
