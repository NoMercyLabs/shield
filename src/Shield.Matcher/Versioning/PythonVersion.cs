using System.Text.RegularExpressions;

namespace Shield.Matcher.Versioning;

// PEP 440 version comparison. The Python spec is NOT SemVer:
//   https://peps.python.org/pep-0440/
//
// Canonical form:
//   [N!]N(.N)*[{a|b|c|rc|alpha|beta|pre|preview}N][.postN][.devN][+localversion]
//
// Key differences from SemVer the matcher must respect:
//   - Epochs: "1!2.0" > "9.0" because epoch 1 trumps epoch 0. Almost never used in practice
//     but a real CPython artefact (e.g. some Mercurial / Twisted historical packages).
//   - Pre-release ordering: dev < alpha/a < beta/b < rc/c/pre/preview < release.
//   - Post-releases sort ABOVE the release: "1.0.post1" > "1.0". SemVer has no analogue.
//   - Dev-releases sort BELOW everything else for the same release: "1.0.dev0" < "1.0a0".
//   - Local version "+local" is allowed but ignored for ordering against advisory ranges
//     (the spec defines local segments as "+local" suffix that ranks above the same base
//     but not against a different base — advisory ranges never include locals).
//   - Implicit/explicit "post" separator: "1.0-1" == "1.0.post1" == "1.0post1".
internal readonly struct PythonVersion : IComparable<PythonVersion>
{
    private readonly int _epoch;
    private readonly IReadOnlyList<int> _release;
    private readonly PreSegment? _pre;
    private readonly int? _post;
    private readonly int? _dev;
    private readonly string _local;

    private PythonVersion(
        int epoch,
        IReadOnlyList<int> release,
        PreSegment? pre,
        int? post,
        int? dev,
        string local
    )
    {
        _epoch = epoch;
        _release = release;
        _pre = pre;
        _post = post;
        _dev = dev;
        _local = local;
    }

    public static PythonVersion Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (!TryParse(raw, out PythonVersion parsed))
            throw new FormatException($"Not a valid PEP 440 version: '{raw}'.");
        return parsed;
    }

    public static bool TryParse(string raw, out PythonVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        // Strip optional leading "v" / "V" — PEP 440 permits it.
        string s = raw.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
            s = s[1..];

        Match match = Pattern.Match(s);
        if (!match.Success)
            return false;

        int epoch = match.Groups["epoch"].Success ? int.Parse(match.Groups["epoch"].Value) : 0;

        int[] release = match
            .Groups["release"]
            .Value.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();

        PreSegment? pre = null;
        if (match.Groups["preL"].Success)
        {
            int number = match.Groups["preN"].Success ? int.Parse(match.Groups["preN"].Value) : 0;
            pre = new(NormalisePreLabel(match.Groups["preL"].Value), number);
        }

        int? post = null;
        if (match.Groups["postN1"].Success)
            post = int.Parse(match.Groups["postN1"].Value);
        else if (match.Groups["postN2"].Success)
            post = int.Parse(match.Groups["postN2"].Value);
        else if (match.Groups["postL"].Success)
            // Bare ".post" or "-post" with no number means post0.
            post = 0;

        int? dev = match.Groups["devN"].Success ? int.Parse(match.Groups["devN"].Value) : null;
        if (match.Groups["devL"].Success && dev is null)
            dev = 0;

        string local = match.Groups["local"].Success
            ? match.Groups["local"].Value.ToLowerInvariant()
            : "";

        version = new(epoch, release, pre, post, dev, local);
        return true;
    }

    public int CompareTo(PythonVersion other)
    {
        int cmp = _epoch.CompareTo(other._epoch);
        if (cmp != 0)
            return cmp;

        cmp = CompareReleaseSegments(_release, other._release);
        if (cmp != 0)
            return cmp;

        // Per PEP 440: a release WITHOUT pre/post/dev is "final" and sorts above same-release
        // with pre, below same-release with post. Dev sorts below pre. The combinations:
        //   1.0.dev0 < 1.0a0 < 1.0a0.dev0 (no — dev0 of a0 < a0) ... canonical order:
        //     1.0.dev0  <  1.0a0.dev0  <  1.0a0  <  1.0b0  <  1.0rc0  <  1.0  <  1.0.post0
        // Easiest correct model: rank tuples (preRank, hasPre, postRank, devRank).
        cmp = CompareRelease(other);
        if (cmp != 0)
            return cmp;

        // Identical version+pre+post+dev — fall back to local string compare. Local "+abc"
        // ranks above same version without local per PEP 440. Local segments compare component
        // by component, numeric > non-numeric, missing rank below present.
        return CompareLocal(_local, other._local);
    }

    private int CompareRelease(PythonVersion other)
    {
        // Combined ordering of (pre, post, dev). PEP 440 defines:
        //   X.dev < X<pre>.dev < X<pre> < X < X.post.dev < X.post
        int leftKey = ReleaseKindKey(_pre, _post, _dev);
        int rightKey = ReleaseKindKey(other._pre, other._post, other._dev);
        if (leftKey != rightKey)
            return leftKey.CompareTo(rightKey);

        if (_pre is { } leftPre && other._pre is { } rightPre)
        {
            int preCmp = leftPre.CompareTo(rightPre);
            if (preCmp != 0)
                return preCmp;
        }

        int postCmp = NullableIntCompare(_post, other._post);
        if (postCmp != 0)
            return postCmp;

        return NullableIntCompare(_dev, other._dev);
    }

    // Ordering buckets — lower comes first.
    //  0: dev only (e.g. 1.0.dev0)
    //  1: pre + dev (1.0a0.dev0)
    //  2: pre (1.0a0)
    //  3: release (1.0)
    //  4: post + dev (1.0.post0.dev0)
    //  5: post (1.0.post0)
    private static int ReleaseKindKey(PreSegment? pre, int? post, int? dev)
    {
        if (post is not null)
            return dev is not null ? 4 : 5;
        if (pre is not null)
            return dev is not null ? 1 : 2;
        if (dev is not null)
            return 0;
        return 3;
    }

    private static int NullableIntCompare(int? a, int? b)
    {
        if (a is null && b is null)
            return 0;
        if (a is null)
            return -1;
        if (b is null)
            return 1;
        return a.Value.CompareTo(b.Value);
    }

    private static int CompareReleaseSegments(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        int len = Math.Max(a.Count, b.Count);
        for (int i = 0; i < len; i++)
        {
            int left = i < a.Count ? a[i] : 0;
            int right = i < b.Count ? b[i] : 0;
            if (left != right)
                return left.CompareTo(right);
        }
        return 0;
    }

    private static int CompareLocal(string a, string b)
    {
        // Identical or both empty.
        if (a == b)
            return 0;
        // A version with a local segment sorts above the same base without one.
        if (a.Length == 0)
            return -1;
        if (b.Length == 0)
            return 1;

        string[] partsA = a.Split('.');
        string[] partsB = b.Split('.');
        int len = Math.Max(partsA.Length, partsB.Length);
        for (int i = 0; i < len; i++)
        {
            string left = i < partsA.Length ? partsA[i] : "";
            string right = i < partsB.Length ? partsB[i] : "";

            bool leftNumeric = int.TryParse(left, out int leftN);
            bool rightNumeric = int.TryParse(right, out int rightN);

            if (leftNumeric && rightNumeric)
            {
                if (leftN != rightN)
                    return leftN.CompareTo(rightN);
                continue;
            }
            // Numeric segments rank above non-numeric.
            if (leftNumeric)
                return 1;
            if (rightNumeric)
                return -1;
            int cmp = string.CompareOrdinal(left, right);
            if (cmp != 0)
                return cmp;
        }
        return 0;
    }

    public static bool operator <(PythonVersion a, PythonVersion b) => a.CompareTo(b) < 0;

    public static bool operator >(PythonVersion a, PythonVersion b) => a.CompareTo(b) > 0;

    public static bool operator <=(PythonVersion a, PythonVersion b) => a.CompareTo(b) <= 0;

    public static bool operator >=(PythonVersion a, PythonVersion b) => a.CompareTo(b) >= 0;

    public static bool operator ==(PythonVersion a, PythonVersion b) => a.CompareTo(b) == 0;

    public static bool operator !=(PythonVersion a, PythonVersion b) => a.CompareTo(b) != 0;

    public override bool Equals(object? obj) => obj is PythonVersion other && CompareTo(other) == 0;

    public override int GetHashCode() => HashCode.Combine(_epoch, _release.Count);

    private readonly struct PreSegment : IComparable<PreSegment>
    {
        public string Label { get; }
        public int Number { get; }

        public PreSegment(string label, int number)
        {
            Label = label;
            Number = number;
        }

        // Pre-release labels in ascending rank. Anything outside the set (shouldn't happen
        // after normalisation) sorts below known labels by string compare.
        private static int Rank(string label) =>
            label switch
            {
                "alpha" => 0,
                "beta" => 1,
                "rc" => 2,
                _ => -1,
            };

        public int CompareTo(PreSegment other)
        {
            int leftRank = Rank(Label);
            int rightRank = Rank(other.Label);
            if (leftRank != rightRank)
                return leftRank.CompareTo(rightRank);
            return Number.CompareTo(other.Number);
        }
    }

    // Normalise pre-release labels per PEP 440:
    //   a | alpha       → "alpha"
    //   b | beta        → "beta"
    //   c | rc | pre | preview → "rc"
    private static string NormalisePreLabel(string raw)
    {
        string lower = raw.ToLowerInvariant();
        return lower switch
        {
            "a" or "alpha" => "alpha",
            "b" or "beta" => "beta",
            "c" or "rc" or "pre" or "preview" => "rc",
            _ => lower,
        };
    }

    // PEP 440 grammar — case-insensitive, single permissive regex. Optional separators between
    // release and pre/post/dev (".", "-", "_", or none). Local segment is dot-separated.
    private static readonly Regex Pattern = new(
        @"^
            (?:(?<epoch>\d+)!)?
            (?<release>\d+(?:\.\d+)*)
            (?:[._-]?
                (?<preL>a|alpha|b|beta|c|rc|pre|preview)
                [._-]?
                (?<preN>\d+)?
            )?
            (?:
                (?:[-._]?post[._-]?(?<postN1>\d+)?)
                |
                (?:[-_.]?(?<postL>post)(?<postN2>\d+)?)
                |
                (?:-(?<postN2>\d+))
            )?
            (?:[._-]?(?<devL>dev)(?<devN>\d+)?)?
            (?:\+(?<local>[a-z0-9]+(?:[._-][a-z0-9]+)*))?
        $",
        RegexOptions.IgnoreCase
            | RegexOptions.IgnorePatternWhitespace
            | RegexOptions.CultureInvariant
            | RegexOptions.Compiled
    );
}
