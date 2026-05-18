using Shield.Core.Domain;

namespace Shield.Matcher.Versioning;

public sealed class GoModVersionComparer : IVersionComparer
{
    public Ecosystem Ecosystem => Ecosystem.Go;

    public bool Satisfies(string version, VersionRange range)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(range);

        if (!GoModVersion.TryParse(version, out GoModVersion candidate))
            return false;

        if (range.Exact is { Count: > 0 })
        {
            foreach (string exact in range.Exact)
            {
                if (
                    GoModVersion.TryParse(exact, out GoModVersion exactVersion)
                    && candidate == exactVersion
                )
                    return true;
            }
            return false;
        }

        if (range.GtOrEq is not null)
        {
            if (!GoModVersion.TryParse(range.GtOrEq, out GoModVersion lower))
                return false;
            if (candidate < lower)
                return false;
        }

        if (range.GtOrEqExclusive is not null)
        {
            if (!GoModVersion.TryParse(range.GtOrEqExclusive, out GoModVersion lowerExclusive))
                return false;
            if (candidate <= lowerExclusive)
                return false;
        }

        if (range.Lt is not null)
        {
            if (!GoModVersion.TryParse(range.Lt, out GoModVersion upper))
                return false;
            if (candidate >= upper)
                return false;
        }

        if (range.LtOrEq is not null)
        {
            if (!GoModVersion.TryParse(range.LtOrEq, out GoModVersion upperInclusive))
                return false;
            if (candidate > upperInclusive)
                return false;
        }

        return true;
    }
}
