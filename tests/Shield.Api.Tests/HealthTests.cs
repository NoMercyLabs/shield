using System.Net;
using FluentAssertions;
using Xunit;

namespace Shield.Api.Tests;

public sealed class HealthTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public HealthTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Healthz_returns_200_with_ok_status()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/healthz");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\"").And.Contain("\"ok\"");
    }
}
