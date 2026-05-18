using System.Globalization;
using System.Text;
using System.Text.Json;
using Octokit;
using Shield.Core.Results;
using Shield.Scanners;

namespace Shield.Api.Services.Notifications;

public sealed class PrCommentService : IPrCommentService
{
    // Sentinels let us update an existing comment instead of spamming on every PR sync.
    public const string SentinelPrefix = "<!-- shield:pr-";
    public const string SentinelSuffix = " -->";

    private readonly ShieldDbContext _shieldDb;
    private readonly FeedsDbContext _feedsDb;
    private readonly ScannerRegistry _scannerRegistry;
    private readonly IOAuthTokenAccessor _tokenAccessor;
    private readonly IFixSuggester _fixSuggester;
    private readonly IGitHubClientFactory _clientFactory;
    private readonly ILogger<PrCommentService> _logger;

    public PrCommentService(
        ShieldDbContext shieldDb,
        FeedsDbContext feedsDb,
        ScannerRegistry scannerRegistry,
        IOAuthTokenAccessor tokenAccessor,
        IFixSuggester fixSuggester,
        IGitHubClientFactory clientFactory,
        ILogger<PrCommentService> logger
    )
    {
        _shieldDb = shieldDb;
        _feedsDb = feedsDb;
        _scannerRegistry = scannerRegistry;
        _tokenAccessor = tokenAccessor;
        _fixSuggester = fixSuggester;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public async Task<PrCommentResult> ProcessPullRequestAsync(
        string owner,
        string repoName,
        int pullNumber,
        string headRef,
        string baseRef,
        CancellationToken ct
    )
    {
        Source? source = await ResolveSourceAsync(owner, repoName, ct);
        if (source is null)
            return new(false, 0, 0, null, "no matching Source");

        IScanner? scanner = _scannerRegistry.FindFor(SourceType.GithubRepo);
        if (scanner is null)
            return new(false, 0, 0, null, "no GithubRepo scanner registered");

        // Synthetic Source whose ConfigJson points at the PR head — keeps the scanner
        // contract unchanged while letting us scan a non-default branch on demand.
        Source prSource = new()
        {
            Id = source.Id,
            Type = source.Type,
            Name = source.Name,
            ConfigJson = RewriteBranch(source.ConfigJson, headRef),
            ScanInterval = source.ScanInterval,
            Enabled = source.Enabled,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
        };

        ScanResult prScan = await scanner.ScanAsync(prSource, ct);
        if (!prScan.Success || prScan.Snapshot is null)
            return new(true, 0, 0, null, $"scan failed: {prScan.Error}");

        InventorySnapshot? baseSnapshot = await _shieldDb
            .InventorySnapshots.Where(snapshot => snapshot.SourceId == source.Id)
            .OrderByDescending(snapshot => snapshot.TakenAt)
            .FirstOrDefaultAsync(ct);

        IReadOnlyList<InventoryItem> baseItems = baseSnapshot is null
            ? Array.Empty<InventoryItem>()
            : await _shieldDb
                .InventoryItems.Where(item => item.SnapshotId == baseSnapshot.Id)
                .ToListAsync(ct);

        IReadOnlyList<InventoryItem> added = DiffAdded(baseItems, prScan.Items);
        if (added.Count == 0)
            return new(true, 0, 0, null, "no added dependencies");

        List<PrFinding> vulnerable = await MatchAddedAsync(added, ct);
        if (vulnerable.Count == 0)
            return new(true, added.Count, 0, null, "no advisories matched added deps");

        string? token = await _tokenAccessor.GetAccessTokenAsync(OAuthProvider.Github, ct);
        if (string.IsNullOrEmpty(token))
            return new(
                true,
                added.Count,
                vulnerable.Count,
                null,
                "no GitHub token available — connect GitHub in Settings"
            );

        IGitHubClient client = _clientFactory.Create(token);
        string sentinel =
            SentinelPrefix + pullNumber.ToString(CultureInfo.InvariantCulture) + SentinelSuffix;
        string body = RenderMarkdown(vulnerable, sentinel);

        try
        {
            long? commentId = await UpsertCommentAsync(
                client,
                owner,
                repoName,
                pullNumber,
                sentinel,
                body,
                ct
            );
            return new(true, added.Count, vulnerable.Count, commentId, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to post PR comment for {Owner}/{Repo}#{Number}",
                owner,
                repoName,
                pullNumber
            );
            return new(
                true,
                added.Count,
                vulnerable.Count,
                null,
                $"comment post failed: {ex.Message}"
            );
        }
    }

    private async Task<Source?> ResolveSourceAsync(
        string owner,
        string repoName,
        CancellationToken ct
    )
    {
        string fullName = $"{owner}/{repoName}";
        List<Source> candidates = await _shieldDb
            .Sources.Where(source => source.Type == SourceType.GithubRepo)
            .ToListAsync(ct);

        foreach (Source source in candidates)
        {
            if (string.Equals(source.Name, fullName, StringComparison.OrdinalIgnoreCase))
                return source;
            if (MatchesConfig(source.ConfigJson, owner, repoName))
                return source;
        }
        return null;
    }

    private static bool MatchesConfig(string configJson, string owner, string repoName)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(configJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            string? cfgOwner = TryReadString(document.RootElement, "owner");
            string? cfgRepo = TryReadString(document.RootElement, "repo");
            return string.Equals(cfgOwner, owner, StringComparison.OrdinalIgnoreCase)
                && string.Equals(cfgRepo, repoName, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? TryReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement element))
            return null;
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    // Preserves any existing token/owner/repo while swapping branch to the PR head ref.
    private static string RewriteBranch(string configJson, string headRef)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(configJson);
            JsonElement root = document.RootElement;
            string? owner = TryReadString(root, "owner");
            string? repo = TryReadString(root, "repo");
            string? token = TryReadString(root, "token");
            return JsonSerializer.Serialize(
                new
                {
                    owner,
                    repo,
                    branch = headRef,
                    token,
                }
            );
        }
        catch (JsonException)
        {
            return configJson;
        }
    }

    private static IReadOnlyList<InventoryItem> DiffAdded(
        IReadOnlyList<InventoryItem> baseItems,
        IReadOnlyList<InventoryItem> headItems
    )
    {
        HashSet<string> baseKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (InventoryItem item in baseItems)
            baseKeys.Add(Key(item));

        List<InventoryItem> added = [];
        foreach (InventoryItem item in headItems)
        {
            if (!baseKeys.Contains(Key(item)))
                added.Add(item);
        }
        return added;
    }

    private static string Key(InventoryItem item) =>
        item.Ecosystem.ToString() + "|" + item.Name + "|" + item.Version;

    // For each newly added (ecosystem, package) pair, surface any Advisory whose package
    // matches. The matcher will eventually do range-correct filtering during the normal
    // pipeline; the PR comment is best-effort early warning, so package-level is fine.
    private async Task<List<PrFinding>> MatchAddedAsync(
        IReadOnlyList<InventoryItem> added,
        CancellationToken ct
    )
    {
        List<PrFinding> result = [];
        foreach (
            IGrouping<Ecosystem, InventoryItem> ecoGroup in added.GroupBy(item => item.Ecosystem)
        )
        {
            HashSet<string> packageNames = ecoGroup
                .Select(item => item.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<Advisory> hits = await _feedsDb
                .Advisories.Where(advisory =>
                    advisory.Ecosystem == ecoGroup.Key
                    && packageNames.Contains(advisory.PackageName)
                )
                .ToListAsync(ct);

            foreach (InventoryItem item in ecoGroup)
            {
                List<Advisory> itemAdvisories = hits.Where(advisory =>
                        string.Equals(
                            advisory.PackageName,
                            item.Name,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();

                foreach (Advisory advisory in itemAdvisories)
                {
                    FixSuggestion? fix = _fixSuggester.SuggestForPackage(
                        item.Ecosystem,
                        item.Name,
                        item.Version,
                        itemAdvisories
                    );
                    result.Add(new(item, advisory, fix));
                }
            }
        }
        return result;
    }

    private static string RenderMarkdown(IReadOnlyList<PrFinding> findings, string sentinel)
    {
        StringBuilder sb = new();
        sb.AppendLine(sentinel);
        sb.AppendLine("## :shield: Shield: vulnerabilities introduced by this PR");
        sb.AppendLine();
        sb.AppendLine($"Found {findings.Count} advisory match(es) on newly added dependencies.");
        sb.AppendLine();
        sb.AppendLine("| Severity | Package | Version | Advisory | Fix |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (PrFinding finding in findings.OrderByDescending(entry => entry.Advisory.Severity))
        {
            string fixCell = finding.Fix is null
                ? "_no fix available_"
                : $"`{finding.Fix.SuggestedVersion}`";
            sb.Append(
                CultureInfo.InvariantCulture,
                $"| **{finding.Advisory.Severity}** | `{finding.Item.Name}` | `{finding.Item.Version}` | `{finding.Advisory.ExternalId}` | {fixCell} |"
            );
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("<sub>Posted by Shield. Comment is updated on every PR sync.</sub>");
        return sb.ToString();
    }

    private static async Task<long?> UpsertCommentAsync(
        IGitHubClient client,
        string owner,
        string repoName,
        int pullNumber,
        string sentinel,
        string body,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<IssueComment> existing = await client.Issue.Comment.GetAllForIssue(
            owner,
            repoName,
            pullNumber
        );
        IssueComment? prior = existing.FirstOrDefault(comment =>
            comment.Body?.Contains(sentinel, StringComparison.Ordinal) == true
        );
        if (prior is null)
        {
            IssueComment created = await client.Issue.Comment.Create(
                owner,
                repoName,
                pullNumber,
                body
            );
            return created.Id;
        }
        IssueComment updated = await client.Issue.Comment.Update(owner, repoName, prior.Id, body);
        return updated.Id;
    }

    private sealed record PrFinding(InventoryItem Item, Advisory Advisory, FixSuggestion? Fix);
}

// Mockable factory wrapper around `new GitHubClient(...) { Credentials = ... }` so tests
// can swap in a substitute IGitHubClient without spinning up an HTTP server. Production
// path wires the GitHubRateLimitHandler underneath Octokit's HttpClientAdapter so the
// Octokit calls share the same primary/secondary/5xx policy as the raw HttpClient path.
public interface IGitHubClientFactory
{
    IGitHubClient Create(string accessToken);
}

public sealed class GitHubClientFactory : IGitHubClientFactory
{
    private static readonly ProductHeaderValue ProductHeader = new("Shield");
    private readonly IServiceProvider _services;

    public GitHubClientFactory(IServiceProvider services)
    {
        _services = services;
    }

    public IGitHubClient Create(string accessToken)
    {
        // DelegatingHandler MUST have an InnerHandler at the bottom of its chain — the
        // Transient registration gives us a fresh handler each call, so setting it here
        // is safe (no risk of "handler already assigned" from a sibling pipeline).
        Octokit.Internal.HttpClientAdapter adapter = new(() =>
        {
            Shield.Api.Http.GitHubRateLimitHandler handler =
                _services.GetRequiredService<Shield.Api.Http.GitHubRateLimitHandler>();
            handler.InnerHandler ??= new HttpClientHandler();
            return handler;
        });
        Connection connection = new(
            ProductHeader,
            GitHubClient.GitHubApiUrl,
            new Octokit.Internal.InMemoryCredentialStore(new(accessToken)),
            adapter,
            new Octokit.Internal.SimpleJsonSerializer()
        );
        return new GitHubClient(connection);
    }
}
