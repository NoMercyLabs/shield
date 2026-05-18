using Shield.Core.Domain;

namespace Shield.Matcher.Versioning;

public sealed class PythonVersionComparer : IVersionComparer
{
    public Ecosystem Ecosystem => Ecosystem.Python;

    public bool Satisfies(string version, VersionRange range)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(range);

        if (!PythonVersion.TryParse(version, out PythonVersion candidate))
            return false;

        if (range.Exact is { Count: > 0 })
        {
            foreach (string exact in range.Exact)
            {
                if (
                    PythonVersion.TryParse(exact, out PythonVersion exactVersion)
                    && candidate == exactVersion
                )
                    return true;
            }
            return false;
        }

        if (range.GtOrEq is not null)
        {
            if (!PythonVersion.TryParse(range.GtOrEq, out PythonVersion lower))
                return false;
            if (candidate < lower)
                return false;
        }

        if (range.GtOrEqExclusive is not null)
        {
            if (!PythonVersion.TryParse(range.GtOrEqExclusive, out PythonVersion lowerExclusive))
                return false;
            if (candidate <= lowerExclusive)
                return false;
        }

        if (range.Lt is not null)
        {
            if (!PythonVersion.TryParse(range.Lt, out PythonVersion upper))
                return false;
            if (candidate >= upper)
                return false;
        }

        if (range.LtOrEq is not null)
        {
            if (!PythonVersion.TryParse(range.LtOrEq, out PythonVersion upperInclusive))
                return false;
            if (candidate > upperInclusive)
                return false;
        }

        return true;
    }
}
