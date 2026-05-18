using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Logging;

namespace Shield.Api.Auth;

// Reverses MasterKeyXmlEncryptor's envelope. If the element isn't a wrapped envelope (legacy
// plaintext key minted before master-key wrapping was enabled), pass it through unchanged so
// existing keyrings keep decoding — log a warning so the operator knows to rotate.
public sealed class MasterKeyXmlDecryptor : IXmlDecryptor
{
    private readonly byte[] _key;
    private readonly ILogger<MasterKeyXmlDecryptor>? _logger;

    public MasterKeyXmlDecryptor(IServiceProvider services)
    {
        _key = MasterKeyDataProtectionExtensions.ResolveMasterKey(services);
        _logger = (ILogger<MasterKeyXmlDecryptor>?)
            services.GetService(typeof(ILogger<MasterKeyXmlDecryptor>));
    }

    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        if (encryptedElement.Name.LocalName != MasterKeyXmlEncryptor.EncryptedElement)
        {
            _logger?.LogWarning(
                "DataProtection keyring entry is not master-key wrapped (element '{Element}'). "
                    + "Falling back to plaintext passthrough — rotate the keyring once safe.",
                encryptedElement.Name.LocalName
            );
            return new(encryptedElement);
        }

        string? nonceB64 = encryptedElement.Attribute(MasterKeyXmlEncryptor.NonceAttribute)?.Value;
        string? tagB64 = encryptedElement.Attribute(MasterKeyXmlEncryptor.TagAttribute)?.Value;
        string? cipherB64 = encryptedElement.Element(MasterKeyXmlEncryptor.CipherElement)?.Value;
        if (nonceB64 is null || tagB64 is null || cipherB64 is null)
            throw new CryptographicException("Master-key envelope missing nonce/tag/cipher.");

        byte[] nonce = Convert.FromBase64String(nonceB64);
        byte[] tag = Convert.FromBase64String(tagB64);
        byte[] cipher = Convert.FromBase64String(cipherB64);
        byte[] plaintext = new byte[cipher.Length];

        using (AesGcm gcm = new(_key, tag.Length))
        {
            gcm.Decrypt(nonce, cipher, tag, plaintext);
        }

        string xml = Encoding.UTF8.GetString(plaintext);
        return XElement.Parse(xml, LoadOptions.PreserveWhitespace);
    }
}
