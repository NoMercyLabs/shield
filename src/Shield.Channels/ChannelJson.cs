using System.Text.Json;

namespace Shield.Channels;

// Shared JsonSerializerOptions for every channel's ConfigJsonEncrypted deserialisation.
// Channel config payloads are written by the Settings UI which uses camelCase property
// names; tolerant case matching means manually-edited configs in any case still parse.
// Cached because the JsonSerializerOptions type-cache builds on first use and rebuilding
// it per call is expensive (CA1869).
internal static class ChannelJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
