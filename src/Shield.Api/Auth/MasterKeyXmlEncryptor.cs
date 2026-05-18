using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace Shield.Api.Auth;

// Wraps each IDataProtection keyring entry in an AES-GCM envelope keyed off the operator's
// master key. The plaintext XML is the unprotected key payload supplied by the framework; we
// emit it as <encryptedKey nonce="…" tag="…"><cipher>…</cipher></encryptedKey>. Decryption is
// performed by MasterKeyXmlDecryptor (matched by element name).
public sealed class MasterKeyXmlEncryptor : IXmlEncryptor
{
    public const string EncryptedElement = "encryptedKey";
    public const string NonceAttribute = "nonce";
    public const string TagAttribute = "tag";
    public const string CipherElement = "cipher";

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public MasterKeyXmlEncryptor(byte[] key)
    {
        if (key is null || key.Length != 32)
            throw new ArgumentException(
                "Master key must be 32 bytes (use SHA-256 of the secret).",
                nameof(key)
            );
        _key = key;
    }

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        byte[] plaintext = Encoding.UTF8.GetBytes(
            plaintextElement.ToString(SaveOptions.DisableFormatting)
        );
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipher = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using (AesGcm gcm = new(_key, TagSize))
        {
            gcm.Encrypt(nonce, plaintext, cipher, tag);
        }

        XElement envelope = new(
            EncryptedElement,
            new XAttribute(NonceAttribute, Convert.ToBase64String(nonce)),
            new XAttribute(TagAttribute, Convert.ToBase64String(tag)),
            new XElement(CipherElement, Convert.ToBase64String(cipher))
        );

        return new(envelope, typeof(MasterKeyXmlDecryptor));
    }
}
