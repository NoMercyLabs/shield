using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Shield.Api.Auth;

// Signed payload identifying an active "Admin viewing as X" override. DataProtection-wrapped
// so a client can't forge or extend the cookie; expiry is enforced by the IssuedAt claim
// rather than the cookie's own MaxAge — the cookie is short-lived but the issuer still
// rejects payloads it considers expired (defence in depth against a cookie that survives
// past the intended TTL via clock skew or browser persistence).
public interface IImpersonationCookieIssuer
{
    string Protect(ImpersonationPayload payload);
    ImpersonationPayload? Unprotect(string value);
}

public sealed record ImpersonationPayload(
    Guid AdminUserId,
    Guid ImpersonatedUserId,
    long IssuedAtUnix
)
{
    public DateTimeOffset IssuedAt => DateTimeOffset.FromUnixTimeSeconds(IssuedAtUnix);
}

public sealed class ImpersonationCookieIssuer : IImpersonationCookieIssuer
{
    // 1 hour MAX, per the spec. Refreshed on every successful /start, so an admin who keeps
    // poking will never get bounced; an idle override times out by itself.
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(1);

    private readonly IDataProtector _protector;

    public ImpersonationCookieIssuer(IDataProtectionProvider protection)
    {
        _protector = protection.CreateProtector("shield.impersonation");
    }

    public string Protect(ImpersonationPayload payload)
    {
        byte[] raw = JsonSerializer.SerializeToUtf8Bytes(payload);
        return _protector.Protect(Convert.ToBase64String(raw));
    }

    public ImpersonationPayload? Unprotect(string value)
    {
        try
        {
            string base64 = _protector.Unprotect(value);
            byte[] raw = Convert.FromBase64String(base64);
            ImpersonationPayload? payload = JsonSerializer.Deserialize<ImpersonationPayload>(raw);
            if (payload is null)
                return null;
            if (DateTimeOffset.UtcNow - payload.IssuedAt > MaxLifetime)
                return null;
            return payload;
        }
        catch
        {
            // Tampered / unparseable / from a previous keyring → treat as absent.
            return null;
        }
    }
}
