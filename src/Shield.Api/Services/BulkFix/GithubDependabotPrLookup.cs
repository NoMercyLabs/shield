using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shield.Api.Services.BulkFix;

// Asks GitHub's /search/issues endpoint for open PRs authored by app/dependabot. Cheaper than
// listing every PR in the repo and filtering client-side, and works equally well on private
// repos as long as the token can read them. Bearer is whatever OAuth identity Shield has —
// connect-flow first, then any signin row. Returns empty when no bearer is available so the
// orchestrator can decide whether to skip the gate or block.
public sealed class GithubDependabotPrLookup : IDependabotPrLookup
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IOAuthTokenStore _tokens;
    private readonly ILogger<GithubDependabotPrLookup> _logger;

    public GithubDependabotPrLookup(
        HttpClient http,
        IOAuthTokenStore tokens,
        ILogger<GithubDependabotPrLookup> logger
    )
    {
        _http = http;
        _tokens = tokens;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DependabotPrSummary>> ListOpenAsync(
        string repoFullName,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(repoFullName))
            return [];

        OAuthTokenSnapshot? token = await _tokens.GetAnyAsync(OAuthProvider.Github, ct);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            _logger.LogDebug("No GitHub OAuth token available; skipping Dependabot conflict check");
            return [];
        }

        string query = Uri.EscapeDataString(
            $"is:pr is:open author:app/dependabot repo:{repoFullName}"
        );
        HttpRequestMessage request = new(
            HttpMethod.Get,
            $"https://api.github.com/search/issues?q={query}&per_page=50"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("Shield-DependabotConflictCheck");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        try
        {
            HttpResponseMessage response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GitHub search returned {Status} for repo {Repo}; skipping Dependabot check",
                    (int)response.StatusCode,
                    repoFullName
                );
                return [];
            }
            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
            SearchResponse? payload = await JsonSerializer.DeserializeAsync<SearchResponse>(
                stream,
                JsonOpts,
                ct
            );
            if (payload?.Items is null)
                return [];

            return payload
                .Items.Select(item => new DependabotPrSummary(
                    item.Number,
                    item.Title ?? string.Empty,
                    item.HtmlUrl ?? string.Empty,
                    item.CreatedAt
                ))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Dependabot conflict check failed for repo {Repo}",
                repoFullName
            );
            return [];
        }
    }

    private sealed record SearchResponse(
        [property: JsonPropertyName("items")] List<SearchItem>? Items
    );

    private sealed record SearchItem(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("created_at")] DateTime CreatedAt
    );
}
