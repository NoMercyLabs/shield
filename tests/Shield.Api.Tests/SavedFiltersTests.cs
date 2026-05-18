using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Shield.Api.Contracts;
using Xunit;

namespace Shield.Api.Tests;

public sealed class SavedFiltersTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public SavedFiltersTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateThenListRoundtripsTheFilter()
    {
        HttpClient client = _factory.CreateClient();
        string name = "critical-lodash-" + Guid.NewGuid().ToString("n");
        const string queryJson = "{\"severity\":[3],\"packageName\":[\"lodash\"]}";

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/saved-filters",
            new CreateSavedFilterRequest(name, "findings", queryJson)
        );
        created.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage list = await client.GetAsync("/api/saved-filters?kind=findings");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        List<SavedFilterResponse>? rows = await list.Content.ReadFromJsonAsync<
            List<SavedFilterResponse>
        >();
        SavedFilterResponse? match = rows!.FirstOrDefault(row => row.Name == name);
        match.Should().NotBeNull();
        match!.QueryJson.Should().Be(queryJson);
        match.Kind.Should().Be("findings");
    }

    [Fact]
    public async Task SavingSameNameOverwritesExistingQuery()
    {
        HttpClient client = _factory.CreateClient();
        string name = "overwrite-" + Guid.NewGuid().ToString("n");
        const string firstJson = "{\"severity\":[1]}";
        const string secondJson = "{\"severity\":[2,3]}";

        await client.PostAsJsonAsync(
            "/api/saved-filters",
            new CreateSavedFilterRequest(name, "findings", firstJson)
        );
        await client.PostAsJsonAsync(
            "/api/saved-filters",
            new CreateSavedFilterRequest(name, "findings", secondJson)
        );

        HttpResponseMessage list = await client.GetAsync("/api/saved-filters?kind=findings");
        List<SavedFilterResponse>? rows = await list.Content.ReadFromJsonAsync<
            List<SavedFilterResponse>
        >();
        IEnumerable<SavedFilterResponse> matches = rows!.Where(row => row.Name == name);
        matches.Should().ContainSingle();
        matches.Single().QueryJson.Should().Be(secondJson);
    }

    [Fact]
    public async Task DeleteRemovesTheFilter()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/saved-filters",
            new CreateSavedFilterRequest(
                "to-delete-" + Guid.NewGuid().ToString("n"),
                "findings",
                "{}"
            )
        );
        SavedFilterResponse? row = await created.Content.ReadFromJsonAsync<SavedFilterResponse>();

        HttpResponseMessage delete = await client.DeleteAsync($"/api/saved-filters/{row!.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage list = await client.GetAsync("/api/saved-filters?kind=findings");
        List<SavedFilterResponse>? rows = await list.Content.ReadFromJsonAsync<
            List<SavedFilterResponse>
        >();
        rows.Should().NotContain(item => item.Id == row.Id);
    }

    [Fact]
    public async Task CreateWithEmptyNameReturns400()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/saved-filters",
            new CreateSavedFilterRequest("   ", "findings", "{}")
        );
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListByKindFiltersToThatKindOnly()
    {
        HttpClient client = _factory.CreateClient();
        string unique = Guid.NewGuid().ToString("n");
        await client.PostAsJsonAsync(
            "/api/saved-filters",
            new CreateSavedFilterRequest("findings-only-" + unique, "findings", "{}")
        );
        await client.PostAsJsonAsync(
            "/api/saved-filters",
            new CreateSavedFilterRequest("sources-only-" + unique, "sources", "{}")
        );

        HttpResponseMessage findingsList = await client.GetAsync(
            "/api/saved-filters?kind=findings"
        );
        List<SavedFilterResponse>? findingsRows = await findingsList.Content.ReadFromJsonAsync<
            List<SavedFilterResponse>
        >();
        findingsRows.Should().Contain(row => row.Name == "findings-only-" + unique);
        findingsRows.Should().NotContain(row => row.Name == "sources-only-" + unique);

        HttpResponseMessage sourcesList = await client.GetAsync("/api/saved-filters?kind=sources");
        List<SavedFilterResponse>? sourcesRows = await sourcesList.Content.ReadFromJsonAsync<
            List<SavedFilterResponse>
        >();
        sourcesRows.Should().Contain(row => row.Name == "sources-only-" + unique);
        sourcesRows.Should().NotContain(row => row.Name == "findings-only-" + unique);
    }
}
