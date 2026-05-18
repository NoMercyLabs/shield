using System.Text.Json;
using System.Text.Json.Serialization;
using Octokit;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Scanners;

// Factory hook so Shield.Api can inject an Octokit client wired to the rate-limit handler
// AND an OAuth-resolved bearer token, without Shield.Scanners taking a direct dependency
// on Shield.Api's IGitHubClientFactory (and through it the entire OAuth token store).
public interface IGitHubScannerClientFactory
{
    Task<IGitHubClient> CreateForSourceAsync(Source source, CancellationToken ct);
}

public sealed class GitHubRepoScanner : IScanner
{
    private readonly IGitHubScannerClientFactory _clientFactory;
    private readonly ParserRegistry _parsers;

    public GitHubRepoScanner(IGitHubScannerClientFactory clientFactory, ParserRegistry parsers)
    {
        _clientFactory = clientFactory;
        _parsers = parsers;
    }

    public SourceType SourceType => SourceType.GithubRepo;

    private static DateTimeOffset ParseRateLimitReset(RateLimitExceededException ex)
    {
        DateTimeOffset reset = ex.Reset;
        return reset > DateTimeOffset.UtcNow ? reset : DateTimeOffset.UtcNow.AddMinutes(1);
    }

    public async ValueTask<ScanResult> ScanAsync(Source source, CancellationToken ct)
    {
        GitHubRepoConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<GitHubRepoConfig>(source.ConfigJson);
        }
        catch (JsonException ex)
        {
            return ScanResult.Fail($"Invalid GithubRepo config JSON: {ex.Message}");
        }

        if (
            config is null
            || string.IsNullOrWhiteSpace(config.Owner)
            || string.IsNullOrWhiteSpace(config.Repo)
        )
            return ScanResult.Fail("GithubRepo config missing 'owner' or 'repo'");

        string branch = string.IsNullOrWhiteSpace(config.Branch) ? "main" : config.Branch!;

        IGitHubClient client = await _clientFactory
            .CreateForSourceAsync(source, ct)
            .ConfigureAwait(false);

        Repository repo;
        try
        {
            repo = await client.Repository.Get(config.Owner, config.Repo).ConfigureAwait(false);
        }
        catch (RateLimitExceededException ex)
        {
            throw new GitHubScanRateLimitedException(ParseRateLimitReset(ex));
        }
        catch (AbuseException ex)
        {
            throw new GitHubScanRateLimitedException(
                DateTimeOffset.UtcNow.AddSeconds(ex.RetryAfterSeconds ?? 60)
            );
        }
        catch (Exception ex)
        {
            return ScanResult.Fail($"Failed to fetch repo: {ex.Message}");
        }

        Reference reference;
        try
        {
            reference = await client
                .Git.Reference.Get(repo.Id, $"heads/{branch}")
                .ConfigureAwait(false);
        }
        catch (RateLimitExceededException ex)
        {
            throw new GitHubScanRateLimitedException(ParseRateLimitReset(ex));
        }
        catch (AbuseException ex)
        {
            throw new GitHubScanRateLimitedException(
                DateTimeOffset.UtcNow.AddSeconds(ex.RetryAfterSeconds ?? 60)
            );
        }
        catch (Exception ex)
        {
            return ScanResult.Fail($"Failed to fetch branch '{branch}': {ex.Message}");
        }

        TreeResponse tree;
        try
        {
            tree = await client
                .Git.Tree.GetRecursive(repo.Id, reference.Object.Sha)
                .ConfigureAwait(false);
        }
        catch (RateLimitExceededException ex)
        {
            throw new GitHubScanRateLimitedException(ParseRateLimitReset(ex));
        }
        catch (AbuseException ex)
        {
            throw new GitHubScanRateLimitedException(
                DateTimeOffset.UtcNow.AddSeconds(ex.RetryAfterSeconds ?? 60)
            );
        }
        catch (Exception ex)
        {
            return ScanResult.Fail($"Failed to fetch tree: {ex.Message}");
        }

        List<InventoryItem> aggregated = [];

        // Path-segment ignore set — defaults skip Fixtures / __pycache__ / node_modules / …
        // unless the source config overrides with its own list. Empty array means "scan
        // everything"; null means "use defaults".
        IReadOnlyList<string> ignore = config.IgnoreGlobs is null
            ? ScannerIgnoreDefaults.Segments
            : config.IgnoreGlobs;
        HashSet<string> ignoreSet = new(ignore, StringComparer.OrdinalIgnoreCase);

        foreach (TreeItem entry in tree.Tree)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.Type.Value != TreeType.Blob)
                continue;

            if (
                ignoreSet.Count > 0
                && ScannerIgnoreDefaults.ContainsIgnoredSegment(entry.Path, ignoreSet)
            )
                continue;

            string filename = Path.GetFileName(entry.Path);
            IParser? parser = _parsers.FindFor(filename);
            if (parser is null)
                continue;

            Blob blob;
            try
            {
                blob = await client.Git.Blob.Get(repo.Id, entry.Sha).ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            byte[] bytes =
                blob.Encoding.Value == EncodingType.Base64
                    ? Convert.FromBase64String(blob.Content)
                    : System.Text.Encoding.UTF8.GetBytes(blob.Content);

            using MemoryStream stream = new(bytes);
            ParseResult parsed = await parser
                .ParseAsync(stream, filename, ct)
                .ConfigureAwait(false);

            if (!parsed.Success)
                continue;

            // entry.Path is the forward-slash relative path from the repo root (GitHub API
            // always returns forward slashes), e.g. "packages/foo/package.json".
            string relativeManifestPath = entry.Path;
            foreach (InventoryItem item in parsed.Items)
                item.ManifestPath = relativeManifestPath;

            aggregated.AddRange(parsed.Items);
        }

        Guid snapshotId = Guid.NewGuid();
        DateTime takenAt = DateTime.UtcNow;
        string contentsSha = LocalFolderScanner.ComputeContentsSha(aggregated);

        InventorySnapshot snapshot = new()
        {
            Id = snapshotId,
            SourceId = source.Id,
            TakenAt = takenAt,
            ContentsSha = contentsSha,
            ItemCount = aggregated.Count,
        };

        foreach (InventoryItem item in aggregated)
            item.SnapshotId = snapshotId;

        return ScanResult.Ok(snapshot, aggregated);
    }
}

public sealed class GitHubRepoConfig
{
    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("repo")]
    public string Repo { get; set; } = string.Empty;

    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    // Per-source override for ScannerIgnoreDefaults — paths whose any segment matches one
    // of these names are dropped from the inventory. When null, defaults apply
    // (Fixtures / __pycache__ / node_modules / …). Set to an empty array to scan everything.
    [JsonPropertyName("ignoreGlobs")]
    public List<string>? IgnoreGlobs { get; set; }
}
