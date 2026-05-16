namespace Shield.Channels.Ntfy;

public sealed record NtfyConfig(
    string Url,
    string? Title = null,
    int? Priority = null,
    string[]? Tags = null,
    string? AuthToken = null
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
