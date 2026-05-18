namespace Shield.Channels.Discord;

public sealed record DiscordConfig(string WebhookUrl)
{
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(WebhookUrl))
            return false;
        if (!Uri.TryCreate(WebhookUrl, UriKind.Absolute, out Uri? uri))
            return false;
        return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
    }
}
