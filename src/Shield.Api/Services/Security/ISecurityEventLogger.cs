using Shield.Core.Domain;

namespace Shield.Api.Services.Security;

// Single entry point for writing SecurityEvent rows. Implementations write the row,
// upsert the matching IpReputation rollup, and broadcast a `security.event` SignalR
// frame so the in-app Security view updates without a refresh.
//
// Scoped lifetime — captures ShieldDbContext. Background workers create their own scope.
public interface ISecurityEventLogger
{
    Task LogAsync(SecurityEvent securityEvent, CancellationToken ct = default);

    // Convenience overload for the common Shield-internal emission shape. Defaults
    // Source/EventType to the dotted naming convention; severity defaults to Medium.
    Task LogAsync(
        string source,
        string eventType,
        Severity severity,
        string? remoteIp = null,
        string? userAgent = null,
        string? userName = null,
        string? path = null,
        string? host = null,
        string? jail = null,
        string? detailsJson = null,
        CancellationToken ct = default
    );
}
