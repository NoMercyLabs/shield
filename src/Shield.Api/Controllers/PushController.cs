using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Shield.Api.Services;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/push")]
public sealed class PushController : ControllerBase
{
    private readonly ShieldDbContext _db;
    private readonly IWebPushSender _push;
    private readonly UserManager<ShieldUser> _userManager;

    public PushController(
        ShieldDbContext db,
        IWebPushSender push,
        UserManager<ShieldUser> userManager
    )
    {
        _db = db;
        _push = push;
        _userManager = userManager;
    }

    // VAPID public key is safe to expose anonymously — it's the verification key the
    // browser uses to bind a subscription to this server. Hiding it would just push the
    // SPA into a round-trip we can avoid.
    [HttpGet("vapid-public-key")]
    [AllowAnonymous]
    public async Task<ActionResult<PushVapidKeyResponse>> GetVapidPublicKey(CancellationToken ct)
    {
        string publicKey = await _push.EnsureVapidPublicKeyAsync(ct);
        return Ok(new PushVapidKeyResponse(publicKey));
    }

    [HttpPost("subscribe")]
    [Authorize]
    [NoApiToken]
    public async Task<IActionResult> Subscribe(
        [FromBody] PushSubscribeRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint))
            return BadRequest(new { error = "Endpoint is required." });
        if (
            request.Keys is null
            || string.IsNullOrWhiteSpace(request.Keys.P256dh)
            || string.IsNullOrWhiteSpace(request.Keys.Auth)
        )
            return BadRequest(new { error = "Subscription keys are required." });

        Guid userId = await ResolveUserIdAsync();
        if (userId == Guid.Empty)
            return Unauthorized();

        // Endpoint is globally unique. If another user previously subscribed on this device
        // (shared browser profile) we transfer ownership instead of failing — the W3C grant
        // is per-origin, not per-account, so the latest authenticated user owns the row.
        PushSubscription? existing = await _db.PushSubscriptions.FirstOrDefaultAsync(
            row => row.Endpoint == request.Endpoint,
            ct
        );

        string? userAgent =
            request.UserAgent
            ?? (
                HttpContext.Request.Headers.UserAgent.ToString() is { Length: > 0 } headerUa
                    ? headerUa
                    : null
            );

        if (existing is null)
        {
            _db.PushSubscriptions.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Endpoint = request.Endpoint,
                    P256dh = request.Keys.P256dh,
                    Auth = request.Keys.Auth,
                    UserAgent = Truncate(userAgent, 500),
                    CreatedAt = DateTime.UtcNow,
                }
            );
        }
        else
        {
            existing.UserId = userId;
            existing.P256dh = request.Keys.P256dh;
            existing.Auth = request.Keys.Auth;
            existing.UserAgent = Truncate(userAgent, 500);
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("unsubscribe")]
    [Authorize]
    [NoApiToken]
    public async Task<IActionResult> Unsubscribe(
        [FromBody] PushUnsubscribeRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint))
            return BadRequest(new { error = "Endpoint is required." });
        Guid userId = await ResolveUserIdAsync();
        if (userId == Guid.Empty)
            return Unauthorized();

        // Scoped to the caller — never let one user delete another user's subscription via
        // a leaked endpoint URL.
        await _db
            .PushSubscriptions.Where(row =>
                row.Endpoint == request.Endpoint && row.UserId == userId
            )
            .ExecuteDeleteAsync(ct);
        return NoContent();
    }

    // Id-based delete for the Settings panel — the list endpoint redacts the full endpoint
    // URL, so the UI needs a stable identifier to reference rows for removal.
    [HttpDelete("subscriptions/{id:guid}")]
    [Authorize]
    [NoApiToken]
    public async Task<IActionResult> DeleteById(Guid id, CancellationToken ct)
    {
        Guid userId = await ResolveUserIdAsync();
        if (userId == Guid.Empty)
            return Unauthorized();

        int removed = await _db
            .PushSubscriptions.Where(row => row.Id == id && row.UserId == userId)
            .ExecuteDeleteAsync(ct);
        return removed == 0 ? NotFound() : NoContent();
    }

    [HttpGet("subscriptions")]
    [Authorize]
    [NoApiToken]
    public async Task<ActionResult<PushSubscriptionListResponse>> List(CancellationToken ct)
    {
        Guid userId = await ResolveUserIdAsync();
        if (userId == Guid.Empty)
            return Unauthorized();

        List<PushSubscription> rows = await _db
            .PushSubscriptions.Where(row => row.UserId == userId)
            .OrderByDescending(row => row.CreatedAt)
            .ToListAsync(ct);

        IReadOnlyList<PushSubscriptionInfo> infos = rows.Select(row => new PushSubscriptionInfo(
                Id: row.Id,
                // Endpoint is technically secret-ish (anyone with the value can send a push
                // attempt) — we surface only the host portion so the Settings UI can label
                // rows without leaking the full token.
                Endpoint: SummarizeEndpoint(row.Endpoint),
                UserAgent: row.UserAgent,
                CreatedAt: row.CreatedAt,
                LastDeliveredAt: row.LastDeliveredAt,
                // UA-string matching is unreliable across browser updates.
                // IsCurrentDevice is always false; the SPA computes it locally via
                // pushManager.getSubscription() + EndpointHash comparison.
                IsCurrentDevice: false,
                EndpointHash: HashEndpoint(row.Endpoint)
            ))
            .ToList();

        return Ok(new PushSubscriptionListResponse(infos));
    }

    [HttpPost("test")]
    [Authorize]
    [NoApiToken]
    public async Task<ActionResult<PushTestResponse>> Test(CancellationToken ct)
    {
        Guid userId = await ResolveUserIdAsync();
        if (userId == Guid.Empty)
            return Unauthorized();

        int count = await _db.PushSubscriptions.CountAsync(row => row.UserId == userId, ct);
        if (count == 0)
            return Ok(new PushTestResponse(0));

        await _push.DispatchAsync(
            new(
                Title: "Shield test notification",
                Body: "If you can see this, push delivery to this device is working.",
                Severity: Severity.Low.ToString(),
                Url: "/notifications",
                Tag: $"shield-test-{Guid.NewGuid():N}"
            ),
            userId,
            ct
        );
        return Ok(new PushTestResponse(count));
    }

    private static string SummarizeEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return string.Empty;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri))
            return endpoint.Length > 64 ? endpoint[..64] + "…" : endpoint;
        return uri.Host;
    }

    // SHA-256 of the full endpoint URL, first 16 hex chars. Long enough to be collision-free
    // in practice; short enough to be opaque. The SPA hashes its local subscription.endpoint
    // the same way to determine which server row matches the current browser subscription.
    private static string HashEndpoint(string endpoint)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(endpoint));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];

    private async Task<Guid> ResolveUserIdAsync()
    {
        string? rawId =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(rawId, out Guid parsed))
            return parsed;
        ShieldUser? user = await _userManager.GetUserAsync(User);
        return user?.Id ?? Guid.Empty;
    }
}
