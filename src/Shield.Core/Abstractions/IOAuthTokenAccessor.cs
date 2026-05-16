using Shield.Core.Domain;

namespace Shield.Core.Abstractions;

// Cross-cutting hook so scanners + feed sync workers can fetch a fresh access token
// for a connected provider without binding to the Shield.Api assembly. The Shield.Api
// implementation also handles the proactive refresh when ExpiresAt is within the
// configured leeway.
public interface IOAuthTokenAccessor
{
    Task<string?> GetAccessTokenAsync(OAuthProvider provider, CancellationToken ct = default);
}
