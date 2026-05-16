using System.Text.Json;
using System.Text.Json.Serialization;
using Octokit;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Scanners;

public sealed class GitHubRepoScanner : IScanner
{
    readonly IGitHubClient _client;
    readonly ParserRegistry _parsers;

    public GitHubRepoScanner(IGitHubClient client, ParserRegistry parsers)
    {
        _client = client;
        _parsers = parsers;
    }

    public SourceType SourceType => SourceType.GithubRepo;

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

        if (config is null || string.IsNullOrWhiteSpace(config.Owner) || string.IsNullOrWhiteSpace(config.Repo))
            return ScanResult.Fail("GithubRepo config missing 'owner' or 'repo'");

        string branch = string.IsNullOrWhiteSpace(config.Branch) ? "main" : config.Branch!;

        Repository repo;
        try
        {
            repo = await _client.Repository.Get(config.Owner, config.Repo).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ScanResult.Fail($"Failed to fetch repo: {ex.Message}");
        }

        Reference reference;
        try
        {
            reference = await _client.Git.Reference
                .Get(repo.Id, $"heads/{branch}")
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ScanResult.Fail($"Failed to fetch branch '{branch}': {ex.Message}");
        }

        TreeResponse tree;
        try
        {
            tree = await _client.Git.Tree
                .GetRecursive(repo.Id, reference.Object.Sha)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ScanResult.Fail($"Failed to fetch tree: {ex.Message}");
        }

        List<InventoryItem> aggregated = new();

        foreach (TreeItem entry in tree.Tree)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.Type.Value != TreeType.Blob)
                continue;

            string filename = Path.GetFileName(entry.Path);
            IParser? parser = _parsers.FindFor(filename);
            if (parser is null)
                continue;

            Blob blob;
            try
            {
                blob = await _client.Git.Blob.Get(repo.Id, entry.Sha).ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            byte[] bytes = blob.Encoding.Value == EncodingType.Base64
                ? Convert.FromBase64String(blob.Content)
                : System.Text.Encoding.UTF8.GetBytes(blob.Content);

            using MemoryStream stream = new(bytes);
            ParseResult parsed = await parser
                .ParseAsync(stream, filename, ct)
                .ConfigureAwait(false);

            if (parsed.Success)
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
}
