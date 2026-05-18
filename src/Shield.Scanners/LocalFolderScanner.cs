using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Scanners;

public sealed class LocalFolderScanner : IScanner
{
    // Shared with the GitHub repo scanner via ScannerIgnoreDefaults so adding a new
    // fixture / build-output exclusion lands in both code paths in one edit.
    private static readonly IReadOnlyList<string> DefaultIgnoreGlobs =
        ScannerIgnoreDefaults.Segments;

    private readonly ParserRegistry _parsers;
    private readonly IDetectedRemoteHostPolicy? _remoteHostPolicy;

    public LocalFolderScanner(
        ParserRegistry parsers,
        IDetectedRemoteHostPolicy? remoteHostPolicy = null
    )
    {
        _parsers = parsers;
        _remoteHostPolicy = remoteHostPolicy;
    }

    public SourceType SourceType => SourceType.LocalFolder;

    public async ValueTask<ScanResult> ScanAsync(Source source, CancellationToken ct)
    {
        LocalFolderConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<LocalFolderConfig>(source.ConfigJson);
        }
        catch (JsonException ex)
        {
            return ScanResult.Fail($"Invalid LocalFolder config JSON: {ex.Message}");
        }

        if (config is null || string.IsNullOrWhiteSpace(config.Path))
            return ScanResult.Fail("LocalFolder config missing 'path'");

        if (!Directory.Exists(config.Path))
            return ScanResult.Fail($"Path does not exist: {config.Path}");

        UpdateDetectedRemote(source, config.Path);

        IReadOnlyList<string> ignore = config.IgnoreGlobs is { Count: > 0 }
            ? config.IgnoreGlobs
            : DefaultIgnoreGlobs;
        HashSet<string> ignoreSet = new(ignore, StringComparer.OrdinalIgnoreCase);

        List<InventoryItem> aggregated = [];

        foreach (string filePath in EnumerateFiles(config.Path, ignoreSet))
        {
            ct.ThrowIfCancellationRequested();

            string filename = Path.GetFileName(filePath);
            IParser? parser = _parsers.FindFor(filename);
            if (parser is null)
                continue;

            string relativeManifestPath = Path.GetRelativePath(config.Path, filePath)
                .Replace('\\', '/');

            await using FileStream stream = File.OpenRead(filePath);
            ParseResult parsed = await parser
                .ParseAsync(stream, filename, ct)
                .ConfigureAwait(false);

            if (!parsed.Success)
                continue;

            foreach (InventoryItem item in parsed.Items)
                item.ManifestPath = relativeManifestPath;

            aggregated.AddRange(parsed.Items);
        }

        Guid snapshotId = Guid.NewGuid();
        DateTime takenAt = DateTime.UtcNow;
        string contentsSha = ComputeContentsSha(aggregated);

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

    // Refresh source.DetectedRemote from the live `.git/config`. Mutates source in place;
    // the scan worker writes the row back to the DB so the API picks up the change.
    private void UpdateDetectedRemote(Source source, string path)
    {
        DetectedRemote? detected = GitRemoteParser.DetectFromWorkingTree(path);
        string? serialised = null;
        if (detected is not null)
        {
            IEnumerable<string> hosts =
                _remoteHostPolicy?.ActionableHosts ?? GitRemoteParser.DefaultActionableHosts;
            if (hosts.Contains(detected.Host, StringComparer.OrdinalIgnoreCase))
                serialised = JsonSerializer.Serialize(detected);
        }
        if (!string.Equals(source.DetectedRemote, serialised, StringComparison.Ordinal))
            source.DetectedRemote = serialised;
    }

    private static IEnumerable<string> EnumerateFiles(string root, HashSet<string> ignoreSet)
    {
        Stack<string> stack = new();
        stack.Push(root);

        while (stack.Count > 0)
        {
            string current = stack.Pop();
            IEnumerable<string> subdirs;
            IEnumerable<string> files;
            try
            {
                subdirs = Directory.EnumerateDirectories(current);
                files = Directory.EnumerateFiles(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string dir in subdirs)
            {
                string name = Path.GetFileName(dir);
                if (ignoreSet.Contains(name))
                    continue;
                stack.Push(dir);
            }

            foreach (string file in files)
                yield return file;
        }
    }

    internal static string ComputeContentsSha(IReadOnlyList<InventoryItem> items)
    {
        IEnumerable<string> lines = items
            .Select(item => $"{item.Ecosystem}|{item.Name}|{item.Version}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(line => line, StringComparer.Ordinal);

        StringBuilder builder = new();
        foreach (string line in lines)
            builder.Append(line).Append('\n');

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class LocalFolderConfig
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("ignoreGlobs")]
    public List<string>? IgnoreGlobs { get; set; }
}
