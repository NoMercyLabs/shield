using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shield.Api.Auth;
using Xunit;

namespace Shield.Api.Tests;

public sealed class DataProtectionTests
{
    private static byte[] MakeKey(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    [Fact]
    public void Round_trip_wrap_unwrap_returns_original_xml()
    {
        byte[] key = MakeKey("test-master-key-32-chars-long-abc");
        MasterKeyXmlEncryptor encryptor = new(key);

        XElement plaintext = new(
            "key",
            new XAttribute("id", "abc-123"),
            new XElement("descriptor", "payload-bytes-here")
        );

        Microsoft.AspNetCore.DataProtection.XmlEncryption.EncryptedXmlInfo info = encryptor.Encrypt(
            plaintext
        );
        info.EncryptedElement.Name.LocalName.Should().Be("encryptedKey");
        info.DecryptorType.Should().Be(typeof(MasterKeyXmlDecryptor));

        ServiceCollection services = new();
        services.AddSingleton(new MasterKeyDataProtectionExtensions.MasterKeyHolder(key));
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        using ServiceProvider provider = services.BuildServiceProvider();
        MasterKeyXmlDecryptor decryptor = new(provider);

        XElement roundTripped = decryptor.Decrypt(info.EncryptedElement);
        roundTripped
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(plaintext.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void Decrypt_falls_back_to_plaintext_passthrough_when_envelope_missing()
    {
        byte[] key = MakeKey("legacy-fallback-secret-also-32chrs");
        ServiceCollection services = new();
        services.AddSingleton(new MasterKeyDataProtectionExtensions.MasterKeyHolder(key));
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        using ServiceProvider provider = services.BuildServiceProvider();
        MasterKeyXmlDecryptor decryptor = new(provider);

        XElement legacyPlaintext = new("key", new XElement("descriptor", "old-style"));
        XElement result = decryptor.Decrypt(legacyPlaintext);

        result
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(legacyPlaintext.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void Decrypt_with_wrong_master_key_throws()
    {
        byte[] writeKey = MakeKey("write-key-must-also-be-32-chars!!");
        byte[] readKey = MakeKey("different-read-key-32-chars-long-x");

        MasterKeyXmlEncryptor encryptor = new(writeKey);
        XElement plaintext = new("key", new XElement("descriptor", "payload"));
        Microsoft.AspNetCore.DataProtection.XmlEncryption.EncryptedXmlInfo info = encryptor.Encrypt(
            plaintext
        );

        ServiceCollection services = new();
        services.AddSingleton(new MasterKeyDataProtectionExtensions.MasterKeyHolder(readKey));
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        using ServiceProvider provider = services.BuildServiceProvider();
        MasterKeyXmlDecryptor decryptor = new(provider);

        Action act = () => decryptor.Decrypt(info.EncryptedElement);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Encryptor_rejects_non_32_byte_key()
    {
        Action act = () => _ = new MasterKeyXmlEncryptor(new byte[16]);
        act.Should().Throw<ArgumentException>();
    }
}
