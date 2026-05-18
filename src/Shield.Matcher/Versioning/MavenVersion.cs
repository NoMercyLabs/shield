namespace Shield.Matcher.Versioning;

// Maven/Gradle version comparison per Maven Resolver semantics, NOT SemVer. The key
// differences from SemVer:
//   - Qualifier ordering is explicit, not alphabetical:
//       snapshot < alpha < beta < milestone < rc < cr < release/final/ga (= zero) < sp
//     so 1.0-rc1 < 1.0 < 1.0-sp1 (SemVer would order 1.0-rc1 < 1.0 but treat sp as a
//     pre-release lower than 1.0).
//   - Trailing .0 segments are ignored: 1.0 == 1.0.0 == 1.0.0.0.
//   - Hyphen and dot can separate qualifiers: 1.0-alpha-1 and 1.0.alpha.1 are equivalent.
//   - "ga", "final", "release" all sort equal to "no qualifier".
//   - Unknown qualifiers sort lexically and AFTER all known qualifiers.
//
// Implements IComparable<MavenVersion> so callers can do direct comparisons.
internal readonly struct MavenVersion : IComparable<MavenVersion>
{
    private readonly IReadOnlyList<Item> _items;

    private MavenVersion(IReadOnlyList<Item> items)
    {
        _items = items;
    }

    public static MavenVersion Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return new(Tokenize(raw.Trim().ToLowerInvariant()));
    }

    public int CompareTo(MavenVersion other)
    {
        IReadOnlyList<Item> a = _items;
        IReadOnlyList<Item> b = other._items;
        int len = Math.Max(a.Count, b.Count);
        for (int i = 0; i < len; i++)
        {
            Item left = i < a.Count ? a[i] : Item.Zero(b[i].Kind);
            Item right = i < b.Count ? b[i] : Item.Zero(left.Kind);
            int cmp = left.CompareTo(right);
            if (cmp != 0)
                return cmp;
        }
        return 0;
    }

    public static bool operator <(MavenVersion a, MavenVersion b) => a.CompareTo(b) < 0;

    public static bool operator >(MavenVersion a, MavenVersion b) => a.CompareTo(b) > 0;

    public static bool operator <=(MavenVersion a, MavenVersion b) => a.CompareTo(b) <= 0;

    public static bool operator >=(MavenVersion a, MavenVersion b) => a.CompareTo(b) >= 0;

    public static bool operator ==(MavenVersion a, MavenVersion b) => a.CompareTo(b) == 0;

    public static bool operator !=(MavenVersion a, MavenVersion b) => a.CompareTo(b) != 0;

    public override bool Equals(object? obj) => obj is MavenVersion other && CompareTo(other) == 0;

    public override int GetHashCode() => _items.Count;

    private enum Kind
    {
        Integer,
        Qualifier,
    }

    private readonly struct Item : IComparable<Item>
    {
        public Kind Kind { get; }
        public long IntegerValue { get; }
        public string QualifierValue { get; }

        private Item(Kind kind, long intVal, string qualVal)
        {
            Kind = kind;
            IntegerValue = intVal;
            QualifierValue = qualVal;
        }

        public static Item Integer(long value) => new(Kind.Integer, value, "");

        public static Item Qualifier(string value) => new(Kind.Qualifier, 0, value);

        public static Item Zero(Kind kind) => kind == Kind.Integer ? Integer(0) : Qualifier("");

        // Known qualifier ranks. "" / "ga" / "final" / "release" all map to RELEASE rank.
        // Anything past RELEASE (i.e. unknown qualifier or "sp") sorts higher.
        private static int QualifierRank(string q) =>
            q switch
            {
                "snapshot" => 0,
                "alpha" or "a" => 1,
                "beta" or "b" => 2,
                "milestone" or "m" => 3,
                "rc" or "cr" => 4,
                "" or "ga" or "final" or "release" => 5,
                "sp" => 7,
                _ => 6,
            };

        public int CompareTo(Item other)
        {
            // Integers always compare numerically. A qualifier compared against an integer is
            // treated as less when the qualifier sorts below RELEASE, equal at RELEASE, greater
            // beyond.
            if (Kind == Kind.Integer && other.Kind == Kind.Integer)
                return IntegerValue.CompareTo(other.IntegerValue);

            if (Kind == Kind.Integer && other.Kind == Kind.Qualifier)
            {
                int otherRank = QualifierRank(other.QualifierValue);
                if (otherRank < 5)
                    return 1;
                if (otherRank > 5)
                    return -1;
                return IntegerValue == 0 ? 0 : 1;
            }
            if (Kind == Kind.Qualifier && other.Kind == Kind.Integer)
            {
                int ownRank = QualifierRank(QualifierValue);
                if (ownRank < 5)
                    return -1;
                if (ownRank > 5)
                    return 1;
                return other.IntegerValue == 0 ? 0 : -1;
            }

            // Both qualifiers — known rank wins, unknown falls back to string compare.
            int leftRank = QualifierRank(QualifierValue);
            int rightRank = QualifierRank(other.QualifierValue);
            if (leftRank != rightRank)
                return leftRank.CompareTo(rightRank);
            if (leftRank == 6)
                return string.CompareOrdinal(QualifierValue, other.QualifierValue);
            return 0;
        }
    }

    private static IReadOnlyList<Item> Tokenize(string raw)
    {
        // Maven token boundaries: explicit separators '.' and '-', plus implicit
        // boundaries at every letter↔digit transition. So "rc1" tokenises as
        // ["rc", 1] not ["rc1"], which is what lets 1.0-rc1 sort below 1.0.
        List<Item> items = [];
        int index = 0;
        while (index < raw.Length)
        {
            char c = raw[index];
            if (c == '.' || c == '-')
            {
                index++;
                continue;
            }
            if (char.IsDigit(c))
            {
                int start = index;
                while (index < raw.Length && char.IsDigit(raw[index]))
                    index++;
                if (long.TryParse(raw[start..index], out long value))
                    items.Add(Item.Integer(value));
                else
                    items.Add(Item.Qualifier(raw[start..index]));
            }
            else
            {
                int start = index;
                while (
                    index < raw.Length
                    && raw[index] != '.'
                    && raw[index] != '-'
                    && !char.IsDigit(raw[index])
                )
                    index++;
                items.Add(Item.Qualifier(raw[start..index]));
            }
        }

        // Trim trailing zero/empty items so 1.0 == 1.0.0 == 1.0.0.0.
        while (items.Count > 0)
        {
            Item last = items[^1];
            bool isZero =
                (last.Kind == Kind.Integer && last.IntegerValue == 0)
                || (
                    last.Kind == Kind.Qualifier
                    && (
                        last.QualifierValue == ""
                        || last.QualifierValue == "ga"
                        || last.QualifierValue == "final"
                        || last.QualifierValue == "release"
                    )
                );
            if (!isZero)
                break;
            items.RemoveAt(items.Count - 1);
        }

        return items;
    }
}
