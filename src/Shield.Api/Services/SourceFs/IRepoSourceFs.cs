namespace Shield.Api.Services.SourceFs;

// Read-only filesystem abstraction over a Source's content. GithubRepo sources fetch via the
// GitHub Contents API; LocalFolder/LinuxHost variants will hit disk / SSH respectively. Editors
// and apply paths consume this instead of switching on SourceType themselves.
public interface IRepoSourceFs
{
    // Fetches the raw file content at `path` from the source's default branch (or per-source
    // branch override for GithubRepo). Returns null when the file doesn't exist in the source.
    Task<string?> ReadFileAsync(Source source, string path, CancellationToken ct);
}
