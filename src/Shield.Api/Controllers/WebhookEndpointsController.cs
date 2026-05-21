using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;

namespace Shield.Api.Controllers;

// CRUD for registered inbound webhook endpoints. Separate from WebhooksController (which
// handles the existing /api/webhooks/github + /dependabot delivery routes) — keeps the
// management surface decoupled from delivery routes, and lets the new receiver land at
// /api/webhooks/in/{id} without colliding with mgmt verbs.
[ApiController]
[Route("api/webhook-endpoints")]
[Authorize(Policy = ShieldPolicies.Admin)]
[NoApiToken]
public sealed class WebhookEndpointsController : ControllerBase
{
    private readonly ShieldDbContext _db;
    private readonly IDataProtector _protector;
    private readonly IAppSettingsService _appSettings;

    public WebhookEndpointsController(
        ShieldDbContext db,
        IDataProtectionProvider protectionProvider,
        IAppSettingsService appSettings
    )
    {
        _db = db;
        _protector = protectionProvider.CreateProtector("shield.webhooks");
        _appSettings = appSettings;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WebhookEndpointResponse>>> List(
        CancellationToken ct
    )
    {
        List<WebhookEndpoint> rows = await _db
            .WebhookEndpoints.AsNoTracking()
            .OrderByDescending(endpoint => endpoint.CreatedAt)
            .ToListAsync(ct);

        string baseUrl = await ResolveBaseUrlAsync(ct);
        return Ok(rows.Select(row => Project(row, baseUrl)).ToList());
    }

    [HttpPost]
    [RequireOriginalIdentity]
    public async Task<ActionResult<CreateWebhookEndpointResponse>> Create(
        [FromBody] CreateWebhookEndpointRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Label))
            return BadRequest(new { error = "Label is required." });
        if (request.Label.Length > 200)
            return BadRequest(new { error = "Label must be 200 characters or fewer." });

        string secret = GenerateHexSecret(byteCount: 32);

        WebhookEndpoint row = new()
        {
            Id = Guid.NewGuid(),
            Provider = request.Provider,
            Label = request.Label.Trim(),
            SecretEncrypted = _protector.Protect(secret),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = ResolveUserId(),
        };
        _db.WebhookEndpoints.Add(row);
        await _db.SaveChangesAsync(ct);

        string baseUrl = await ResolveBaseUrlAsync(ct);
        return Ok(new CreateWebhookEndpointResponse(Project(row, baseUrl), secret));
    }

    [HttpDelete("{id:guid}")]
    [RequireOriginalIdentity]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        WebhookEndpoint? row = await _db.WebhookEndpoints.FirstOrDefaultAsync(
            endpoint => endpoint.Id == id,
            ct
        );
        if (row is null)
            return NotFound();
        _db.WebhookEndpoints.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<string> ResolveBaseUrlAsync(CancellationToken ct)
    {
        string? configured = await _appSettings.GetStringAsync(AppSettingKeys.PublicUrl, ct);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');
        return $"{Request.Scheme}://{Request.Host}".TrimEnd('/');
    }

    private static WebhookEndpointResponse Project(WebhookEndpoint row, string baseUrl) =>
        new(
            row.Id,
            row.Provider,
            row.Label,
            $"{baseUrl}/api/webhooks/in/{row.Id:N}",
            row.CreatedAt,
            row.LastDeliveryAt,
            row.LastDeliveryStatus
        );

    private static string GenerateHexSecret(int byteCount)
    {
        byte[] bytes = new byte[byteCount];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private Guid? ResolveUserId()
    {
        string? raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out Guid id) ? id : null;
    }
}
