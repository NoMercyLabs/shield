namespace Shield.Api.Services.BulkFix;

// Lists currently-open Dependabot PRs in a GitHub repo so the bulk-apply orchestrator can
// warn (or block) before Shield creates a competing PR that would conflict. Empty list when
// no GitHub token is available — caller must decide whether to fail-open or fail-closed.
public interface IDependabotPrLookup
{
    Task<IReadOnlyList<DependabotPrSummary>> ListOpenAsync(
        string repoFullName,
        CancellationToken ct = default
    );
}

public sealed record DependabotPrSummary(
    int Number,
    string Title,
    string HtmlUrl,
    DateTime CreatedAt
);
