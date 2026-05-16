using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Shield.Api.Auth;

// Registers the AES-GCM master-key XML encryptor as the default IXmlEncryptor on the data
// protection keyring. Equivalent in spirit to ProtectKeysWithDpapi / ProtectKeysWithCertificate
// but rooted in a 32-byte key supplied by the operator via env var — no platform dependency,
// no PFX file, no Azure KeyVault. The key bytes are pinned in a singleton so both the encryptor
// (used at key creation) and decryptor (resolved per-key by KeyManager) see the same value.
public static class MasterKeyDataProtectionExtensions
{
    public sealed class MasterKeyHolder
    {
        public byte[] Key { get; }

        public MasterKeyHolder(byte[] key)
        {
            Key = key;
        }
    }

    public static IDataProtectionBuilder ProtectKeysWithMasterKey(
        this IDataProtectionBuilder builder,
        byte[] key
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (key is null || key.Length != 32)
            throw new ArgumentException("Master key must be 32 bytes.", nameof(key));

        builder.Services.AddSingleton(new MasterKeyHolder(key));

        // Configure KeyManagementOptions so newly created keys get wrapped on write.
        builder.Services.Configure<KeyManagementOptions>(options =>
        {
            options.XmlEncryptor = new MasterKeyXmlEncryptor(key);
        });

        return builder;
    }

    public static byte[] ResolveMasterKey(IServiceProvider services)
    {
        MasterKeyHolder? holder = (MasterKeyHolder?)services.GetService(typeof(MasterKeyHolder));
        if (holder is null)
            throw new InvalidOperationException(
                "Master key not registered — call ProtectKeysWithMasterKey() before resolving MasterKeyXmlDecryptor."
            );
        return holder.Key;
    }
}
