namespace Shield.Channels.Webhook;

public sealed record WebhookConfig(
    string Url,
    string? Method = null,
    Dictionary<string, string>? Headers = null,
    string? BodyTemplate = null
)
{
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Url))
            return false;
        if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri))
            return false;
        return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
    }
}
