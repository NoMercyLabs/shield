using System.Text;
using System.Text.Json;
using Shield.Api.Services;
using Shield.Api.Workers;
using Shield.Api.Workers.Queues;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
public sealed class WebhooksController : ControllerBase
{
    private const string SignatureHeader = "X-Hub-Signature-256";
    private const string EventHeader = "X-GitHub-Event";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IWebhookSignatureValidator _signatureValidator;
    private readonly IWebhookSecretProvider _secrets;
    private readonly IPrCommentService _prCommentService;
    private readonly FeedsDbContext _feedsDb;
    private readonly ShieldDbContext _shieldDb;
    private readonly MatchQueue _matchQueue;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IWebhookSignatureValidator signatureValidator,
        IWebhookSecretProvider secrets,
        IPrCommentService prCommentService,
        FeedsDbContext feedsDb,
        ShieldDbContext shieldDb,
        MatchQueue matchQueue,
        ILogger<WebhooksController> logger
    )
    {
        _signatureValidator = signatureValidator;
        _secrets = secrets;
        _prCommentService = prCommentService;
        _feedsDb = feedsDb;
        _shieldDb = shieldDb;
        _matchQueue = matchQueue;
        _logger = logger;
    }

    [HttpPost("github")]
    [AllowAnonymous]
    public async Task<IActionResult> Github(CancellationToken ct)
    {
        (bool ok, byte[] body, string? error) = await ReadAndVerifyAsync(
            await _secrets.GetGithubSecretAsync(ct),
            ct
        );
        if (!ok)
            return Unauthorized(new { error });

        string? eventType = Request.Headers[EventHeader].FirstOrDefault();
        if (!string.Equals(eventType, "pull_request", StringComparison.Ordinal))
            return Ok(new { ignored = true, eventType });

        GitHubPullRequestEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<GitHubPullRequestEvent>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = $"Invalid pull_request payload: {ex.Message}" });
        }
        if (evt is null)
            return BadRequest(new { error = "Empty pull_request payload" });

        if (evt.Action is not ("opened" or "synchronize" or "reopened"))
            return Ok(new { ignored = true, action = evt.Action });

        PrCommentResult result = await _prCommentService.ProcessPullRequestAsync(
            evt.Repository.Owner.Login,
            evt.Repository.Name,
            evt.PullRequest.Number,
            evt.PullRequest.Head.Ref,
            evt.PullRequest.Base.Ref,
            ct
        );
        return Ok(result);
    }

    [HttpPost("dependabot")]
    [AllowAnonymous]
    public async Task<IActionResult> Dependabot(CancellationToken ct)
    {
        (bool ok, byte[] body, string? error) = await ReadAndVerifyAsync(
            await _secrets.GetDependabotSecretAsync(ct),
            ct
        );
        if (!ok)
            return Unauthorized(new { error });

        DependabotAlertEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<DependabotAlertEvent>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = $"Invalid dependabot payload: {ex.Message}" });
        }
        if (evt is null)
            return BadRequest(new { error = "Empty dependabot payload" });

        Advisory advisory = MapToAdvisory(evt);
        Advisory? existing = await _feedsDb.Advisories.FirstOrDefaultAsync(
            row => row.Feed == advisory.Feed && row.ExternalId == advisory.ExternalId,
            ct
        );
        if (existing is null)
        {
            _feedsDb.Advisories.Add(advisory);
        }
        else
        {
            existing.AffectedRangesJson = advisory.AffectedRangesJson;
            existing.Severity = advisory.Severity;
            existing.Cvss = advisory.Cvss;
            existing.Summary = advisory.Summary;
            existing.ReferencesJson = advisory.ReferencesJson;
            existing.PublishedAt = advisory.PublishedAt;
            existing.ModifiedAt = advisory.ModifiedAt;
            existing.FetchedAt = advisory.FetchedAt;
        }
        await _feedsDb.SaveChangesAsync(ct);

        // Best-effort: if the repo on the alert matches a Source we already track,
        // re-match against the latest snapshot so a fix shows up in real time.
        int? matchedSourceId = null;
        if (evt.Repository is not null)
        {
            string fullName = evt.Repository.FullName;
            Source? source = await _shieldDb.Sources.FirstOrDefaultAsync(
                row => row.Type == SourceType.GithubRepo && row.Name == fullName,
                ct
            );
            if (source is not null)
            {
                matchedSourceId = source.Id;
                await _matchQueue.EnqueueAsync(new(null, source.Id, MatchAll: true), ct);
            }
        }

        return Ok(
            new
            {
                persisted = true,
                advisoryId = advisory.Id,
                externalId = advisory.ExternalId,
                matchedSourceId,
            }
        );
    }

    [HttpPost("secrets")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    public async Task<ActionResult<WebhookSecretsResponse>> SetSecrets(
        [FromBody] WebhookSecretsRequest request,
        CancellationToken ct
    )
    {
        await _secrets.SaveAsync(request.GithubSecret, request.DependabotSecret, ct);
        return Ok(
            new WebhookSecretsResponse(
                GithubSecretSet: !string.IsNullOrEmpty(request.GithubSecret),
                DependabotSecretSet: !string.IsNullOrEmpty(request.DependabotSecret)
            )
        );
    }

    private async Task<(bool Ok, byte[] Body, string? Error)> ReadAndVerifyAsync(
        string? secret,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogWarning("Webhook secret not configured for {Path}", Request.Path);
            return (false, [], "Webhook secret not configured");
        }

        Request.EnableBuffering();
        using MemoryStream buffer = new();
        await Request.Body.CopyToAsync(buffer, ct);
        byte[] payload = buffer.ToArray();

        string? signature = Request.Headers[SignatureHeader].FirstOrDefault();
        if (!_signatureValidator.Verify(signature, payload, secret))
            return (false, payload, "Invalid signature");

        return (true, payload, null);
    }

    // Map Dependabot's alert shape into Shield's Advisory row. We re-use Feed.Ghsa
    // (Dependabot advisories ARE GHSA entries) and mark the dependabot origin in
    // ReferencesJson so the matcher and UI can distinguish cross-validated entries
    // when both an OSV fetch and a Dependabot webhook land for the same id.
    private static Advisory MapToAdvisory(DependabotAlertEvent evt)
    {
        DependabotAlert alert = evt.Alert;
        DependabotSecurityAdvisory advisory = alert.SecurityAdvisory;
        DependabotSecurityVulnerability? vuln = alert.SecurityVulnerability;
        Ecosystem ecosystem = MapEcosystem(alert.Dependency.Package.Ecosystem);

        string affectedRangesJson = BuildAffectedRanges(vuln);
        string referencesJson = BuildReferences(advisory, alert.HtmlUrl);
        DateTime fetchedAt = DateTime.UtcNow;

        return new()
        {
            Id = Guid.NewGuid(),
            Feed = Feed.Ghsa,
            ExternalId = advisory.GhsaId,
            Ecosystem = ecosystem,
            PackageName = alert.Dependency.Package.Name,
            AffectedRangesJson = affectedRangesJson,
            Severity = MapSeverity(advisory.Severity),
            Cvss = advisory.Cvss?.Score,
            Summary = advisory.Summary ?? advisory.Description ?? string.Empty,
            ReferencesJson = referencesJson,
            PublishedAt = advisory.PublishedAt ?? fetchedAt,
            ModifiedAt = advisory.UpdatedAt ?? fetchedAt,
            FetchedAt = fetchedAt,
        };
    }

    private static string BuildAffectedRanges(DependabotSecurityVulnerability? vuln)
    {
        if (vuln is null)
            return "[]";
        List<object> events = [];
        if (!string.IsNullOrWhiteSpace(vuln.VulnerableVersionRange))
            events.Add(new { introduced = "0" });
        if (!string.IsNullOrWhiteSpace(vuln.FirstPatchedVersion?.Identifier))
            events.Add(new { @fixed = vuln.FirstPatchedVersion!.Identifier });

        return JsonSerializer.Serialize(
            new[]
            {
                new
                {
                    type = "SEMVER",
                    events = events.ToArray(),
                    range = vuln.VulnerableVersionRange,
                },
            }
        );
    }

    private static string BuildReferences(DependabotSecurityAdvisory advisory, string? htmlUrl)
    {
        List<object> entries = [new { type = "DEPENDABOT", url = htmlUrl ?? string.Empty }];
        if (advisory.References is not null)
        {
            foreach (DependabotReference reference in advisory.References)
                entries.Add(new { type = "WEB", url = reference.Url });
        }
        if (!string.IsNullOrEmpty(advisory.CveId))
            entries.Add(new { type = "CVE", url = advisory.CveId });
        return JsonSerializer.Serialize(entries);
    }

    // Dependabot ecosystem strings (lowercase) map almost 1:1 to our enum; anything
    // we don't recognise falls back to Npm so the row still persists for cross-check.
    private static Ecosystem MapEcosystem(string raw) =>
        raw?.ToLowerInvariant() switch
        {
            "npm" => Ecosystem.Npm,
            "nuget" => Ecosystem.Nuget,
            "composer" => Ecosystem.Composer,
            "maven" or "gradle" => Ecosystem.Gradle,
            "pip" or "pypi" or "python" => Ecosystem.Python,
            "go" => Ecosystem.Go,
            "rust" or "cargo" => Ecosystem.Rust,
            _ => Ecosystem.Npm,
        };

    private static Severity MapSeverity(string? raw) =>
        raw?.ToLowerInvariant() switch
        {
            "critical" => Severity.Critical,
            "high" => Severity.High,
            "medium" or "moderate" => Severity.Medium,
            _ => Severity.Low,
        };
}
