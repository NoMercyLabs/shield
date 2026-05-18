using FluentAssertions;
using Shield.Api.Services;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Api.Tests;

public sealed class FixSuggesterTests
{
    // ── SuggestForPackage — single advisory, existing behaviour ───────────────

    [Fact]
    public void SuggestForPackage_returns_null_when_no_fix_events_in_range()
    {
        FixSuggester suggester = new();
        Advisory advisory = MakeAdvisory("GHSA-aaa", "[{\"events\":[{\"introduced\":\"0\"}]}]");
        InventoryItem item = MakeItem("lodash", "4.17.20");

        FixSuggestion? result = suggester.SuggestForPackage(
            item.Ecosystem,
            item.Name,
            item.Version,
            [advisory]
        );

        result.Should().BeNull();
    }

    [Fact]
    public void SuggestForPackage_returns_null_when_fix_is_not_greater_than_current()
    {
        FixSuggester suggester = new();
        Advisory advisory = MakeAdvisory(
            "GHSA-aaa",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.20\"}]}]"
        );
        InventoryItem item = MakeItem("lodash", "4.17.21");

        FixSuggestion? result = suggester.SuggestForPackage(
            item.Ecosystem,
            item.Name,
            item.Version,
            [advisory]
        );

        result.Should().BeNull();
    }

    [Fact]
    public void SuggestForPackage_picks_highest_qualifying_fix_within_single_advisory()
    {
        FixSuggester suggester = new();
        Advisory advisory = MakeAdvisory(
            "GHSA-aaa",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.21\"}]},"
                + "{\"events\":[{\"introduced\":\"4.17.0\"},{\"fixed\":\"5.0.0\"}]}]"
        );
        InventoryItem item = MakeItem("lodash", "4.17.20");

        FixSuggestion? result = suggester.SuggestForPackage(
            item.Ecosystem,
            item.Name,
            item.Version,
            [advisory]
        );

        result.Should().NotBeNull();
        result!.SuggestedVersion.Should().Be("5.0.0");
        result.PackageName.Should().Be("lodash");
        result.CurrentVersion.Should().Be("4.17.20");
    }

    [Fact]
    public void SuggestForPackage_skips_fix_events_below_current_version()
    {
        FixSuggester suggester = new();
        Advisory advisory = MakeAdvisory(
            "GHSA-aaa",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.10\"}]},"
                + "{\"events\":[{\"introduced\":\"4.17.0\"},{\"fixed\":\"4.17.21\"}]}]"
        );
        InventoryItem item = MakeItem("lodash", "4.17.20");

        FixSuggestion? result = suggester.SuggestForPackage(
            item.Ecosystem,
            item.Name,
            item.Version,
            [advisory]
        );

        result.Should().NotBeNull();
        result!.SuggestedVersion.Should().Be("4.17.21");
    }

    [Fact]
    public void SuggestForPackage_returns_null_when_advisory_has_no_fix_events()
    {
        FixSuggester suggester = new();
        Advisory advisory = MakeAdvisory("GHSA-aaa", "[]");
        InventoryItem item = MakeItem("lodash", "4.17.20");

        FixSuggestion? result = suggester.SuggestForPackage(
            item.Ecosystem,
            item.Name,
            item.Version,
            [advisory]
        );

        result.Should().BeNull();
    }

    [Fact]
    public void SuggestForPackage_handles_nuget_versions_via_fallback_comparer()
    {
        FixSuggester suggester = new();
        Advisory advisory = MakeAdvisory(
            "GHSA-aaa",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"13.0.3\"}]}]",
            Ecosystem.Nuget
        );
        InventoryItem item = MakeItem("Newtonsoft.Json", "13.0.1", Ecosystem.Nuget);

        FixSuggestion? result = suggester.SuggestForPackage(
            item.Ecosystem,
            item.Name,
            item.Version,
            [advisory]
        );

        result.Should().NotBeNull();
        result!.SuggestedVersion.Should().Be("13.0.3");
    }

    // ── SuggestForPackage — aggregate across multiple advisories ──────────────

