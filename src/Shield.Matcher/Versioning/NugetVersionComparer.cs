using NuGet.Versioning;
using Shield.Core.Domain;

namespace Shield.Matcher.Versioning;

public sealed class NugetVersionComparer : IVersionComparer
{
    public Ecosystem Ecosystem => Ecosystem.Nuget;

    public bool Satisfies(string version, VersionRange range)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(range);

        if (!NuGetVersion.TryParse(version, out NuGetVersion? candidate))
            return false;

        if (range.Exact is { Count: > 0 })
        {
            foreach (string exact in range.Exact)
            {
                if (
                    NuGetVersion.TryParse(exact, out NuGetVersion? exactVersion)
                    && candidate == exactVersion
                )
                    return true;
            }
            return false;
        }

        if (range.GtOrEq is not null)
        {
            if (!NuGetVersion.TryParse(range.GtOrEq, out NuGetVersion? lower))
                return false;
            if (candidate < lower)
                return false;
        }

        if (range.GtOrEqExclusive is not null)
        {
            if (!NuGetVersion.TryParse(range.GtOrEqExclusive, out NuGetVersion? lowerExclusive))
                return false;
            if (candidate <= lowerExclusive)
                return false;
        }

        if (range.Lt is not null)
        {
            if (!NuGetVersion.TryParse(range.Lt, out NuGetVersion? upper))
                return false;
            if (candidate >= upper)
                return false;
        }

        if (range.LtOrEq is not null)
        {
            if (!NuGetVersion.TryParse(range.LtOrEq, out NuGetVersion? upperInclusive))
                return false;
            if (candidate > upperInclusive)
                return false;
        }

        return true;
    }

    public static VersionRange? ParseNuGetRangeNotation(string notation)
    {
        ArgumentNullException.ThrowIfNull(notation);
        if (
            !NuGet.Versioning.VersionRange.TryParse(
                notation,
                out NuGet.Versioning.VersionRange? parsed
            )
        )
            return null;

        string? gtOrEq = null;
        string? gtOrEqExclusive = null;
        string? lt = null;
        string? ltOrEq = null;
        IReadOnlyList<string>? exact = null;

        if (parsed.HasLowerBound)
        {
            string lowerString = parsed.MinVersion!.ToNormalizedString();
            if (parsed.IsMinInclusive)
                gtOrEq = lowerString;
            else
                gtOrEqExclusive = lowerString;
        }

        if (parsed.HasUpperBound)
        {
            string upperString = parsed.MaxVersion!.ToNormalizedString();
            if (parsed.IsMaxInclusive)
                ltOrEq = upperString;
            else
                lt = upperString;
        }

        if (
            parsed.HasLowerBound
            && parsed.HasUpperBound
            && parsed.IsMinInclusive
            && parsed.IsMaxInclusive
            && parsed.MinVersion == parsed.MaxVersion
        )
        {
            exact = [parsed.MinVersion!.ToNormalizedString()];
            gtOrEq = null;
            ltOrEq = null;
        }

        return new(
            GtOrEq: gtOrEq,
            Lt: lt,
            GtOrEqExclusive: gtOrEqExclusive,
            LtOrEq: ltOrEq,
            Exact: exact
        );
    }
}
