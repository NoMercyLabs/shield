using System.Text.Json;
using NuGet.Versioning;
using Semver;
using Shield.Core.Domain;
using Shield.Matcher.Versioning;

namespace Shield.Api.Services;

// Resolves the highest known fix version across every advisory that matches a given package,
// so one version bump kills every known vulnerability in a single PR / manifest edit.
public sealed record FixSuggestion(
    string PackageName,
    string CurrentVersion,
    string SuggestedVersion,
    string? Notes
);

public interface IFixSuggester
{
    FixSuggestion? SuggestForPackage(
        Ecosystem ecosystem,
        string packageName,
        string currentVersion,
        IReadOnlyList<Advisory> matchingAdvisories
    );
}

public sealed class FixSuggester : IFixSuggester
{
    public FixSuggestion? SuggestForPackage(
        Ecosystem ecosystem,
        string packageName,
        string currentVersion,
        IReadOnlyList<Advisory> matchingAdvisories
    )
    {
        ArgumentNullException.ThrowIfNull(matchingAdvisories);

        if (matchingAdvisories.Count == 0)
            return null;

        // Collect (advisoryExternalId, fixedVersion) pairs from every advisory.
        // Only keep fix events strictly greater than the currently-installed version.
        List<(string AdvisoryId, string Version)> qualifying = [];
        foreach (Advisory advisory in matchingAdvisories)
        {
            IReadOnlyList<FixEvent> fixes = ExtractFixEvents(advisory.AffectedRangesJson);
            foreach (FixEvent fix in fixes)
            {
                if (string.IsNullOrWhiteSpace(fix.Fixed))
                    continue;
                if (Compare(ecosystem, fix.Fixed, currentVersion) > 0)
                    qualifying.Add((advisory.ExternalId, fix.Fixed));
            }
        }

        if (qualifying.Count == 0)
            return null;

        // Filter out any candidate version that is itself within a vulnerable range declared
        // by ANY of the matching advisories. Walk candidates from highest to lowest and pick
        // the first one that is safe across every advisory.
        IReadOnlyList<(string AdvisoryId, string Version)> candidatesByDescending = qualifying
            .OrderByDescending(entry => entry.Version, new EcosystemVersionComparer(ecosystem))
            .ToList();

        (string AdvisoryId, string Version)? chosenNullable = null;
        foreach ((string advisoryId, string candidateVersion) in candidatesByDescending)
        {
            bool selfVulnerable = false;
            foreach (Advisory advisory in matchingAdvisories)
            {
                if (IsVersionVulnerable(ecosystem, candidateVersion, advisory.AffectedRangesJson))
                {
                    selfVulnerable = true;
                    break;
                }
            }
            if (!selfVulnerable)
            {
                chosenNullable = (advisoryId, candidateVersion);
                break;
            }
        }

        if (chosenNullable is null)
            return null;

        (string AdvisoryId, string Version) chosen = chosenNullable.Value;

        // Collect the distinct advisory IDs covered by bumping to the chosen version.
        HashSet<string> coveredIds = qualifying
            .Where(entry =>
                Compare(ecosystem, chosen.Version, entry.Version) >= 0
                || string.Equals(entry.Version, chosen.Version, StringComparison.Ordinal)
            )
            .Select(entry => entry.AdvisoryId)
            .ToHashSet(StringComparer.Ordinal);

        string notes = BuildAggregateNotes(coveredIds);
        return new(
            PackageName: packageName,
            CurrentVersion: currentVersion,
            SuggestedVersion: chosen.Version,
            Notes: notes
        );
    }

    // Returns true when `version` falls within any vulnerable range described by `affectedRangesJson`.
    // Parses ranges directly rather than reusing ExtractFixEvents so we can handle open-ended
    // ranges (introduced with no fixed) which ExtractFixEvents intentionally omits.
    internal static bool IsVersionVulnerable(
        Ecosystem ecosystem,
        string version,
        string affectedRangesJson
    )
    {
        if (string.IsNullOrWhiteSpace(affectedRangesJson))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(affectedRangesJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                return false;

            foreach (JsonElement range in root.EnumerateArray())
            {
                if (range.ValueKind != JsonValueKind.Object)
                    continue;
                if (
                    !range.TryGetProperty("events", out JsonElement events)
                    || events.ValueKind != JsonValueKind.Array
                )
                    continue;

                string? introduced = null;
                string? fixed_ = null;

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
                        fixed_ = fixedEl.GetString();
                    }
                }

                bool afterIntroduced =
                    introduced is null || Compare(ecosystem, version, introduced) >= 0;
                bool beforeFixed =
                    string.IsNullOrWhiteSpace(fixed_) || Compare(ecosystem, version, fixed_) < 0;

                if (afterIntroduced && beforeFixed)
                    return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildAggregateNotes(IReadOnlyCollection<string> advisoryIds)
    {
        string idList = string.Join(", ", advisoryIds.OrderBy(id => id, StringComparer.Ordinal));
        return $"covers {advisoryIds.Count} {(advisoryIds.Count == 1 ? "advisory" : "advisories")}: {idList}";
    }

    // Returns negative when `left` < `right`, 0 equal, positive when `left` > `right`.
    // Falls back to ordinal string compare if neither comparer can parse — keeps the
    // suggester useful for exotic version strings without throwing.
    private static int Compare(Ecosystem ecosystem, string left, string right)
    {
        if (
            SemVerHelper.TryParse(left, out SemVersion? leftSem)
            && SemVerHelper.TryParse(right, out SemVersion? rightSem)
        )
            return leftSem!.ComparePrecedenceTo(rightSem);

        if (
            NuGetVersion.TryParse(left, out NuGetVersion? leftNuget)
            && NuGetVersion.TryParse(right, out NuGetVersion? rightNuget)
        )
            return leftNuget.CompareTo(rightNuget);

        return string.CompareOrdinal(left, right);
    }

    internal static IReadOnlyList<FixEvent> ExtractFixEvents(string affectedRangesJson)
    {
        if (string.IsNullOrWhiteSpace(affectedRangesJson))
            return [];

        try
        {
            using JsonDocument document = JsonDocument.Parse(affectedRangesJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                return [];

            List<FixEvent> result = [];
            foreach (JsonElement range in root.EnumerateArray())
            {
                if (range.ValueKind != JsonValueKind.Object)
                    continue;
                if (
                    !range.TryGetProperty("events", out JsonElement events)
                    || events.ValueKind != JsonValueKind.Array
                )
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
                            result.Add(new(introduced, fixedVersion, limit));
                        }
                        introduced = null;
                    }
                }
            }
            return result;
        }
        catch (JsonException)
        {
            return [];
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

    internal readonly record struct FixEvent(string? Introduced, string Fixed, string? Limit);

    private sealed class EcosystemVersionComparer : IComparer<string>
    {
        private readonly Ecosystem _ecosystem;

        public EcosystemVersionComparer(Ecosystem ecosystem) => _ecosystem = ecosystem;

        public int Compare(string? left, string? right)
        {
            if (left is null && right is null)
                return 0;
            if (left is null)
                return -1;
            if (right is null)
                return 1;
            return FixSuggester.Compare(_ecosystem, left, right);
        }
    }
}