    [Fact]
    public void SuggestForPackage_returns_null_when_advisory_list_is_empty()
    {
        FixSuggester suggester = new();

        FixSuggestion? result = suggester.SuggestForPackage(Ecosystem.Npm, "lodash", "4.17.20", []);

        result.Should().BeNull();
    }

    [Fact]
    public void SuggestForPackage_returns_null_when_no_advisory_qualifies()
    {
        FixSuggester suggester = new();
        Advisory advisory = MakeAdvisory(
            "GHSA-aaa",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.10\"}]}]"
        );

        FixSuggestion? result = suggester.SuggestForPackage(
            Ecosystem.Npm,
            "lodash",
            "4.17.20",
            [advisory]
        );

        result.Should().BeNull();
    }

    [Fact]
    public void SuggestForPackage_picks_highest_version_across_three_advisories()
    {
        FixSuggester suggester = new();

        Advisory advisoryA = MakeAdvisory(
            "GHSA-aaa",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.21\"}]}]"
        );
        Advisory advisoryB = MakeAdvisory(
            "GHSA-bbb",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.18\"}]}]"
        );
        Advisory advisoryC = MakeAdvisory(
            "GHSA-ccc",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.22\"}]}]"
        );
        InventoryItem item = MakeItem("lodash", "4.17.17");

        FixSuggestion? result = suggester.SuggestForPackage(
            item.Ecosystem,
            item.Name,
            item.Version,
            [advisoryA, advisoryB, advisoryC]
        );

        result.Should().NotBeNull();
        result!.SuggestedVersion.Should().Be("4.17.22");
        result.Notes.Should().Contain("covers 3 advisories");
        result.Notes.Should().Contain("GHSA-aaa");
        result.Notes.Should().Contain("GHSA-bbb");
        result.Notes.Should().Contain("GHSA-ccc");
    }

    [Fact]
    public void SuggestForPackage_skips_advisory_whose_fix_is_below_current_version()
    {
        FixSuggester suggester = new();

        Advisory advisoryBelowCurrent = MakeAdvisory(
            "GHSA-old",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.10\"}]}]"
        );
        Advisory advisoryAboveCurrent = MakeAdvisory(
            "GHSA-new",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.22\"}]}]"
        );
        InventoryItem item = MakeItem("lodash", "4.17.20");

        FixSuggestion? result = suggester.SuggestForPackage(
            item.Ecosystem,
            item.Name,
            item.Version,
            [advisoryBelowCurrent, advisoryAboveCurrent]
        );

        result.Should().NotBeNull();
        result!.SuggestedVersion.Should().Be("4.17.22");
        result.Notes.Should().Contain("GHSA-new");
        result.Notes.Should().NotContain("GHSA-old");
    }

    [Fact]
    public void SuggestForPackage_notes_says_singular_advisory_when_only_one_qualifies()
    {
        FixSuggester suggester = new();

        Advisory advisory = MakeAdvisory(
            "GHSA-solo",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.21\"}]}]"
        );
        InventoryItem item = MakeItem("lodash", "4.17.20");

        FixSuggestion? result = suggester.SuggestForPackage(
            item.Ecosystem,
            item.Name,
            item.Version,
            [advisory]
        );

        result.Should().NotBeNull();
        result!.Notes.Should().Contain("covers 1 advisory:");
        result.Notes.Should().Contain("GHSA-solo");
    }

    [Fact]
    public void SuggestForPackage_picks_higher_version_than_old_single_advisory_logic_would_have()
    {
        // Old Suggest(advisory, item) picked the LOWEST qualifying fix from one advisory.
        // SuggestForPackage picks the HIGHEST across all advisories.
        // Scenario: advisory A fixes at 4.17.21, advisory B fixes at 4.18.0.
        // Old: would return 4.17.21 (only sees advisory A).
        // New: returns 4.18.0 (highest across A+B).
        FixSuggester suggester = new();

        Advisory advisoryA = MakeAdvisory(
            "GHSA-aaa",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.21\"}]}]"
        );
        Advisory advisoryB = MakeAdvisory(
            "GHSA-bbb",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.18.0\"}]}]"
        );
        InventoryItem item = MakeItem("lodash", "4.17.20");

        FixSuggestion? result = suggester.SuggestForPackage(
            item.Ecosystem,
            item.Name,
            item.Version,
            [advisoryA, advisoryB]
        );

        result.Should().NotBeNull();
        result!.SuggestedVersion.Should().Be("4.18.0");
        result.Notes.Should().Contain("covers 2 advisories");
    }

