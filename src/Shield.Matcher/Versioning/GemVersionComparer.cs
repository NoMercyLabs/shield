using Shield.Core.Domain;

namespace Shield.Matcher.Versioning;

public sealed class GemVersionComparer : IVersionComparer
{
    public Ecosystem Ecosystem => Ecosystem.RubyGems;

    public bool Satisfies(string version, VersionRange range)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(range);

        GemVersion candidate = GemVersion.Parse(version);

        if (range.Exact is { Count: > 0 })
        {
            foreach (string exact in range.Exact)
            {
                if (candidate == GemVersion.Parse(exact))
                    return true;
            }
            return false;
        }

        if (range.GtOrEq is not null && candidate < GemVersion.Parse(range.GtOrEq))
            return false;
        if (
            range.GtOrEqExclusive is not null
            && candidate <= GemVersion.Parse(range.GtOrEqExclusive)
        )
            return false;
        if (range.Lt is not null && candidate >= GemVersion.Parse(range.Lt))
            return false;
        if (range.LtOrEq is not null && candidate > GemVersion.Parse(range.LtOrEq))
            return false;

        return true;
    }
}
