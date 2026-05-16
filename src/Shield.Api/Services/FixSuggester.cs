using System.Text.Json;
using NuGet.Versioning;
using Semver;
using Shield.Core.Domain;
using Shield.Matcher.Versioning;

namespace Shield.Api.Services;

// Resolves the lowest known fix version from an advisory's OSV ranges that is strictly
// greater than the installed version. Falls back to a permissive semver/NuGet parse so
// ecosystems without a dedicated matcher comparer (Python/Go/Rust) still yield suggestions.
public sealed record FixSuggestion(
    string PackageName,
    string CurrentVersion,
    string SuggestedVersion,
    string? Notes
);

public interface IFixSuggester
{
    FixSuggestion? Suggest(Advisory advisory, InventoryItem currentItem);
}

public sealed class FixSuggester : IFixSuggester
{
    public FixSuggestion? Suggest(Advisory advisory, InventoryItem currentItem)
    {
        ArgumentNullException.ThrowIfNull(advisory);
        ArgumentNullException.ThrowIfNull(currentItem);

        IReadOnlyList<FixEvent> fixes = ExtractFixEvents(advisory.AffectedRangesJson);
        if (fixes.Count == 0)
            return null;

        Ecosystem ecosystem = currentItem.Ecosystem;
        List<(string Version, FixEvent Event)> qualifying = new();
        foreach (FixEvent fix in fixes)
        {
            if (string.IsNullOrWhiteSpace(fix.Fixed))
                continue;
            if (Compare(ecosystem, fix.Fixed, currentItem.Version) > 0)
                qualifying.Add((fix.Fixed, fix));
        }

        if (qualifying.Count == 0)
            return null;

        (string Version, FixEvent Event) chosen = qualifying
            .OrderBy(entry => entry.Version, new EcosystemVersionComparer(ecosystem))
            .First();

        string? notes = BuildNotes(chosen.Event, qualifying.Count);
        return new FixSuggestion(
            PackageName: currentItem.Name,
            CurrentVersion: currentItem.Version,
            SuggestedVersion: chosen.Version,
            Notes: notes
        );
    }

    private static string? BuildNotes(FixEvent fix, int candidateCount)
    {
        List<string> parts = new();
        if (!string.IsNullOrWhiteSpace(fix.Introduced))
            parts.Add($"introduced in {fix.Introduced}");
        if (!string.IsNullOrWhiteSpace(fix.Limit))
            parts.Add($"does not apply >= {fix.Limit}");
        if (candidateCount > 1)
            parts.Add($"{candidateCount} fix events considered");
        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }

    // Returns negative when `left` < `right`, 0 equal, positive when `left` > `right`.
    // Falls back to ordinal string compare if neither comparer can parse — keeps the
    // suggester useful for exotic version strings without throwing.
    private static int Compare(Ecosystem ecosystem, string left, string right)
    {
        if (TryParseSemver(left, out SemVersion? leftSem) && TryParseSemver(right, out SemVersion? rightSem))
            return leftSem!.ComparePrecedenceTo(rightSem);

        if (NuGetVersion.TryParse(left, out NuGetVersion? leftNuget)
            && NuGetVersion.TryParse(right, out NuGetVersion? rightNuget))
            return leftNuget.CompareTo(rightNuget);

        return string.CompareOrdinal(left, right);
    }

    private static bool TryParseSemver(string raw, out SemVersion parsed)
    {
        string trimmed = raw.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
            trimmed = trimmed[1..];
        return SemVersion.TryParse(trimmed, SemVersionStyles.Any, out parsed!);
    }

    private static IReadOnlyList<FixEvent> ExtractFixEvents(string affectedRangesJson)
    {
        if (string.IsNullOrWhiteSpace(affectedRangesJson))
            return Array.Empty<FixEvent>();

        try
        {
            using JsonDocument document = JsonDocument.Parse(affectedRangesJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                return Array.Empty<FixEvent>();

            List<FixEvent> result = new();
            foreach (JsonElement range in root.EnumerateArray())
            {
                if (range.ValueKind != JsonValueKind.Object)
                    continue;
                if (!range.TryGetProperty("events", out JsonElement events) || events.ValueKind != JsonValueKind.Array)
                    continue;

                string? introduced = null;
                foreach (JsonElement evt in events.EnumerateArray())
                {
                    if (evt.ValueKind != JsonValueKind.Object)
                        continue;

                    if (evt.TryGetProperty("introduced", out JsonElement introducedEl))
                    {
                        string? value = introducedEl.GetString();
                        introduced = value == "0" ? null : value;
                    }
                    else if (evt.TryGetProperty("fixed", out JsonElement fixedEl))
                    {
                        string? fixedVersion = fixedEl.GetString();
                        if (!string.IsNullOrWhiteSpace(fixedVersion))
                        {
                            string? limit = TryReadLaterLimit(events);
                            result.Add(new FixEvent(introduced, fixedVersion, limit));
                        }
                        introduced = null;
                    }
                }
            }
            return result;
        }
        catch (JsonException)
        {
            return Array.Empty<FixEvent>();
        }
    }

    private static string? TryReadLaterLimit(JsonElement events)
    {
        foreach (JsonElement evt in events.EnumerateArray())
        {
            if (evt.ValueKind != JsonValueKind.Object)
                continue;
            if (evt.TryGetProperty("limit", out JsonElement limitEl))
                return limitEl.GetString();
        }
        return null;
    }

    private readonly record struct FixEvent(string? Introduced, string Fixed, string? Limit);

    private sealed class EcosystemVersionComparer : IComparer<string>
    {
        private readonly Ecosystem _ecosystem;
        public EcosystemVersionComparer(Ecosystem ecosystem) => _ecosystem = ecosystem;
        public int Compare(string? left, string? right)
        {
            if (left is null && right is null) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            return FixSuggester.Compare(_ecosystem, left, right);
        }
    }
}
