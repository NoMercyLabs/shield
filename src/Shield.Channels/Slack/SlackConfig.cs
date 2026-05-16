namespace Shield.Channels.Slack;

// Either WebhookUrl (legacy) or ChannelId (OAuth/chat.postMessage) is required.
public sealed record SlackConfig(string? WebhookUrl = null, string? ChannelId = null)
{
    public bool IsValid()
    {
        if (UsesOAuth)
            return true;
        if (string.IsNullOrWhiteSpace(WebhookUrl))
            return false;
        if (!Uri.TryCreate(WebhookUrl, UriKind.Absolute, out Uri? uri))
            return false;
        return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
    }

    public bool UsesOAuth => !string.IsNullOrWhiteSpace(ChannelId);
}
