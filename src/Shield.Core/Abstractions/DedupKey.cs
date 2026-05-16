using System.Security.Cryptography;
using System.Text;
using Shield.Core.Domain;

namespace Shield.Core.Abstractions;

public static class DedupKey
{
    public static string Compute(int sourceId, Ecosystem eco, string pkg, string advisoryExternalId)
    {
        ArgumentNullException.ThrowIfNull(pkg);
        ArgumentNullException.ThrowIfNull(advisoryExternalId);

        string payload = $"{sourceId}|{(int)eco}|{pkg}|{advisoryExternalId}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
