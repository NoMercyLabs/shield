using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shield.Feeds.Ghsa;

public sealed class GhsaGraphQLClient
{
    private readonly HttpClient _http;

    public GhsaGraphQLClient(HttpClient http)
    {
        _http = http;
    }

    public async ValueTask<GhsaAdvisoryPage> QueryAdvisoriesAsync(
        DateTime publishedSinceUtc,
        int pageSize,
        string? afterCursor,
        CancellationToken ct
    )
    {
        const string query = """
            query($first: Int!, $publishedSince: DateTime!, $after: String) {
              securityAdvisories(
                first: $first,
                orderBy: { field: PUBLISHED_AT, direction: ASC },
                publishedSince: $publishedSince,
                after: $after
              ) {
                pageInfo { hasNextPage endCursor }
                nodes {
                  ghsaId
                  summary
                  severity
                  publishedAt
                  updatedAt
                  references { url }
                  cvss { score }
                  vulnerabilities(first: 50) {
                    nodes {
                      package { ecosystem name }
                      vulnerableVersionRange
                      firstPatchedVersion { identifier }
                    }
                  }
                }
              }
            }
            """;

        GraphQLRequest payload = new()
        {
            Query = query,
            Variables = new Dictionary<string, object?>
            {
                ["first"] = pageSize,
                ["publishedSince"] = publishedSinceUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["after"] = afterCursor,
            },
        };

        HttpResponseMessage response = await _http.PostAsJsonAsync((Uri?)null, payload, ct);
        response.EnsureSuccessStatusCode();

        GraphQLResponse<SecurityAdvisoriesData>? body = await response.Content.ReadFromJsonAsync<
            GraphQLResponse<SecurityAdvisoriesData>
        >(cancellationToken: ct);

        if (body is null)
        {
            throw new InvalidOperationException("GHSA returned empty body.");
        }

        if (body.Errors is { Length: > 0 })
        {
            string message = string.Join(
                "; ",
                body.Errors.Select(graphError => graphError.Message)
            );
            throw new InvalidOperationException($"GHSA GraphQL errors: {message}");
        }

        SecurityAdvisoriesConnection? connection = body.Data?.SecurityAdvisories;
        if (connection is null)
        {
            return new GhsaAdvisoryPage([], false, null);
        }

        return new GhsaAdvisoryPage(
            connection.Nodes ?? [],
            connection.PageInfo?.HasNextPage ?? false,
            connection.PageInfo?.EndCursor
        );
    }

    private sealed class GraphQLRequest
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;

        [JsonPropertyName("variables")]
        public Dictionary<string, object?> Variables { get; set; } = new();
    }

    private sealed class GraphQLResponse<TData>
    {
        [JsonPropertyName("data")]
        public TData? Data { get; set; }

        [JsonPropertyName("errors")]
        public GraphQLError[]? Errors { get; set; }
    }

    private sealed class GraphQLError
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class SecurityAdvisoriesData
    {
        [JsonPropertyName("securityAdvisories")]
        public SecurityAdvisoriesConnection? SecurityAdvisories { get; set; }
    }

    private sealed class SecurityAdvisoriesConnection
    {
        [JsonPropertyName("pageInfo")]
        public GhsaPageInfo? PageInfo { get; set; }

        [JsonPropertyName("nodes")]
        public GhsaAdvisoryNode[]? Nodes { get; set; }
    }
}

public sealed record GhsaAdvisoryPage(
    IReadOnlyList<GhsaAdvisoryNode> Nodes,
    bool HasNextPage,
    string? EndCursor
);

public sealed class GhsaPageInfo
{
    [JsonPropertyName("hasNextPage")]
    public bool HasNextPage { get; set; }

    [JsonPropertyName("endCursor")]
    public string? EndCursor { get; set; }
}

public sealed class GhsaAdvisoryNode
{
    [JsonPropertyName("ghsaId")]
    public string GhsaId { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "LOW";

    [JsonPropertyName("publishedAt")]
    public DateTime PublishedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("references")]
    public GhsaReference[]? References { get; set; }

    [JsonPropertyName("cvss")]
    public GhsaCvss? Cvss { get; set; }

    [JsonPropertyName("vulnerabilities")]
    public GhsaVulnerabilitiesConnection? Vulnerabilities { get; set; }
}

public sealed class GhsaReference
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

public sealed class GhsaCvss
{
    [JsonPropertyName("score")]
    public double? Score { get; set; }
}

public sealed class GhsaVulnerabilitiesConnection
{
    [JsonPropertyName("nodes")]
    public GhsaVulnerabilityNode[]? Nodes { get; set; }
}

public sealed class GhsaVulnerabilityNode
{
    [JsonPropertyName("package")]
    public GhsaPackage? Package { get; set; }

    [JsonPropertyName("vulnerableVersionRange")]
    public string VulnerableVersionRange { get; set; } = string.Empty;

    [JsonPropertyName("firstPatchedVersion")]
    public GhsaPatchedVersion? FirstPatchedVersion { get; set; }
}

public sealed class GhsaPackage
{
    [JsonPropertyName("ecosystem")]
    public string Ecosystem { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class GhsaPatchedVersion
{
    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = string.Empty;
}
