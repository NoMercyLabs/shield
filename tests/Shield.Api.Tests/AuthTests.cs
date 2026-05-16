using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Shield.Api.Contracts;
using Xunit;

namespace Shield.Api.Tests;

public sealed class AuthTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public AuthTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Me_in_single_user_mode_returns_admin_principal()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        MeResponse? me = await response.Content.ReadFromJsonAsync<MeResponse>();
        me.Should().NotBeNull();
        me!.SingleUserMode.Should().BeTrue();
        me.Roles.Should().Contain("Admin");
    }
}
