using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace Shield.Api.Tests;

public sealed class OgTests : IClassFixture<ShieldWebAppFactory>
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];

    private readonly ShieldWebAppFactory _factory;

    public OgTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Default_og_image_returns_png_with_cache_header()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/og/default.png");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Public.Should().BeTrue();

        byte[] body = await response.Content.ReadAsByteArrayAsync();
        body.Length.Should().BeGreaterThan(0);
        body.Take(4).Should().Equal(PngMagic);
    }

    [Fact]
    public async Task Instance_og_image_returns_png()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/og/instance.png");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        body.Take(4).Should().Equal(PngMagic);
    }

    [Fact]
    public async Task Icon_endpoint_returns_png_for_whitelisted_size()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/og/icon-192.png");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        body.Take(4).Should().Equal(PngMagic);
    }

    [Fact]
    public async Task Icon_endpoint_rejects_non_whitelisted_size()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/og/icon-1234.png");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Discordbot_user_agent_gets_enriched_meta_for_root()
    {
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(HttpMethod.Get, "/");
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (compatible; Discordbot/2.0; +https://discordapp.com)"
        );
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("<meta property=\"og:title\"");
        body.Should().Contain("Shield");
        body.Should().Contain("og:image");
        body.Should().Contain("/api/og/default.png");
    }

    [Fact]
    public async Task Discordbot_user_agent_gets_login_title_on_login_route()
    {
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(HttpMethod.Get, "/login");
        request.Headers.UserAgent.ParseAdd("Discordbot/2.0");
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Sign in to Shield");
    }

    [Fact]
    public async Task Regular_browser_user_agent_falls_through_to_unmodified_spa_shell()
    {
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(HttpMethod.Get, "/");
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130 Safari/537.36"
        );
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        // The static index.html ships with the baseline OG tags so even a plain browser
        // load contains them — the assertion here is that the crawler middleware did NOT
        // rewrite the body (route-specific copy like "Sign in to Shield" only appears for
        // bot UAs on /login). Cheapest signal: the original Shield title is intact and
        // no bot-only "Sign in to Shield" string leaks onto the root response.
        body.Should().Contain("Shield");
        body.Should().NotContain("Sign in to Shield");
    }
}
