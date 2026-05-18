using System.Text.Json.Serialization;

namespace Shield.Scanners;

// Pure-C# `.git/config` reader + git remote URL parser. No shelling out, no
// LibGit2 dep — Shield only needs origin to surface "this folder ships to GitHub".
public static class GitRemoteParser
{
    public static IReadOnlyList<string> DefaultActionableHosts { get; } =
        ["github.com", "gitlab.com", "bitbucket.org"];

    // Tries to read `<workingTree>/.git/config`, find `[remote "origin"]`, and parse its `url`.
    // Returns null if no working tree, no .git/config, no origin block, or unparseable URL.
    public static DetectedRemote? DetectFromWorkingTree(string workingTreeRoot)
    {
        if (string.IsNullOrWhiteSpace(workingTreeRoot))
            return null;

        string configPath = Path.Combine(workingTreeRoot, ".git", "config");
        if (!File.Exists(configPath))
            return null;

        string? originUrl;
        string? branch;
        try
        {
            string[] lines = File.ReadAllLines(configPath);
            originUrl = FindRemoteUrl(lines, "origin");
            branch = FindHeadBranch(workingTreeRoot);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(originUrl))
            return null;

        DetectedRemote? parsed = ParseRemoteUrl(originUrl);
        if (parsed is null)
            return null;

        return string.IsNullOrWhiteSpace(branch) ? parsed : parsed with { Branch = branch };
    }

    // Parses well-known git URL forms. Returns null on malformed input.
    public static DetectedRemote? ParseRemoteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        string trimmed = url.Trim();

        // SCP-like: git@host:owner/repo(.git)?
        if (!trimmed.Contains("://") && trimmed.Contains('@') && trimmed.Contains(':'))
        {
            int at = trimmed.IndexOf('@');
            int colon = trimmed.IndexOf(':', at);
            if (colon <= at + 1)
                return null;
            string host = trimmed.Substring(at + 1, colon - at - 1);
            string pathPart = trimmed[(colon + 1)..];
            return BuildFromHostAndPath(host, pathPart, trimmed);
        }

        // URI schemes: https://, http://, ssh://, git://
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            string scheme = uri.Scheme.ToLowerInvariant();
            if (scheme != "https" && scheme != "http" && scheme != "ssh" && scheme != "git")
                return null;
            string host = uri.Host;
            string pathPart = uri.AbsolutePath.TrimStart('/');
            return BuildFromHostAndPath(host, pathPart, trimmed);
        }

        return null;
    }

    private static DetectedRemote? BuildFromHostAndPath(string host, string pathPart, string originalUrl)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(pathPart))
            return null;

        string normalised = pathPart.TrimEnd('/');
        if (normalised.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            normalised = normalised[..^4];

        string[] parts = normalised.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        // Some hosts (Gitea/Forgejo subgroups, GitLab) nest groups — last segment is the repo,
        // everything before is the owner path. Join with `/` so the original namespace survives.
        string repo = parts[^1];
        string owner = string.Join('/', parts[..^1]);

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return null;

        return new(host.ToLowerInvariant(), owner, repo, originalUrl, Branch: null);
    }

    private static string? FindRemoteUrl(IReadOnlyList<string> lines, string remoteName)
    {
        string header = $"[remote \"{remoteName}\"]";
        bool inBlock = false;
        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inBlock = string.Equals(line, header, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inBlock)
                continue;

            int equals = line.IndexOf('=');
            if (equals <= 0)
                continue;
            string key = line[..equals].Trim();
            string value = line[(equals + 1)..].Trim();
            if (string.Equals(key, "url", StringComparison.OrdinalIgnoreCase))
                return value;
        }
        return null;
    }

    private static string? FindHeadBranch(string workingTreeRoot)
    {
        string headPath = Path.Combine(workingTreeRoot, ".git", "HEAD");
        if (!File.Exists(headPath))
            return null;
        try
        {
            string head = File.ReadAllText(headPath).Trim();
            const string prefix = "ref: refs/heads/";
            return head.StartsWith(prefix, StringComparison.Ordinal) ? head[prefix.Length..] : null;
        }
        catch
        {
            return null;
        }
    }
}

// Policy: which detected hosts are "actionable" for promote-to-GitHub. Backed by AppSettings.
public interface IDetectedRemoteHostPolicy
{
    IReadOnlyCollection<string> ActionableHosts { get; }
}

public sealed record DetectedRemote(
    [property: JsonPropertyName("host")] string Host,
    [property: JsonPropertyName("owner")] string Owner,
    [property: JsonPropertyName("repo")] string Repo,
    [property: JsonPropertyName("remoteUrl")] string RemoteUrl,
    [property: JsonPropertyName("branch")] string? Branch = null
);
