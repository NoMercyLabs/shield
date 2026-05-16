using System.Text.Json;
using System.Text.Json.Nodes;
using Shield.Core.Domain;

namespace Shield.Api.Contracts;

public sealed record CreateChannelRequest(
    ChannelType Type,
    string Name,
    string ConfigJson,
    Severity MinSeverity,
    bool Enabled = true
);

public sealed record UpdateChannelRequest(
    string Name,
    string ConfigJson,
    Severity MinSeverity,
    bool Enabled
);

// `ConfigJson` is kept for backwards compatibility (existing clients read it),
// but new UI uses `ParsedConfig` — the same payload as an object, with secret
// fields replaced by "****". Parsing is best-effort: malformed JSON yields null
// and the client can fall back to ConfigJson.
public sealed record ChannelResponse(
    Guid Id,
    ChannelType Type,
    string Name,
    string ConfigJson,
    JsonNode? ParsedConfig,
    Severity MinSeverity,
    bool Enabled
)
{
    public static ChannelResponse From(AlertChannel channel) =>
        new(
            channel.Id,
            channel.Type,
            channel.Name,
            channel.ConfigJsonEncrypted,
            BuildMaskedConfig(channel.Type, channel.ConfigJsonEncrypted),
            channel.MinSeverity,
            channel.Enabled
        );

    private static JsonNode? BuildMaskedConfig(ChannelType type, string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return null;
        try
        {
            JsonNode? node = JsonNode.Parse(rawJson);
            if (node is not JsonObject obj)
                return node;
            MaskSecrets(type, obj);
            return obj;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Per-type secret fields. Names are case-insensitive against the parsed
    // JSON. URL-bearing fields keep host + path tail visible so operators can
    // tell which webhook a channel points at without leaking the token.
    private static void MaskSecrets(ChannelType type, JsonObject obj)
    {
        switch (type)
        {
            case ChannelType.Discord:
                MaskUrl(obj, "webhookUrl");
                break;
            case ChannelType.Slack:
                MaskUrl(obj, "webhookUrl");
                break;
            case ChannelType.Ntfy:
                MaskScalar(obj, "authToken");
                break;
            case ChannelType.Smtp:
                MaskScalar(obj, "password");
                break;
            case ChannelType.Webhook:
                MaskUrl(obj, "url");
                MaskHeaderSecrets(obj);
                break;
            case ChannelType.Inbox:
                break;
        }
    }

    private static void MaskScalar(JsonObject obj, string key)
    {
        foreach (KeyValuePair<string, JsonNode?> kv in obj)
        {
            if (
                string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)
                && kv.Value is JsonValue
            )
            {
                obj[kv.Key] = "****";
                return;
            }
        }
    }

    private static void MaskUrl(JsonObject obj, string key)
    {
        foreach (KeyValuePair<string, JsonNode?> kv in obj)
        {
            if (!string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                continue;
            string? raw = kv.Value?.GetValue<string>();
            if (string.IsNullOrEmpty(raw))
                return;
            obj[kv.Key] = MaskUrlValue(raw);
            return;
        }
    }

    private static string MaskUrlValue(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed))
            return "****";
        string tail = parsed.AbsolutePath;
        if (tail.Length > 8)
            tail = string.Concat("…", tail.AsSpan(tail.Length - 6));
        return $"{parsed.Scheme}://{parsed.Host}/****{tail}";
    }

    private static void MaskHeaderSecrets(JsonObject obj)
    {
        if (obj["headers"] is not JsonObject headers)
            return;
        string[] secretKeys = ["authorization", "x-api-key", "api-key", "token"];
        foreach (string key in headers.Select(kv => kv.Key).ToArray())
        {
            if (
                secretKeys.Any(secret =>
                    string.Equals(secret, key, StringComparison.OrdinalIgnoreCase)
                )
            )
                headers[key] = "****";
        }
    }
}

public sealed record TestSendResponse(bool Success, int Delivered, string? Error);
