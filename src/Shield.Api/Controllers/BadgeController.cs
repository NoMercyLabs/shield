using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Net.Http.Headers;
using Shield.Api.Services;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/badge")]
[AllowAnonymous]
public sealed class BadgeController : ControllerBase
{
    public const string SvgContentType = "image/svg+xml";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ShieldDbContext _db;
    private readonly IBadgeRenderer _renderer;
    private readonly IMemoryCache _cache;

    public BadgeController(ShieldDbContext db, IBadgeRenderer renderer, IMemoryCache cache)
    {
        _db = db;
        _renderer = renderer;
        _cache = cache;
    }

    [HttpGet("{owner}/{repo}.svg")]
    public async Task<IActionResult> Get(string owner, string repo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return Svg(_renderer.RenderNotWatched());

        string cacheKey = $"badge::{owner.ToLowerInvariant()}/{repo.ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
            return Svg(cached);

        Source? source = await ResolveSourceAsync(owner, repo, ct);
        string svg;
        if (source is null)
        {
            svg = _renderer.RenderNotWatched();
        }
        else
        {
            (int critical, int high, int medium, int low) = await CountOpenAsync(source.Id, ct);
            svg = _renderer.Render(critical, high, medium, low);
        }

        _cache.Set(cacheKey, svg, CacheTtl);
        return Svg(svg);
    }

    private async Task<Source?> ResolveSourceAsync(string owner, string repo, CancellationToken ct)
    {
        string fullName = $"{owner}/{repo}";
        List<Source> candidates = await _db
            .Sources.Where(source => source.Type == SourceType.GithubRepo)
            .ToListAsync(ct);

        foreach (Source source in candidates)
        {
            if (string.Equals(source.Name, fullName, StringComparison.OrdinalIgnoreCase))
                return source;
            if (ConfigMatches(source.ConfigJson, owner, repo))
                return source;
        }
        return null;
    }

    private static bool ConfigMatches(string configJson, string owner, string repo)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(configJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            string? cfgOwner = TryReadString(document.RootElement, "owner");
            string? cfgRepo = TryReadString(document.RootElement, "repo");
            return string.Equals(cfgOwner, owner, StringComparison.OrdinalIgnoreCase)
                && string.Equals(cfgRepo, repo, StringComparison.OrdinalIgnoreCase);
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

    private async Task<(int Critical, int High, int Medium, int Low)> CountOpenAsync(
        int sourceId,
        CancellationToken ct
    )
    {
        var grouped = await _db
            .Findings.Where(finding =>
                finding.SourceId == sourceId && finding.State == FindingState.Open
            )
            .GroupBy(finding => finding.Severity)
            .Select(group => new { Severity = group.Key, Count = group.Count() })
            .ToListAsync(ct);

        int critical = 0;
        int high = 0;
        int medium = 0;
        int low = 0;
        foreach (var entry in grouped)
        {
            switch (entry.Severity)
            {
                case Severity.Critical:
                    critical = entry.Count;
                    break;
                case Severity.High:
                    high = entry.Count;
                    break;
                case Severity.Medium:
                    medium = entry.Count;
                    break;
                case Severity.Low:
                    low = entry.Count;
                    break;
            }
        }
        return (critical, high, medium, low);
    }

    private ContentResult Svg(string body)
    {
        Response.Headers[HeaderNames.CacheControl] =
            $"public, max-age={(int)CacheTtl.TotalSeconds}";
        return Content(body, SvgContentType);
    }
}
