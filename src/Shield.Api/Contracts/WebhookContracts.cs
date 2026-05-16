using System.Text.Json.Serialization;

namespace Shield.Api.Contracts;

// Minimal subset of GitHub's pull_request webhook payload. We only read the fields we
// need; everything else is preserved in the raw JsonElement for diagnostics if needed.
public sealed record GitHubPullRequestEvent(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("pull_request")] GitHubPullRequest PullRequest,
    [property: JsonPropertyName("repository")] GitHubRepository Repository
);

public sealed record GitHubPullRequest(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("head")] GitHubPullRequestRef Head,
    [property: JsonPropertyName("base")] GitHubPullRequestRef Base
);

public sealed record GitHubPullRequestRef(
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("sha")] string Sha
);

public sealed record GitHubRepository(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("owner")] GitHubRepositoryOwner Owner
);

public sealed record GitHubRepositoryOwner([property: JsonPropertyName("login")] string Login);

// Dependabot alert webhook payload — same X-Hub-Signature-256 shape, different schema.
// Reference: https://docs.github.com/en/webhooks/webhook-events-and-payloads#dependabot_alert
public sealed record DependabotAlertEvent(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("alert")] DependabotAlert Alert,
    [property: JsonPropertyName("repository")] GitHubRepository? Repository
);

public sealed record DependabotAlert(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("dependency")] DependabotAlertDependency Dependency,
    [property: JsonPropertyName("security_advisory")] DependabotSecurityAdvisory SecurityAdvisory,
    [property: JsonPropertyName("security_vulnerability")]
        DependabotSecurityVulnerability? SecurityVulnerability,
    [property: JsonPropertyName("created_at")] DateTime? CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime? UpdatedAt,
    [property: JsonPropertyName("html_url")] string? HtmlUrl
);

public sealed record DependabotAlertDependency(
    [property: JsonPropertyName("package")] DependabotPackage Package,
    [property: JsonPropertyName("manifest_path")] string? ManifestPath,
    [property: JsonPropertyName("scope")] string? Scope
);

public sealed record DependabotPackage(
    [property: JsonPropertyName("ecosystem")] string Ecosystem,
    [property: JsonPropertyName("name")] string Name
);

public sealed record DependabotSecurityAdvisory(
    [property: JsonPropertyName("ghsa_id")] string GhsaId,
    [property: JsonPropertyName("cve_id")] string? CveId,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("cvss")] DependabotCvss? Cvss,
    [property: JsonPropertyName("published_at")] DateTime? PublishedAt,
    [property: JsonPropertyName("updated_at")] DateTime? UpdatedAt,
    [property: JsonPropertyName("references")] IReadOnlyList<DependabotReference>? References
);

public sealed record DependabotCvss(
    [property: JsonPropertyName("score")] double? Score,
    [property: JsonPropertyName("vector_string")] string? VectorString
);

public sealed record DependabotReference([property: JsonPropertyName("url")] string Url);

public sealed record DependabotSecurityVulnerability(
    [property: JsonPropertyName("package")] DependabotPackage Package,
    [property: JsonPropertyName("vulnerable_version_range")] string? VulnerableVersionRange,
    [property: JsonPropertyName("first_patched_version")]
        DependabotFirstPatched? FirstPatchedVersion
);

public sealed record DependabotFirstPatched(
    [property: JsonPropertyName("identifier")] string Identifier
);

// Admin payload to rotate the two webhook secrets without going through SettingsController.
public sealed record WebhookSecretsRequest(string? GithubSecret, string? DependabotSecret);

public sealed record WebhookSecretsResponse(bool GithubSecretSet, bool DependabotSecretSet);
