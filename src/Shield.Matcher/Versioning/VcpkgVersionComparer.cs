using Shield.Core.Domain;

namespace Shield.Matcher.Versioning;

public sealed class VcpkgVersionComparer : IVersionComparer
{
    public Ecosystem Ecosystem => Ecosystem.Vcpkg;

    public bool Satisfies(string version, VersionRange range)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(range);

        VcpkgVersion candidate = VcpkgVersion.Parse(version);

        if (range.Exact is { Count: > 0 })
        {
            foreach (string exact in range.Exact)
            {
                if (candidate == VcpkgVersion.Parse(exact))
                    return true;
            }
            return false;
        }

        if (range.GtOrEq is not null && candidate < VcpkgVersion.Parse(range.GtOrEq))
            return false;
        if (
            range.GtOrEqExclusive is not null
            && candidate <= VcpkgVersion.Parse(range.GtOrEqExclusive)
        )
            return false;
        if (range.Lt is not null && candidate >= VcpkgVersion.Parse(range.Lt))
            return false;
        if (range.LtOrEq is not null && candidate > VcpkgVersion.Parse(range.LtOrEq))
            return false;

        return true;
    }
}
