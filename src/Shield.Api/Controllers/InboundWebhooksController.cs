using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Shield.Api.Services.Webhooks;

namespace Shield.Api.Controllers;

// Inbound delivery endpoint for endpoints registered via WebhookEndpointsController.
// Verifies the provider-specific signature scheme, persists a raw WebhookEnvelope row so
// future detectors can backfill from history, and returns 200. Projection / detection /
// gating land on top of the persisted envelopes in a follow-up.
[ApiController]
[Route("api/webhooks/in")]
[AllowAnonymous]
public sealed class InboundWebhooksController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly ShieldDbContext _db;
    private readonly IDataProtector _protector;
    private readonly IEnumerable<IInboundWebhookHandler> _handlers;
    private readonly ILogger<InboundWebhooksController> _logger;

    public InboundWebhooksController(
        ShieldDbContext db,
        IDataProtectionProvider protectionProvider,
        IEnumerable<IInboundWebhookHandler> handlers,
        ILogger<InboundWebhooksController> logger
    )
    {
        _db = db;
        _protector = protectionProvider.CreateProtector("shield.webhooks");
        _handlers = handlers;
        _logger = logger;
    }

    [HttpPost("{id:guid}")]
    public async Task<IActionResult> Deliver(Guid id, CancellationToken ct)
    {
        WebhookEndpoint? endpoint = await _db.WebhookEndpoints.FirstOrDefaultAsync(
            row => row.Id == id,
            ct
        );
        if (endpoint is null)
            return NotFound(new { error = "Unknown webhook endpoint." });

        Request.EnableBuffering();
        using MemoryStream buffer = new();
        await Request.Body.CopyToAsync(buffer, ct);
        byte[] payload = buffer.ToArray();

        string secret;
        bool secretOk;
        try
        {
            secret = _protector.Unprotect(endpoint.SecretEncrypted);
            secretOk = true;
        }
        catch
        {
            _logger.LogWarning(
                "Webhook secret for endpoint {Id} could not be decrypted (DataProtection key rotated?)",
                id
            );
            secret = string.Empty;
            secretOk = false;
        }

        (bool ok, string reason) = secretOk
            ? VerifySignature(endpoint.Provider, payload, secret)
            : (false, "secret-undecryptable");

        WebhookEnvelope envelope = new()
        {
            Id = Guid.NewGuid(),
            EndpointId = endpoint.Id,
            Provider = endpoint.Provider,
            EventType = ExtractEventType(endpoint.Provider),
            DeliveryId = ExtractDeliveryId(endpoint.Provider),
            HeadersJson = CaptureHeadersJson(endpoint.Provider),
            PayloadJson = Encoding.UTF8.GetString(payload),
            SignatureValid = ok,
            Reason = ok ? null : reason,
            ReceivedAt = DateTime.UtcNow,
        };
        _db.WebhookEnvelopes.Add(envelope);

        endpoint.LastDeliveryAt = envelope.ReceivedAt;
        endpoint.LastDeliveryStatus = ok ? envelope.EventType ?? "ok" : reason;
        await _db.SaveChangesAsync(ct);

        if (!ok)
            return StatusCode(StatusCodes.Status401Unauthorized, new { error = reason });

        // Fan-out to registered handlers. Best-effort — a thrown handler must not silence
        // the rest, and must not affect the 200 we owe the provider.
        InboundWebhookContext handlerContext = new(
            endpoint.Provider,
            endpoint.Id,
            envelope.EventType,
            payload
        );
        foreach (IInboundWebhookHandler handler in _handlers)
        {
            try
            {
                await handler.HandleAsync(handlerContext, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Inbound webhook handler {Handler} threw for endpoint {Id}",
                    handler.GetType().Name,
                    endpoint.Id
                );
            }
        }

        return Ok(new { received = true, eventType = envelope.EventType });
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

    private string? ExtractEventType(OAuthProvider provider) =>
        provider switch
        {
            OAuthProvider.Github => Request.Headers["X-GitHub-Event"].FirstOrDefault(),
            OAuthProvider.Gitea or OAuthProvider.Forgejo => Request
                .Headers["X-Gitea-Event"]
                .FirstOrDefault(),
            OAuthProvider.Gitlab => Request.Headers["X-Gitlab-Event"].FirstOrDefault(),
            _ => null,
        };

    private string? ExtractDeliveryId(OAuthProvider provider) =>
        provider switch
        {
            OAuthProvider.Github => Request.Headers["X-GitHub-Delivery"].FirstOrDefault(),
            OAuthProvider.Gitea or OAuthProvider.Forgejo => Request
                .Headers["X-Gitea-Delivery"]
                .FirstOrDefault(),
            OAuthProvider.Gitlab => Request.Headers["X-Gitlab-Event-UUID"].FirstOrDefault(),
            _ => null,
        };

    private string CaptureHeadersJson(OAuthProvider provider)
    {
        // Allow-list per provider — we deliberately don't capture every header (cookies,
        // auth, proxy-injected) to keep the envelope free of incidental sensitive data.
        string[] allow = provider switch
        {
            OAuthProvider.Github =>
            [
                "X-GitHub-Event",
                "X-GitHub-Delivery",
                "X-GitHub-Hook-ID",
                "X-GitHub-Hook-Installation-Target-Type",
                "X-GitHub-Hook-Installation-Target-ID",
                "User-Agent",
            ],
            OAuthProvider.Gitea or OAuthProvider.Forgejo =>
            [
                "X-Gitea-Event",
                "X-Gitea-Delivery",
                "X-Gitea-Event-Type",
                "User-Agent",
            ],
            OAuthProvider.Gitlab =>
            [
                "X-Gitlab-Event",
                "X-Gitlab-Event-UUID",
                "X-Gitlab-Instance",
                "User-Agent",
            ],
            _ => ["User-Agent"],
        };

        Dictionary<string, string> captured = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in allow)
        {
            string? value = Request.Headers[name].FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                captured[name] = value;
        }
        return JsonSerializer.Serialize(captured, JsonOpts);
    }

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
}
