using System.Text.Json;
using System.Text.Json.Serialization;
using Shield.Api.Services.Scanning;
using Shield.Core.Domain;

namespace Shield.Api.Services.Webhooks;

// When a GitHub / Gitea / Forgejo pull_request webhook lands with action=closed, enqueue a
// scan of the matching Source so findings + bulk-apply state reflect what the merge changed.
// Without this, the dashboard only refreshes on the scheduled cadence — Stoney merged a fix
// PR and rightly expected Shield to notice. GitLab uses merge_request events with a slightly
// different shape; handled separately when the time comes.
public sealed class PullRequestClosedRescanHandler : IInboundWebhookHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly ShieldDbContext _db;
    private readonly IPersistentScanQueue _scanQueue;
    private readonly ILogger<PullRequestClosedRescanHandler> _logger;

    public PullRequestClosedRescanHandler(
        ShieldDbContext db,
        IPersistentScanQueue scanQueue,
        ILogger<PullRequestClosedRescanHandler> logger
    )
    {
        _db = db;
        _scanQueue = scanQueue;
        _logger = logger;
    }

    public async Task HandleAsync(InboundWebhookContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EventType, "pull_request", StringComparison.OrdinalIgnoreCase))
            return;
        if (
            context.Provider
            is not (OAuthProvider.Github or OAuthProvider.Gitea or OAuthProvider.Forgejo)
        )
            return;

        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(context.Payload, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not parse pull_request payload for envelope dispatch");
            return;
        }
        if (payload?.Repository?.FullName is null)
            return;
        if (!string.Equals(payload.Action, "closed", StringComparison.OrdinalIgnoreCase))
            return;

        string repoFullName = payload.Repository.FullName;
        Source? source = await _db.Sources.FirstOrDefaultAsync(
            row => row.Type == SourceType.GithubRepo && row.Name == repoFullName,
            ct
        );
        if (source is null)
        {
            _logger.LogDebug(
                "pull_request closed for {Repo} but no matching Source — ignored",
                repoFullName
            );
            return;
        }

        await _scanQueue.EnqueueAsync(source.Id, ct);
        _logger.LogInformation(
            "Enqueued rescan for source {SourceId} ({Repo}) after PR #{Number} closed",
            source.Id,
            repoFullName,
            payload.PullRequest?.Number ?? 0
        );
    }

    private sealed record Payload(
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("pull_request")] PrSummary? PullRequest,
        [property: JsonPropertyName("repository")] RepoSummary? Repository
    );

    private sealed record PrSummary([property: JsonPropertyName("number")] int Number);

    private sealed record RepoSummary([property: JsonPropertyName("full_name")] string? FullName);
}
