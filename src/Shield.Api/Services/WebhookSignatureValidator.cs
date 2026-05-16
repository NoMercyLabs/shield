using System.Security.Cryptography;
using System.Text;

namespace Shield.Api.Services;

// Constant-time HMAC-SHA256 validation of GitHub-style `X-Hub-Signature-256` headers.
// Header format is `sha256=<hex>`. Returns false for any malformed input rather than
// throwing so the controller can map a single 401 without exception filters.
public interface IWebhookSignatureValidator
{
    bool Verify(string? headerValue, byte[] payload, string? secret);
}

public sealed class WebhookSignatureValidator : IWebhookSignatureValidator
{
    private const string Prefix = "sha256=";

    public bool Verify(string? headerValue, byte[] payload, string? secret)
    {
        if (string.IsNullOrEmpty(secret))
            return false;
        if (
            string.IsNullOrEmpty(headerValue)
            || !headerValue.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
        )
            return false;

        string hex = headerValue[Prefix.Length..];
        if (hex.Length != 64)
            return false;

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return false;
        }

        using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(secret));
        byte[] expected = hmac.ComputeHash(payload);
        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }
}
