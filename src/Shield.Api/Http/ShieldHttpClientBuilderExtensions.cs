using Shield.Core.Http;

namespace Shield.Api.Http;

public static class ShieldHttpClientBuilderExtensions
{
    // Bind the canonical UA on every named/typed client at registration so we never depend on
    // per-request header adds remembering to do it.
    public static IHttpClientBuilder AddShieldUserAgent(this IHttpClientBuilder builder) =>
        builder.ConfigureHttpClient(client =>
            client.DefaultRequestHeaders.UserAgent.ParseAdd(ShieldUserAgent.Header)
        );
}
