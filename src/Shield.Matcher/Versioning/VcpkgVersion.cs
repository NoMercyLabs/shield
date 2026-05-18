using System.Globalization;

namespace Shield.Matcher.Versioning;

// vcpkg version comparison. Reference:
//   https://learn.microsoft.com/en-us/vcpkg/users/versioning
//
// vcpkg has FOUR version schemes — declared in vcpkg.json as `version`, `version-semver`,
// `version-date`, or `version-string`. The OSV/GHSA advisory feeds report the underlying
// version literal without telling us which scheme was originally declared, so the matcher
// has to detect the shape from the string itself:
//
//   - version-semver: standard SemVer 2 (compares numerically segment by segment, with
//     SemVer pre-release rules).
//   - version-date: yyyy-MM-dd[.NNN] (compares lexically — yyyy-MM-dd is sortable as text).
//   - version (relaxed): N(.N)* digit sequences with no pre-release tag — compares
//     numerically by segment.
//   - version-string: opaque label, compares lexically.
//
// Every scheme is then suffixed with an optional `#<port-version>` integer. Port-version
// 0 is omitted, all other integers count for tie-breaking after the base version compares
// equal. So `1.2.3#5` > `1.2.3` > `1.2.2#99`.
internal readonly struct VcpkgVersion : IComparable<VcpkgVersion>
{
    private readonly Scheme _scheme;
    private readonly IReadOnlyList<long> _numericSegments;
    private readonly string _stringValue;
    private readonly string _preRelease;
    private readonly int _portVersion;

    private VcpkgVersion(
        Scheme scheme,
        IReadOnlyList<long> numericSegments,
        string stringValue,
        string preRelease,
        int portVersion
    )
    {
        _scheme = scheme;
        _numericSegments = numericSegments;
        _stringValue = stringValue;
        _preRelease = preRelease;
        _portVersion = portVersion;
    }

    public static VcpkgVersion Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return Tokenize(raw.Trim());
    }

    public int CompareTo(VcpkgVersion other)
    {
        // Semver and Relaxed are both numeric-segment schemes — cross-compare them via
        // numeric segments. Semver brings a possible pre-release segment that Relaxed
        // lacks; an empty pre-release on one side compares as "release" (higher than any
        // pre-release per SemVer §11.4.4).
        bool leftNumeric = _scheme is Scheme.Semver or Scheme.Relaxed;
        bool rightNumeric = other._scheme is Scheme.Semver or Scheme.Relaxed;

        int cmp;
        if (leftNumeric && rightNumeric)
        {
            cmp = CompareSemver(other);
        }
        else if (_scheme != other._scheme)
        {
            // Genuinely different shapes (date vs numeric, string vs numeric). Fall back
            // to scheme rank for a stable but undefined ordering — operators see this
            // and it's never the result of a real advisory match.
            return _scheme.CompareTo(other._scheme);
        }
        else
        {
            cmp = _scheme switch
            {
                Scheme.Date => string.CompareOrdinal(_stringValue, other._stringValue),
                _ => string.CompareOrdinal(_stringValue, other._stringValue),
            };
        }

        if (cmp != 0)
            return cmp;

        return _portVersion.CompareTo(other._portVersion);
    }

    private int CompareSemver(VcpkgVersion other)
    {
        int cmp = CompareNumericSegments(other);
        if (cmp != 0)
            return cmp;

        if (_preRelease.Length == 0 && other._preRelease.Length == 0)
            return 0;
        if (_preRelease.Length == 0)
            return 1;
        if (other._preRelease.Length == 0)
            return -1;
        return ComparePreReleaseSegments(_preRelease, other._preRelease);
    }

    private int CompareNumericSegments(VcpkgVersion other)
    {
        int len = Math.Max(_numericSegments.Count, other._numericSegments.Count);
        for (int i = 0; i < len; i++)
        {
            long left = i < _numericSegments.Count ? _numericSegments[i] : 0;
            long right = i < other._numericSegments.Count ? other._numericSegments[i] : 0;
            if (left != right)
                return left.CompareTo(right);
        }
        return 0;
    }

    private static int ComparePreReleaseSegments(string a, string b)
    {
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
            bool leftN = long.TryParse(
                left,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long ln
            );
            bool rightN = long.TryParse(
                right,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long rn
            );
            if (leftN && rightN)
            {
                if (ln != rn)
                    return ln.CompareTo(rn);
                continue;
            }
            if (leftN)
                return -1;
            if (rightN)
                return 1;
            int cmp = string.CompareOrdinal(left, right);
            if (cmp != 0)
                return cmp;
        }
        return 0;
    }

    public static bool operator <(VcpkgVersion a, VcpkgVersion b) => a.CompareTo(b) < 0;

    public static bool operator >(VcpkgVersion a, VcpkgVersion b) => a.CompareTo(b) > 0;

    public static bool operator <=(VcpkgVersion a, VcpkgVersion b) => a.CompareTo(b) <= 0;

    public static bool operator >=(VcpkgVersion a, VcpkgVersion b) => a.CompareTo(b) >= 0;

    public static bool operator ==(VcpkgVersion a, VcpkgVersion b) => a.CompareTo(b) == 0;

    public static bool operator !=(VcpkgVersion a, VcpkgVersion b) => a.CompareTo(b) != 0;

    public override bool Equals(object? obj) => obj is VcpkgVersion other && CompareTo(other) == 0;

    public override int GetHashCode() => HashCode.Combine(_scheme, _stringValue, _portVersion);

    private enum Scheme
    {
        // Order matters for cross-scheme fallback: relaxed/numeric first, then semver,
        // then date, then opaque string.
        Relaxed = 0,
        Semver = 1,
        Date = 2,
        String = 3,
    }

    private static VcpkgVersion Tokenize(string raw)
    {
        // Split off port-version suffix `#N`.
        int portVersion = 0;
        int hashIndex = raw.IndexOf('#');
        string baseVersion = raw;
        if (hashIndex >= 0)
        {
            string portStr = raw[(hashIndex + 1)..];
            if (
                int.TryParse(
                    portStr,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int parsed
                )
            )
                portVersion = parsed;
            baseVersion = raw[..hashIndex];
        }

        // Detect scheme by shape.
        Scheme scheme;
        IReadOnlyList<long> numeric = Array.Empty<long>();
        string stringValue = baseVersion;
        string preRelease = "";

        if (IsDate(baseVersion))
        {
            scheme = Scheme.Date;
        }
        else if (TryParseRelaxed(baseVersion, out numeric))
        {
            // Plain numeric — Relaxed scheme. Trim trailing zero segments so 1.2 == 1.2.0
            // == 1.2.0.0, mirroring SemVer's behaviour and what callers expect from a "no
            // qualifier" version.
            numeric = TrimTrailingZeros(numeric);
            scheme = Scheme.Relaxed;
        }
        else if (TryParseSemver(baseVersion, out numeric, out preRelease))
        {
            scheme = Scheme.Semver;
        }
        else
        {
            scheme = Scheme.String;
        }

        return new(scheme, numeric, stringValue, preRelease, portVersion);
    }

    // version-date is "yyyy-MM-dd" optionally followed by ".N" (e.g. 2024-03-15.2).
    private static bool IsDate(string s)
    {
        if (s.Length < 10)
            return false;
        if (s[4] != '-' || s[7] != '-')
            return false;
        for (int i = 0; i < 10; i++)
        {
            if (i is 4 or 7)
                continue;
            if (!char.IsDigit(s[i]))
                return false;
        }
        if (s.Length == 10)
            return true;
        if (s[10] != '.')
            return false;
        for (int i = 11; i < s.Length; i++)
        {
            if (!char.IsDigit(s[i]))
                return false;
        }
        return true;
    }

    private static bool TryParseSemver(
        string s,
        out IReadOnlyList<long> numeric,
        out string preRelease
    )
    {
        numeric = Array.Empty<long>();
        preRelease = "";

        // Drop build metadata for ordering (SemVer 2 §10).
        int plusIndex = s.IndexOf('+');
        if (plusIndex >= 0)
            s = s[..plusIndex];

        int dashIndex = s.IndexOf('-');
        string corePart = dashIndex >= 0 ? s[..dashIndex] : s;
        string prePart = dashIndex >= 0 ? s[(dashIndex + 1)..] : "";

        string[] parts = corePart.Split('.');
        // Strict SemVer: MAJOR.MINOR.PATCH. We accept 1-3 segments for the matcher.
        if (parts.Length < 1 || parts.Length > 3)
            return false;
        long[] values = new long[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (
                !long.TryParse(
                    parts[i],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out values[i]
                )
            )
                return false;
        }
        // Only call this Semver if there's a pre-release segment OR exactly 3 parts.
        if (prePart.Length == 0 && parts.Length != 3)
            return false;
        numeric = values;
        preRelease = prePart;
        return true;
    }

    private static IReadOnlyList<long> TrimTrailingZeros(IReadOnlyList<long> segments)
    {
        int last = segments.Count - 1;
        while (last >= 0 && segments[last] == 0)
            last--;
        if (last == segments.Count - 1)
            return segments;
        long[] trimmed = new long[last + 1];
        for (int i = 0; i <= last; i++)
            trimmed[i] = segments[i];
        return trimmed;
    }

    private static bool TryParseRelaxed(string s, out IReadOnlyList<long> numeric)
    {
        numeric = Array.Empty<long>();
        if (s.Length == 0)
            return false;
        string[] parts = s.Split('.');
        long[] values = new long[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (
                !long.TryParse(
                    parts[i],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out values[i]
                )
            )
                return false;
        }
        numeric = values;
        return true;
    }
}
