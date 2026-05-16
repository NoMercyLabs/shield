using FluentAssertions;
using Shield.Api.Services;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Api.Tests;

public sealed class FixSuggesterTests
{
    [Fact]
    public void Suggest_returns_null_when_no_fix_events_in_range()
    {
        FixSuggester suggester = new();
        Advisory advisory = MakeAdvisory("[{\"events\":[{\"introduced\":\"0\"}]}]");
        InventoryItem item = MakeItem("lodash", "4.17.20");

        FixSuggestion? result = suggester.Suggest(advisory, item);

        result.Should().BeNull();
    }

    [Fact]
    public void Suggest_returns_null_when_fix_is_not_greater_than_current()
    {
        FixSuggester suggester = new();
        Advisory advisory = MakeAdvisory(
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.20\"}]}]"
        );
        InventoryItem item = MakeItem("lodash", "4.17.21");

        FixSuggestion? result = suggester.Suggest(advisory, item);

        result.Should().BeNull();
    }

    [Fact]
    public void Suggest_picks_lowest_qualifying_fix_when_multiple_ranges_apply()
    {
        FixSuggester suggester = new();
        Advisory advisory = MakeAdvisory(
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.21\"}]},"
            + "{\"events\":[{\"introduced\":\"4.17.0\"},{\"fixed\":\"5.0.0\"}]}]"
        );
        InventoryItem item = MakeItem("lodash", "4.17.20");

        FixSuggestion? result = suggester.Suggest(advisory, item);

        result.Should().NotBeNull();
        result!.SuggestedVersion.Should().Be("4.17.21");
        result.PackageName.Should().Be("lodash");
        result.CurrentVersion.Should().Be("4.17.20");
    }

    [Fact]
    public void Suggest_skips_fix_events_below_current_version()
    {
        FixSuggester suggester = new();
        Advisory advisory = MakeAdvisory(
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.10\"}]},"
            + "{\"events\":[{\"introduced\":\"4.17.0\"},{\"fixed\":\"4.17.21\"}]}]"
        );
        InventoryItem item = MakeItem("lodash", "4.17.20");

        FixSuggestion? result = suggester.Suggest(advisory, item);

        result.Should().NotBeNull();
        result!.SuggestedVersion.Should().Be("4.17.21");
    }

    [Fact]
    public void Suggest_returns_null_when_advisory_has_no_fix_events()
    {
        FixSuggester suggester = new();
        Advisory advisory = MakeAdvisory("[]");
        InventoryItem item = MakeItem("lodash", "4.17.20");

        FixSuggestion? result = suggester.Suggest(advisory, item);

        result.Should().BeNull();
    }

    [Fact]
    public void Suggest_handles_nuget_versions_via_fallback_comparer()
    {
        FixSuggester suggester = new();
        Advisory advisory = MakeAdvisory(
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"13.0.3\"}]}]",
            Ecosystem.Nuget
        );
        InventoryItem item = MakeItem("Newtonsoft.Json", "13.0.1", Ecosystem.Nuget);

        FixSuggestion? result = suggester.Suggest(advisory, item);

        result.Should().NotBeNull();
        result!.SuggestedVersion.Should().Be("13.0.3");
    }

    [Fact]
    public void Suggest_notes_includes_introduced_when_present()
    {
        FixSuggester suggester = new();
        Advisory advisory = MakeAdvisory(
            "[{\"events\":[{\"introduced\":\"4.0.0\"},{\"fixed\":\"4.17.21\"}]}]"
        );
        InventoryItem item = MakeItem("lodash", "4.17.20");

        FixSuggestion? result = suggester.Suggest(advisory, item);

        result.Should().NotBeNull();
        result!.Notes.Should().Contain("introduced in 4.0.0");
    }

    private static Advisory MakeAdvisory(string rangesJson, Ecosystem ecosystem = Ecosystem.Npm) =>
        new()
        {
            Id = Guid.NewGuid(),
            ExternalId = "TEST-FIX",
            Ecosystem = ecosystem,
            PackageName = "lodash",
            AffectedRangesJson = rangesJson,
            Severity = Severity.High,
            Summary = "synthetic",
            ReferencesJson = "[]",
        };

    private static InventoryItem MakeItem(string name, string version, Ecosystem ecosystem = Ecosystem.Npm) =>
        new()
        {
            Id = 1,
            Ecosystem = ecosystem,
            Name = name,
            Version = version,
        };
}
