using Semver;

namespace Shield.Api.Services;

// Centralises every SemVer operation Shield does: trimming the noisy range prefixes that ship
// in dependency manifests, parsing strict or loose strings, picking the latest stable from a
// version list, comparing two candidates for "is newer" or "is major bump". Every other service
// should call into this helper instead of importing Semver directly — keeps the rules (prefix
// chars, prerelease skip, fallback behaviour) consistent across all ecosystems.
public static class SemVerHelper
{
    // Range-prefix chars commonly seen in package manifests across ecosystems: caret/tilde for
    // npm/composer, comparison ops for pip/cargo, equals for explicit pins, leading whitespace.
    private static readonly char[] RangePrefixes = ['^', '~', '>', '<', '=', ' '];

    // Some ecosystems prefix tags with 'v' (Go, Rust). Strip it before parsing.
    public static string Normalise(string version) =>
        version.TrimStart(RangePrefixes).TrimStart('v', 'V');

    public static bool TryParse(string version, out SemVersion? parsed) =>
        SemVersion.TryParse(Normalise(version), SemVersionStyles.Any, out parsed);

    // True when `candidate` is strictly higher precedence than `current`. Falls back to a raw
    // string-inequality check when either side fails to parse — better to surface "something
    // changed" than to silently swallow an unparseable version pair.
    public static bool IsNewer(string current, string candidate)
    {
        if (TryParse(current, out SemVersion? a) && TryParse(candidate, out SemVersion? b))
            return b!.ComparePrecedenceTo(a) > 0;
        return !string.Equals(current, candidate, StringComparison.Ordinal);
    }

    // True only when both versions parse AND candidate's major exceeds current's. Returns false
    // on parse failure — an ambiguous bump is treated as non-major so the conservative path
    // (allow it through) wins; callers that need stricter behaviour can re-check at their site.
    public static bool IsMajorBump(string current, string candidate)
    {
        if (TryParse(current, out SemVersion? a) && TryParse(candidate, out SemVersion? b))
            return b!.Major > a!.Major;
        return false;
    }

    // True only when both versions parse AND candidate's major matches current's. Used by the
    // Updates "latest-minor" scope so consumers can filter out major bumps without re-parsing.
    public static bool IsSameMajor(string current, string candidate)
    {
        if (TryParse(current, out SemVersion? a) && TryParse(candidate, out SemVersion? b))
            return b!.Major == a!.Major;
        return false;
    }

    // Picks the latest stable (non-prerelease) candidate from an arbitrary collection. Returns
    // null when nothing parses. Callers pass a version-extractor so they can keep the original
    // payload object (PackageMeta, NpmVersion, custom records) instead of being forced to project.
    public static T? PickLatestStable<T>(IEnumerable<T> candidates, Func<T, string?> getVersion)
    {
        T? best = default;
        SemVersion? bestSemver = null;
        foreach (T item in candidates)
        {
            string? raw = getVersion(item);
            if (string.IsNullOrEmpty(raw))
                continue;
            if (!TryParse(raw, out SemVersion? parsed))
                continue;
            if (parsed!.IsPrerelease)
                continue;
            if (bestSemver is null || parsed.ComparePrecedenceTo(bestSemver) > 0)
            {
                best = item;
                bestSemver = parsed;
            }
        }
        return best;
    }
}
