using Shield.Core.Domain;

namespace Shield.Api.Services.Findings;

public sealed class TyposquatDetector : ITyposquatDetector
{
    // Cap input length for Levenshtein — anything longer than 64 chars is almost
    // certainly not a typosquat candidate (real squats target short, memorable
    // names like `lodash`, `react`, `axios`).
    private const int MaxNameLengthForLevenshtein = 64;

    private readonly IPopularPackageRegistry _popular;

    public TyposquatDetector(IPopularPackageRegistry popular)
    {
        _popular = popular;
    }

    public bool IsTyposquat(Ecosystem ecosystem, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLengthForLevenshtein)
            return false;

        IReadOnlySet<string> popular = _popular.For(ecosystem);
        if (popular.Count == 0)
            return false;

        // A package matching itself is not a typosquat.
        if (popular.Contains(name))
            return false;

        foreach (string candidate in popular)
        {
            if (candidate.Length > MaxNameLengthForLevenshtein)
                continue;

            // Skip pairs whose lengths differ by more than the distance threshold —
            // |len(a) - len(b)| is a lower bound for Levenshtein.
            int lengthDelta = Math.Abs(candidate.Length - name.Length);
            if (lengthDelta > 2)
                continue;

            int distance = Levenshtein(candidate, name, 2);
            if (distance is > 0 and <= 2)
                return true;
        }
        return false;
    }

    public bool IsScopeMismatch(string name)
    {
        // Match shapes like "@scope/inner". The classic confusion attack is
        // @lodash/lodash where the scope advertises lodash but the inner name is
        // also lodash — i.e. the package is pretending to be lodash inside a scope
        // the real lodash team doesn't own.
        if (!name.StartsWith('@'))
            return false;
        int slash = name.IndexOf('/');
        if (slash <= 1 || slash == name.Length - 1)
            return false;
        string scope = name.Substring(1, slash - 1);
        string inner = name[(slash + 1)..];
        if (string.Equals(scope, inner, StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlySet<string> popular = _popular.For(Ecosystem.Npm);
            return popular.Contains(inner);
        }
        return false;
    }

    // Iterative Levenshtein with an early-exit cap. Returns int.MaxValue if the
    // distance is known to exceed `cap` — caller treats that as "too far".
    private static int Levenshtein(string a, string b, int cap)
    {
        int lenA = a.Length;
        int lenB = b.Length;
        if (lenA == 0)
            return lenB;
        if (lenB == 0)
            return lenA;

        int[] prev = new int[lenB + 1];
        int[] curr = new int[lenB + 1];
        for (int column = 0; column <= lenB; column++)
            prev[column] = column;

        for (int row = 1; row <= lenA; row++)
        {
            curr[0] = row;
            int minInRow = curr[0];
            for (int column = 1; column <= lenB; column++)
            {
                int cost =
                    char.ToLowerInvariant(a[row - 1]) == char.ToLowerInvariant(b[column - 1])
                        ? 0
                        : 1;
                curr[column] = Math.Min(
                    Math.Min(curr[column - 1] + 1, prev[column] + 1),
                    prev[column - 1] + cost
                );
                if (curr[column] < minInRow)
                    minInRow = curr[column];
            }
            if (minInRow > cap)
                return int.MaxValue;

            (prev, curr) = (curr, prev);
        }
        return prev[lenB];
    }
}
