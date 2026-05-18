namespace Shield.Matcher.Versioning;

// RubyGems Gem::Version comparison. Reference: rubygems/version.rb in the RubyGems source.
//   https://github.com/rubygems/rubygems/blob/master/lib/rubygems/version.rb
//
// Rules that diverge from SemVer:
//   - Version is a dot-separated list of "segments". Each segment is either an integer or a
//     string (letters + digits). Strings come BEFORE integers at the same position, so any
//     version containing a string segment is a "pre-release" version: 1.0.a < 1.0.
//   - Integer segments compare numerically; string segments compare lexically.
//   - Trailing zero integer segments are normalised away: "1.0" == "1.0.0".
//   - When two versions share a prefix and one has additional integer segments equal to zero,
//     they're equal: "1.0.0" == "1". But "1.0.a" < "1.0.0" because string < integer.
//   - No epochs, no build metadata, no "+" syntax. RubyGems is the simplest of the lot once
//     you accept that segments are heterogeneous.
internal readonly struct GemVersion : IComparable<GemVersion>
{
    private readonly IReadOnlyList<Segment> _segments;

    private GemVersion(IReadOnlyList<Segment> segments)
    {
        _segments = segments;
    }

    public static GemVersion Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return new(Tokenize(raw.Trim()));
    }

    public int CompareTo(GemVersion other)
    {
        IReadOnlyList<Segment> a = _segments;
        IReadOnlyList<Segment> b = other._segments;
        int len = Math.Max(a.Count, b.Count);

        for (int i = 0; i < len; i++)
        {
            // When one side runs out, pad with integer 0. This is what makes 1.0 == 1.0.0:
            // both extend to [1, 0, 0] for comparison.
            Segment left = i < a.Count ? a[i] : Segment.Zero;
            Segment right = i < b.Count ? b[i] : Segment.Zero;
            int cmp = left.CompareTo(right);
            if (cmp != 0)
                return cmp;
        }
        return 0;
    }

    public static bool operator <(GemVersion a, GemVersion b) => a.CompareTo(b) < 0;

    public static bool operator >(GemVersion a, GemVersion b) => a.CompareTo(b) > 0;

    public static bool operator <=(GemVersion a, GemVersion b) => a.CompareTo(b) <= 0;

    public static bool operator >=(GemVersion a, GemVersion b) => a.CompareTo(b) >= 0;

    public static bool operator ==(GemVersion a, GemVersion b) => a.CompareTo(b) == 0;

    public static bool operator !=(GemVersion a, GemVersion b) => a.CompareTo(b) != 0;

    public override bool Equals(object? obj) => obj is GemVersion other && CompareTo(other) == 0;

    public override int GetHashCode() => _segments.Count;

    private readonly struct Segment : IComparable<Segment>
    {
        public bool IsNumeric { get; }
        public long Number { get; }
        public string Text { get; }

        private Segment(bool isNumeric, long number, string text)
        {
            IsNumeric = isNumeric;
            Number = number;
            Text = text;
        }

        public static Segment Integer(long value) => new(true, value, "");

        public static Segment String(string value) => new(false, 0, value);

        public static Segment Zero { get; } = Integer(0);

        public int CompareTo(Segment other)
        {
            // String segments sort BELOW integer segments — that's what makes "1.0.alpha" < "1.0".
            if (IsNumeric && other.IsNumeric)
                return Number.CompareTo(other.Number);
            if (IsNumeric)
                return 1;
            if (other.IsNumeric)
                return -1;
            return string.CompareOrdinal(Text, other.Text);
        }
    }

    private static IReadOnlyList<Segment> Tokenize(string raw)
    {
        // RubyGems tokenisation: split on '.' and on letter↔digit transitions. So "1.0.a1"
        // becomes [1, 0, "a", 1]; "1.0.alpha" becomes [1, 0, "alpha"]; "1.0-pre.1" — RubyGems
        // doesn't use '-', but a stray dash separator is treated as '.' to match Bundler's
        // permissive parser.
        List<Segment> segments = [];
        string normalised = raw.Replace('-', '.');
        int index = 0;
        while (index < normalised.Length)
        {
            char c = normalised[index];
            if (c == '.')
            {
                index++;
                continue;
            }
            if (char.IsDigit(c))
            {
                int start = index;
                while (index < normalised.Length && char.IsDigit(normalised[index]))
                    index++;
                if (long.TryParse(normalised[start..index], out long value))
                    segments.Add(Segment.Integer(value));
                else
                    segments.Add(Segment.String(normalised[start..index]));
            }
            else if (char.IsLetter(c))
            {
                int start = index;
                while (index < normalised.Length && char.IsLetter(normalised[index]))
                    index++;
                segments.Add(Segment.String(normalised[start..index]));
            }
            else
            {
                // Unknown punctuation — skip to avoid infinite loop on malformed inputs.
                index++;
            }
        }

        // Trim trailing zero integer segments so "1.0" == "1.0.0". String segments stop the
        // trim — "1.0.a" is a pre-release of 1.0, not 1.0 itself.
        while (segments.Count > 0 && segments[^1].IsNumeric && segments[^1].Number == 0)
            segments.RemoveAt(segments.Count - 1);

        return segments;
    }
}
