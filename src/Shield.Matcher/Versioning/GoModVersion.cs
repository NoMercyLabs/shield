using System.Globalization;

namespace Shield.Matcher.Versioning;

// Go module version comparison. Reference:
//   https://go.dev/ref/mod#versions
//   https://semver.org/spec/v2.0.0.html (Go is SemVer 2.0 with Go-specific extensions)
//
// Format: "v" prefix is REQUIRED on canonical Go versions (e.g. v1.2.3, v0.0.0-...).
// The matcher accepts both "v1.2.3" and "1.2.3" since OSV advisory rows sometimes drop
// the prefix.
//
// Go extensions over plain SemVer:
//   - +incompatible suffix: v2.0.0+incompatible flags v2+ modules that did NOT adopt the
//     /v2 import-path convention. The suffix is part of the version, not build metadata —
//     it MUST be preserved for matching.
//   - Pseudo-versions: v0.0.0-20191023135150-abc1234def56 encode "no tagged release, here's
//     a commit". They sort as a pre-release of the base version (v0.0.0) and the timestamp
//     segment provides ordering between two pseudo-versions on the same base.
//   - Pre-releases follow SemVer rules (1.0.0-alpha < 1.0.0).
internal readonly struct GoModVersion : IComparable<GoModVersion>
{
    private readonly long _major;
    private readonly long _minor;
    private readonly long _patch;
    private readonly string _preRelease;
    private readonly bool _incompatible;

    private GoModVersion(long major, long minor, long patch, string preRelease, bool incompatible)
    {
        _major = major;
        _minor = minor;
        _patch = patch;
        _preRelease = preRelease;
        _incompatible = incompatible;
    }

    public static GoModVersion Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (!TryParse(raw, out GoModVersion v))
            throw new FormatException($"Not a valid Go module version: '{raw}'.");
        return v;
    }

    public static bool TryParse(string raw, out GoModVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string s = raw.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
            s = s[1..];

        // Split off +incompatible suffix. Any other build metadata (+sha256:...) is dropped
        // per SemVer, but Go specifically requires +incompatible to participate in compare.
        bool incompatible = false;
        int plusIndex = s.IndexOf('+');
        if (plusIndex >= 0)
        {
            string buildMeta = s[(plusIndex + 1)..];
            if (buildMeta.Equals("incompatible", StringComparison.OrdinalIgnoreCase))
                incompatible = true;
            s = s[..plusIndex];
        }

        // Split pre-release. Pseudo-versions look like "0.0.0-20191023135150-abc1234def56"
        // — the hyphen-prefixed remainder is treated as the SemVer pre-release segment.
        string preRelease = "";
        int dashIndex = s.IndexOf('-');
        if (dashIndex >= 0)
        {
            preRelease = s[(dashIndex + 1)..];
            s = s[..dashIndex];
        }

        string[] parts = s.Split('.');
        if (parts.Length < 1 || parts.Length > 3)
            return false;

        if (
            !long.TryParse(
                parts[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long major
            )
        )
            return false;

        long minor = 0;
        long patch = 0;
        if (
            parts.Length >= 2
            && !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor)
        )
            return false;
        if (
            parts.Length >= 3
            && !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch)
        )
            return false;

        version = new(major, minor, patch, preRelease, incompatible);
        return true;
    }

    public int CompareTo(GoModVersion other)
    {
        int cmp = _major.CompareTo(other._major);
        if (cmp != 0)
            return cmp;
        cmp = _minor.CompareTo(other._minor);
        if (cmp != 0)
            return cmp;
        cmp = _patch.CompareTo(other._patch);
        if (cmp != 0)
            return cmp;

        // Pre-release present sorts BELOW no-prerelease. Two prereleases compare per SemVer:
        // segment-wise, numeric < alphanumeric only when both numeric, otherwise lex compare.
        cmp = ComparePreRelease(_preRelease, other._preRelease);
        if (cmp != 0)
            return cmp;

        // +incompatible sorts ABOVE non-incompatible at the same base. This is Go-specific.
        return _incompatible.CompareTo(other._incompatible);
    }

    private static int ComparePreRelease(string a, string b)
    {
        // Both empty — equal release versions.
        if (a.Length == 0 && b.Length == 0)
            return 0;
        // Per SemVer, absence of pre-release ranks HIGHER than presence.
        if (a.Length == 0)
            return 1;
        if (b.Length == 0)
            return -1;

        string[] partsA = a.Split('.');
        string[] partsB = b.Split('.');
        int len = Math.Max(partsA.Length, partsB.Length);
        for (int i = 0; i < len; i++)
        {
            if (i >= partsA.Length)
                return -1;
            if (i >= partsB.Length)
                return 1;

            string left = partsA[i];
            string right = partsB[i];

            bool leftNumeric = long.TryParse(
                left,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long leftN
            );
            bool rightNumeric = long.TryParse(
                right,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long rightN
            );

            if (leftNumeric && rightNumeric)
            {
                if (leftN != rightN)
                    return leftN.CompareTo(rightN);
                continue;
            }
            // Numeric segments rank LOWER than alphanumeric (SemVer 2 §11.4.3).
            if (leftNumeric)
                return -1;
            if (rightNumeric)
                return 1;
            int cmp = string.CompareOrdinal(left, right);
            if (cmp != 0)
                return cmp;
        }
        return 0;
    }

    public static bool operator <(GoModVersion a, GoModVersion b) => a.CompareTo(b) < 0;

    public static bool operator >(GoModVersion a, GoModVersion b) => a.CompareTo(b) > 0;

    public static bool operator <=(GoModVersion a, GoModVersion b) => a.CompareTo(b) <= 0;

    public static bool operator >=(GoModVersion a, GoModVersion b) => a.CompareTo(b) >= 0;

    public static bool operator ==(GoModVersion a, GoModVersion b) => a.CompareTo(b) == 0;

    public static bool operator !=(GoModVersion a, GoModVersion b) => a.CompareTo(b) != 0;

    public override bool Equals(object? obj) => obj is GoModVersion other && CompareTo(other) == 0;

    public override int GetHashCode() => HashCode.Combine(_major, _minor, _patch);
}
