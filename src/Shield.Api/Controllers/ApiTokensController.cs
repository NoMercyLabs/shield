using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Shield.Api.Auth;
using Shield.Api.Contracts;
using Shield.Api.Services;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Data.Identity;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[NoApiToken]
public sealed class ApiTokensController : ControllerBase
{
    private static readonly HashSet<string> KnownScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "findings:read",
        "findings:write",
        "sources:read",
        "sbom:write",
    };

    private readonly IApiTokenStore _store;
    private readonly UserManager<ShieldUser> _userManager;
    private readonly INotificationPublisher _notifications;
    private readonly ISecurityEventLogger _securityLog;
    private readonly ILogger<ApiTokensController> _log;

    public ApiTokensController(
        IApiTokenStore store,
        UserManager<ShieldUser> userManager,
        INotificationPublisher notifications,
        ISecurityEventLogger securityLog,
        ILogger<ApiTokensController> log
    )
    {
        _store = store;
        _userManager = userManager;
        _notifications = notifications;
        _securityLog = securityLog;
        _log = log;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApiTokenSummary>>> List(
        [FromQuery] bool all = false,
        CancellationToken ct = default
    )
    {
        Guid userId = await ResolveUserIdAsync();
        bool isAdmin = User.IsInRole(ShieldRoles.Admin);
        IReadOnlyList<ApiToken> rows =
            all && isAdmin
                ? await _store.ListAllAsync(ct)
                : await _store.ListForUserAsync(userId, ct);
        return Ok(rows.Select(Project).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<CreateApiTokenResponse>> Create(
        [FromBody] CreateApiTokenRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required." });
        if (request.Scopes is null || request.Scopes.Count == 0)
            return BadRequest(new { error = "At least one scope is required." });

        List<string> unknown = request.Scopes.Where(scope => !KnownScopes.Contains(scope)).ToList();
        if (unknown.Count > 0)
            return BadRequest(
                new
                {
                    error = "Unknown scope(s).",
                    unknown,
                    known = KnownScopes.ToArray(),
                }
            );

        DateTime? expiresAt =
            request.ExpiresInDays is { } days && days > 0 ? DateTime.UtcNow.AddDays(days) : null;

        Guid userId = await ResolveUserIdAsync();
        (ApiToken token, string plaintext) = await _store.CreateAsync(
            userId,
            request.Name,
            request.Scopes,
            expiresAt,
            request.SourceIdFilter ?? [],
            ct
        );

        try
        {
            await _securityLog.LogAsync(
                source: "shield.auth",
                eventType: "apitoken.created",
                severity: Severity.Low,
                ct: ct
            );
        }
        catch (Exception ex)
        {
            _log.LogBestEffortFailure(ex);
        }

        try
        {
            await _notifications.PublishAsync(
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Kind = NotificationKind.SystemMessage,
                    Severity = Severity.Low,
                    Title = "API token created",
                    Body = $"API token '{token.Name}' created.",
                    RelatedType = "ApiToken",
                    RelatedId = token.Id.ToString(),
                    CreatedAt = DateTime.UtcNow,
                },
                ct
            );
        }
        catch (Exception ex)
        {
            _log.LogBestEffortFailure(ex);
        }

        return Ok(new CreateApiTokenResponse(Project(token), plaintext));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        Guid userId = await ResolveUserIdAsync();
        bool isAdmin = User.IsInRole(ShieldRoles.Admin);

        // Fetch name before revoke so we can include it in the notification body.
        IReadOnlyList<ApiToken> tokens = isAdmin
            ? await _store.ListAllAsync(ct)
            : await _store.ListForUserAsync(userId, ct);
        ApiToken? target = tokens.FirstOrDefault(token => token.Id == id);

        bool ok = await _store.RevokeAsync(id, userId, isAdmin, ct);
        if (!ok)
            return NotFound();

        string tokenName = target?.Name ?? id.ToString();

        try
        {
            await _securityLog.LogAsync(
                source: "shield.auth",
                eventType: "apitoken.revoked",
                severity: Severity.Low,
                ct: ct
            );
        }
        catch (Exception ex)
        {
            _log.LogBestEffortFailure(ex);
        }

        try
        {
            await _notifications.PublishAsync(
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Kind = NotificationKind.SystemMessage,
                    Severity = Severity.Low,
                    Title = "API token revoked",
                    Body = $"API token '{tokenName}' revoked.",
                    RelatedType = "ApiToken",
                    RelatedId = id.ToString(),
                    CreatedAt = DateTime.UtcNow,
                },
                ct
            );
        }
        catch (Exception ex)
        {
            _log.LogBestEffortFailure(ex);
        }

        return NoContent();
    }

    private async Task<Guid> ResolveUserIdAsync()
    {
        string? rawId =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(rawId, out Guid parsed))
            return parsed;

        // Fallback for principals that don't carry NameIdentifier as a Guid (eg SingleUser
        // synthetic principal in some paths) — resolve via the username claim.
        ShieldUser? user = await _userManager.GetUserAsync(User);
        return user?.Id ?? Guid.Empty;
    }

    private static ApiTokenSummary Project(ApiToken token)
    {
        IReadOnlyList<string> scopes = string.IsNullOrEmpty(token.Scopes)
            ? []
            : token.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries);
        IReadOnlyList<int> sourceFilter = string.IsNullOrEmpty(token.SourceIdFilter)
            ? []
            : token
                .SourceIdFilter.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => int.TryParse(part, out int id) ? id : 0)
                .Where(id => id > 0)
                .ToArray();
        return new(
            token.Id,
            token.UserId,
            token.Name,
            token.Prefix,
            scopes,
            sourceFilter,
            token.CreatedAt,
            token.ExpiresAt,
            token.LastUsedAt,
            token.LastUsedIp,
            token.RevokedAt
        );
    }
}
