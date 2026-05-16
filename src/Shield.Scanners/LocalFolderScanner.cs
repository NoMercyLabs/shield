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
    static readonly IReadOnlyList<string> DefaultIgnoreGlobs = new[]
    {
        "node_modules",
        "vendor",
        "bin",
        "obj",
        ".git",
    };

    readonly ParserRegistry _parsers;

    public LocalFolderScanner(ParserRegistry parsers)
    {
        _parsers = parsers;
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

        IReadOnlyList<string> ignore = config.IgnoreGlobs is { Count: > 0 }
            ? config.IgnoreGlobs
            : DefaultIgnoreGlobs;
        HashSet<string> ignoreSet = new(ignore, StringComparer.OrdinalIgnoreCase);

        List<InventoryItem> aggregated = new();

        foreach (string filePath in EnumerateFiles(config.Path, ignoreSet))
        {
            ct.ThrowIfCancellationRequested();

            string filename = Path.GetFileName(filePath);
            IParser? parser = _parsers.FindFor(filename);
            if (parser is null)
                continue;

            await using FileStream stream = File.OpenRead(filePath);
            ParseResult parsed = await parser
                .ParseAsync(stream, filename, ct)
                .ConfigureAwait(false);

            if (parsed.Success)
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

    static IEnumerable<string> EnumerateFiles(string root, HashSet<string> ignoreSet)
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
