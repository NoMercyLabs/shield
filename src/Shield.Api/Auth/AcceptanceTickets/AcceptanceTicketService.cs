using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shield.Api.Auth.AcceptanceTickets;

// Signs the payload with HMAC-SHA256 over `body.signature` (base64url) using the existing
// JWT signing key. Five-minute TTL is enforced at validate time. Compact, self-contained,
// no DB round-trip — the ticket is the proof.
public sealed class AcceptanceTicketService : IAcceptanceTicketService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly byte[] _signingKey;

    public AcceptanceTicketService(IConfiguration configuration)
    {
        string raw =
            configuration["Shield:Auth:JwtSigningKey"]
            ?? configuration["Shield:Auth:Jwt:Secret"]
            ?? throw new InvalidOperationException(
                "Shield:Auth:JwtSigningKey is required for acceptance tickets."
            );
        _signingKey = Encoding.UTF8.GetBytes(raw);
    }

    public string Issue(AcceptanceTicketPayload payload)
    {
        string body = Encode(JsonSerializer.SerializeToUtf8Bytes(payload, s_jsonOptions));
        string sig = Encode(HmacSha256(_signingKey, Encoding.UTF8.GetBytes(body)));
        return $"{body}.{sig}";
    }

    public bool TryValidate(string ticket, out AcceptanceTicketPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(ticket))
            return false;
        string[] parts = ticket.Split('.', 2);
        if (parts.Length != 2)
            return false;

        byte[] expectedSig = HmacSha256(_signingKey, Encoding.UTF8.GetBytes(parts[0]));
        byte[] actualSig;
        try
        {
            actualSig = Decode(parts[1]);
        }
        catch
        {
            return false;
        }
        if (!CryptographicOperations.FixedTimeEquals(expectedSig, actualSig))
            return false;

        AcceptanceTicketPayload? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AcceptanceTicketPayload>(
                Decode(parts[0]),
                s_jsonOptions
            );
        }
        catch
        {
            return false;
        }
        if (parsed is null)
            return false;
        if (parsed.ExpiresAt <= DateTimeOffset.UtcNow)
            return false;

        payload = parsed;
        return true;
    }

    public static DateTimeOffset DefaultExpiry() => DateTimeOffset.UtcNow + DefaultTtl;

    private static byte[] HmacSha256(byte[] key, byte[] data) => HMACSHA256.HashData(key, data);

    private static string Encode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode(string data)
    {
        string padded = data.Replace('-', '+').Replace('_', '/');
        return (padded.Length % 4) switch
        {
            2 => Convert.FromBase64String(padded + "=="),
            3 => Convert.FromBase64String(padded + "="),
            _ => Convert.FromBase64String(padded),
        };
    }
}
