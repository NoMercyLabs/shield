using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Feeds.Osv;
using Shield.Feeds.Osv.Extensions;
using Xunit;

namespace Shield.Feeds.Osv.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOsvFeed_RegistersIFeedSync_AndOsvFeedSync()
    {
        ServiceCollection services = [];
        services.AddOsvFeed();

        ServiceProvider provider = services.BuildServiceProvider();

        IFeedSync feedSync = provider.GetRequiredService<IFeedSync>();
        feedSync.Should().BeOfType<OsvFeedSync>();
        feedSync.Feed.Should().Be(Feed.Osv);

        OsvFeedSync direct = provider.GetRequiredService<OsvFeedSync>();
        direct.Should().NotBeNull();
    }

    [Fact]
    public void AddOsvFeed_RegistersHttpClient_WithCorrectBaseAddress()
    {
        ServiceCollection services = [];
        services.AddOsvFeed();

        ServiceProvider provider = services.BuildServiceProvider();
        IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();
        HttpClient client = factory.CreateClient(OsvFeedSync.HttpClientName);

        client.BaseAddress.Should().Be(new Uri("https://api.osv.dev/"));
    }
}
