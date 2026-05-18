using System.Text.Json;
using Octokit;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Scanners;

namespace Shield.Api.Services.SourceFs;

public sealed class GithubRepoSourceFs : IRepoSourceFs
{
    private static readonly ProductHeaderValue ProductHeader = new("Shield");

    private readonly IOAuthTokenAccessor _tokenAccessor;

    public GithubRepoSourceFs(IOAuthTokenAccessor tokenAccessor)
    {
        _tokenAccessor = tokenAccessor;
    }

    public async Task<string?> ReadFileAsync(Source source, string path, CancellationToken ct)
    {
        if (source.Type != SourceType.GithubRepo)
            return null;

        GitHubRepoConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<GitHubRepoConfig>(source.ConfigJson);
        }
        catch (JsonException)
        {
            return null;
        }
        if (
            config is null
            || string.IsNullOrWhiteSpace(config.Owner)
            || string.IsNullOrWhiteSpace(config.Repo)
        )
            return null;

        string? token = await _tokenAccessor.GetAccessTokenAsync(OAuthProvider.Github, ct);
        if (string.IsNullOrEmpty(token) && !string.IsNullOrWhiteSpace(config.Token))
            token = config.Token;
        if (string.IsNullOrEmpty(token))
            return null;

        GitHubClient client = new(ProductHeader) { Credentials = new(token) };
        Repository repo;
        try
        {
            repo = await client.Repository.Get(config.Owner, config.Repo);
        }
        catch
        {
            return null;
        }
        string branch = string.IsNullOrWhiteSpace(config.Branch)
            ? repo.DefaultBranch
            : config.Branch!;

        try
        {
            IReadOnlyList<RepositoryContent> contents =
                await client.Repository.Content.GetAllContentsByRef(repo.Id, path, branch);
            return contents.Count > 0 ? contents[0].Content : null;
        }
        catch (NotFoundException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }
}
