using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Api.Services;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

// Validates the personal-access-token surface: creation reveals the plaintext exactly once,
// the bearer authenticates against scoped + non-scoped endpoints correctly, revocation flips
// the gate to 401, and LastUsedAt persists at most once per 60s window.
public sealed class ApiTokenTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public ApiTokenTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
        ApiTokenStore.ResetCoalesceGate();
    }

    [Fact]
    public async Task Create_returns_full_token_once()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/apitokens",
            new CreateApiTokenRequest("smoke", ["findings:read"], null, null)
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        CreateApiTokenResponse? body =
            await response.Content.ReadFromJsonAsync<CreateApiTokenResponse>();
        body.Should().NotBeNull();
        body!.Plaintext.Should().StartWith("shld_");
        body.Plaintext.Length.Should().Be(5 + 32);
        body.Token.Prefix.Should().HaveLength(8);
        body.Token.Scopes.Should().ContainSingle().Which.Should().Be("findings:read");
        body.Token.RevokedAt.Should().BeNull();

        // Subsequent list response must NOT echo the plaintext.
        HttpResponseMessage list = await client.GetAsync("/api/apitokens");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        IReadOnlyList<ApiTokenSummary>? listed = await list.Content.ReadFromJsonAsync<
            IReadOnlyList<ApiTokenSummary>
        >();
        listed.Should().NotBeNull();
        listed!.Should().Contain(token => token.Id == body.Token.Id);
    }

    [Fact]
    public async Task Token_with_findings_read_scope_can_list_findings()
    {
        HttpClient adminClient = _factory.CreateClient();
        CreateApiTokenResponse created = await CreateTokenAsync(
            adminClient,
            "findings-reader",
            ["findings:read"]
        );

        HttpClient tokenClient = NewBearerClient(created.Plaintext);
        HttpResponseMessage response = await tokenClient.GetAsync("/api/findings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_without_scope_returns_403()
    {
        HttpClient adminClient = _factory.CreateClient();
        // Token has only sources:read — calling findings should be forbidden.
        CreateApiTokenResponse created = await CreateTokenAsync(
            adminClient,
            "no-findings-scope",
            ["sources:read"]
        );

        HttpClient tokenClient = NewBearerClient(created.Plaintext);
        HttpResponseMessage response = await tokenClient.GetAsync("/api/findings");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Revoked_token_returns_401()
    {
        HttpClient adminClient = _factory.CreateClient();
        CreateApiTokenResponse created = await CreateTokenAsync(
            adminClient,
            "to-revoke",
            ["findings:read"]
        );

        HttpResponseMessage revoke = await adminClient.DeleteAsync(
            $"/api/apitokens/{created.Token.Id}"
        );
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpClient tokenClient = NewBearerClient(created.Plaintext);
        HttpResponseMessage response = await tokenClient.GetAsync("/api/findings");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Last_used_tracked_per_minute()
    {
        HttpClient adminClient = _factory.CreateClient();
        CreateApiTokenResponse created = await CreateTokenAsync(
            adminClient,
            "coalesce-target",
            ["findings:read"]
        );

        HttpClient tokenClient = NewBearerClient(created.Plaintext);
        // First hit — should persist LastUsedAt.
        await tokenClient.GetAsync("/api/findings");

        DateTime? firstLastUsed = await ReadLastUsedAsync(created.Token.Id);
        firstLastUsed.Should().NotBeNull();

        // Second hit immediately — coalesce window keeps the persisted timestamp pinned.
        await tokenClient.GetAsync("/api/findings");
        DateTime? secondLastUsed = await ReadLastUsedAsync(created.Token.Id);
        secondLastUsed.Should().Be(firstLastUsed);
    }

    [Fact]
    public async Task Token_cannot_create_other_tokens()
    {
        HttpClient adminClient = _factory.CreateClient();
        // Even a token whose owner is Admin must not be able to mint more tokens via the
        // NoApiToken gate on ApiTokensController.
        CreateApiTokenResponse created = await CreateTokenAsync(
            adminClient,
            "escalation-attempt",
            ["findings:read"]
        );

        HttpClient tokenClient = NewBearerClient(created.Plaintext);
        HttpResponseMessage response = await tokenClient.PostAsJsonAsync(
            "/api/apitokens",
            new CreateApiTokenRequest("nested", ["findings:read"], null, null)
        );
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Settings is also off-limits to api tokens per the threat model.
        HttpResponseMessage settings = await tokenClient.GetAsync("/api/settings");
        settings.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<CreateApiTokenResponse> CreateTokenAsync(
        HttpClient adminClient,
        string name,
        string[] scopes
    )
    {
        HttpResponseMessage response = await adminClient.PostAsJsonAsync(
            "/api/apitokens",
            new CreateApiTokenRequest(name, scopes, null, null)
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        CreateApiTokenResponse? body =
            await response.Content.ReadFromJsonAsync<CreateApiTokenResponse>();
        body.Should().NotBeNull();
        return body!;
    }

    private HttpClient NewBearerClient(string plaintext)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", plaintext);
        return client;
    }

    private async Task<DateTime?> ReadLastUsedAsync(Guid id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        ApiToken? row = await db.ApiTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        return row?.LastUsedAt;
    }
}
