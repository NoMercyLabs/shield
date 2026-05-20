using Microsoft.Extensions.Options;
using Shield.Core.Abstractions;

namespace Shield.Feeds.Ghsa;

// Reads the optional GhsaOptions.Pat directly from config. Shield.Api replaces this with
// OAuthBackedGhsaTokenSource so a user who's already authenticated to GitHub via the SPA
// (connect-flow or signin) doesn't have to also paste a PAT into config.
public sealed class GhsaPatTokenSource : IGhsaAuthTokenSource
{
    private readonly IOptionsMonitor<GhsaOptions> _options;

    public GhsaPatTokenSource(IOptionsMonitor<GhsaOptions> options)
    {
        _options = options;
    }

    public ValueTask<string?> GetTokenAsync(CancellationToken ct = default)
    {
        string? pat = _options.CurrentValue.Pat;
        return ValueTask.FromResult(string.IsNullOrWhiteSpace(pat) ? null : pat);
    }
}