    // ── Suggested-version-itself-vulnerable check (Gap 7) ─────────────────────

    [Fact]
    public void SuggestForPackage_skips_suggested_version_that_is_itself_vulnerable()
    {
        // Advisory A: fix at 1.2.0
        // Advisory B: 1.2.0 is still vulnerable (its range includes 1.2.0), fix at 1.3.0
        // Advisory C: fix at 1.3.0
        // Expected: suggester returns 1.3.0, not 1.2.0
        FixSuggester suggester = new();

        Advisory advisoryA = MakeAdvisory(
            "GHSA-aaa",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"1.2.0\"}]}]"
        );
        // Advisory B's range: introduced 0, NOT fixed — so 1.2.0 is in this range.
        // We model "1.2.0 is vulnerable" by saying the range covers up to 1.3.0.
        Advisory advisoryB = MakeAdvisory(
            "GHSA-bbb",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"1.3.0\"}]}]"
        );
        Advisory advisoryC = MakeAdvisory(
            "GHSA-ccc",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"1.3.0\"}]}]"
        );
        InventoryItem item = MakeItem("lodash", "1.1.0");

        FixSuggestion? result = suggester.SuggestForPackage(
            item.Ecosystem,
            item.Name,
            item.Version,
            [advisoryA, advisoryB, advisoryC]
        );

        result.Should().NotBeNull();
        result!.SuggestedVersion.Should().Be("1.3.0", "1.2.0 is still vulnerable per advisory B");
    }

    [Fact]
    public void SuggestForPackage_returns_null_when_all_fix_candidates_are_themselves_vulnerable()
    {
        FixSuggester suggester = new();

        // Only candidate is 1.2.0, but advisory B says 1.2.0 is also vulnerable and there's
        // no fix above it.
        Advisory advisoryA = MakeAdvisory(
            "GHSA-aaa",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"1.2.0\"}]}]"
        );
        Advisory advisoryB = MakeAdvisory(
            "GHSA-bbb",
            "[{\"events\":[{\"introduced\":\"1.2.0\"}]}]"
        );
        InventoryItem item = MakeItem("lodash", "1.1.0");

        FixSuggestion? result = suggester.SuggestForPackage(
            item.Ecosystem,
            item.Name,
            item.Version,
            [advisoryA, advisoryB]
        );

        result.Should().BeNull("no candidate is free of all advisories");
    }

    [Fact]
    public void IsVersionVulnerable_returns_true_when_version_falls_in_range()
    {
        string rangesJson = "[{\"events\":[{\"introduced\":\"1.0.0\"},{\"fixed\":\"1.3.0\"}]}]";

        FixSuggester.IsVersionVulnerable(Ecosystem.Npm, "1.2.0", rangesJson).Should().BeTrue();
        FixSuggester.IsVersionVulnerable(Ecosystem.Npm, "1.3.0", rangesJson).Should().BeFalse();
        FixSuggester.IsVersionVulnerable(Ecosystem.Npm, "0.9.0", rangesJson).Should().BeFalse();
    }

    private static Advisory MakeAdvisory(
        string externalId,
        string rangesJson,
        Ecosystem ecosystem = Ecosystem.Npm
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            Ecosystem = ecosystem,
            PackageName = "lodash",
            AffectedRangesJson = rangesJson,
            Severity = Severity.High,
            Summary = "synthetic",
            ReferencesJson = "[]",
        };

    private static InventoryItem MakeItem(
        string name,
        string version,
        Ecosystem ecosystem = Ecosystem.Npm
    ) =>
        new()
        {
            Id = 1,
            Ecosystem = ecosystem,
            Name = name,
            Version = version,
        };
}
