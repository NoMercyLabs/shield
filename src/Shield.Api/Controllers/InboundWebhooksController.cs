using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Shield.Api.Controllers;

// Inbound delivery endpoint for endpoints registered via WebhookEndpointsController.
// Verifies the provider-specific signature scheme, records the delivery, returns 200.
// The receive-all-raw-then-project pipeline lands in a follow-up — for now this only
// proves the signature and lights up LastDeliveryAt so the admin sees deliveries arriving.
[ApiController]
[Route("api/webhooks/in")]
[AllowAnonymous]
public sealed class InboundWebhooksController : ControllerBase
{
    private readonly ShieldDbContext _db;
    private readonly IDataProtector _protector;
    private readonly ILogger<InboundWebhooksController> _logger;

    public InboundWebhooksController(
        ShieldDbContext db,
        IDataProtectionProvider protectionProvider,
        ILogger<InboundWebhooksController> logger
    )
    {
        _db = db;
        _protector = protectionProvider.CreateProtector("shield.webhooks");
        _logger = logger;
    }

    [HttpPost("{id:guid}")]
    public async Task<IActionResult> Deliver(Guid id, CancellationToken ct)
    {
        WebhookEndpoint? row = await _db.WebhookEndpoints.FirstOrDefaultAsync(
            endpoint => endpoint.Id == id,
            ct
        );
        if (row is null)
            return NotFound(new { error = "Unknown webhook endpoint." });

        string secret;
        try
        {
            secret = _protector.Unprotect(row.SecretEncrypted);
        }
        catch
        {
            _logger.LogWarning(
                "Webhook secret for endpoint {Id} could not be decrypted (DataProtection key rotated?)",
                id
            );
            return await RecordAsync(row, ok: false, "secret-undecryptable", ct);
        }

        Request.EnableBuffering();
        using MemoryStream buffer = new();
        await Request.Body.CopyToAsync(buffer, ct);
        byte[] payload = buffer.ToArray();

        (bool ok, string reason) = VerifySignature(row.Provider, payload, secret);
        if (!ok)
            return await RecordAsync(
                row,
                ok: false,
                reason,
                ct,
                status: StatusCodes.Status401Unauthorized
            );

        return await RecordAsync(row, ok: true, "delivered", ct);
    }

    private (bool Ok, string Reason) VerifySignature(
        OAuthProvider provider,
        byte[] payload,
        string secret
    ) =>
        provider switch
        {
            OAuthProvider.Github => VerifyHmacHex(
                Request.Headers["X-Hub-Signature-256"].FirstOrDefault(),
                payload,
                secret,
                prefix: "sha256="
            ),
            OAuthProvider.Gitea or OAuthProvider.Forgejo => VerifyHmacHex(
                Request.Headers["X-Gitea-Signature"].FirstOrDefault(),
                payload,
                secret,
                prefix: null
            ),
            OAuthProvider.Gitlab => VerifyConstantTimeToken(
                Request.Headers["X-Gitlab-Token"].FirstOrDefault(),
                secret
            ),
            _ => (false, "unsupported-provider"),
        };

    private static (bool Ok, string Reason) VerifyHmacHex(
        string? header,
        byte[] payload,
        string secret,
        string? prefix
    )
    {
        if (string.IsNullOrEmpty(header))
            return (false, "missing-signature");
        string hex = header;
        if (!string.IsNullOrEmpty(prefix))
        {
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return (false, "bad-prefix");
            hex = header[prefix.Length..];
        }
        if (hex.Length != 64)
            return (false, "bad-length");

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return (false, "not-hex");
        }

        using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(secret));
        byte[] expected = hmac.ComputeHash(payload);
        return CryptographicOperations.FixedTimeEquals(provided, expected)
            ? (true, "ok")
            : (false, "hmac-mismatch");
    }

    private static (bool Ok, string Reason) VerifyConstantTimeToken(string? header, string secret)
    {
        if (string.IsNullOrEmpty(header))
            return (false, "missing-token");
        byte[] providedBytes = Encoding.UTF8.GetBytes(header);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(secret);
        if (providedBytes.Length != expectedBytes.Length)
            return (false, "token-mismatch");
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes)
            ? (true, "ok")
            : (false, "token-mismatch");
    }

    private async Task<IActionResult> RecordAsync(
        WebhookEndpoint row,
        bool ok,
        string reason,
        CancellationToken ct,
        int status = StatusCodes.Status200OK
    )
    {
        row.LastDeliveryAt = DateTime.UtcNow;
        row.LastDeliveryStatus = ok ? "ok" : reason;
        await _db.SaveChangesAsync(ct);
        return ok ? Ok(new { received = true }) : StatusCode(status, new { error = reason });
    }
}
