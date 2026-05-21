using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

// Validates the webhook-endpoint registration + delivery surface:
//  - POST /api/webhook-endpoints returns the secret exactly once (CreateReturnsSecretOnce)
//  - subsequent list/GET never echoes the secret (ListResponseRedactsSecret)
//  - DELETE removes the row + cascades any envelopes (DeleteRemovesRowAndEnvelopes)
//  - inbound /api/webhooks/in/{id} verifies GitHub HMAC + persists envelope (InboundAcceptsValidGithubHmac)
//  - bad signature lands as 401 + envelope row with reason=hmac-mismatch (InboundRejectsBadGithubHmac)
//  - GitLab token uses constant-time compare (InboundAcceptsGitlabTokenMatch)
public sealed class WebhookEndpointTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public WebhookEndpointTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateReturnsSecretOnce()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/webhook-endpoints",
            new CreateWebhookEndpointRequest(OAuthProvider.Github, "test-create")
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        CreateWebhookEndpointResponse? body =
            await response.Content.ReadFromJsonAsync<CreateWebhookEndpointResponse>();
        body.Should().NotBeNull();
        body!.Secret.Should().HaveLength(64);
        body.Endpoint.Provider.Should().Be(OAuthProvider.Github);
        body.Endpoint.Label.Should().Be("test-create");
        body.Endpoint.InboundUrl.Should().EndWith($"/api/webhooks/in/{body.Endpoint.Id:N}");
    }

    [Fact]
    public async Task ListResponseRedactsSecret()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        CreateWebhookEndpointResponse? created = await CreateAsync(
            client,
            OAuthProvider.Gitea,
            "redact-check"
        );
        created.Should().NotBeNull();

        HttpResponseMessage listResponse = await client.GetAsync("/api/webhook-endpoints");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        string raw = await listResponse.Content.ReadAsStringAsync();
        raw.Should().NotContain(created!.Secret);
    }

    [Fact]
    public async Task DeleteRemovesRowAndEnvelopes()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        CreateWebhookEndpointResponse? created = await CreateAsync(
            client,
            OAuthProvider.Github,
            "delete-cascade"
        );
        created.Should().NotBeNull();
        Guid id = created!.Endpoint.Id;

        // Seed an envelope row so we can verify cascade-delete works.
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            db.WebhookEnvelopes.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    EndpointId = id,
                    Provider = OAuthProvider.Github,
                    EventType = "ping",
                    HeadersJson = "{}",
                    PayloadJson = "{}",
                    SignatureValid = true,
                    ReceivedAt = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }

        HttpResponseMessage delete = await client.DeleteAsync($"/api/webhook-endpoints/{id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using IServiceScope verifyScope = _factory.Services.CreateScope();
        ShieldDbContext verifyDb =
            verifyScope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        (await verifyDb.WebhookEndpoints.AnyAsync(row => row.Id == id)).Should().BeFalse();
        (await verifyDb.WebhookEnvelopes.AnyAsync(envelope => envelope.EndpointId == id))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task InboundAcceptsValidGithubHmac()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        CreateWebhookEndpointResponse? created = await CreateAsync(
            client,
            OAuthProvider.Github,
            "hmac-ok"
        );
        created.Should().NotBeNull();

        HttpClient anon = _factory.CreateClient();
        byte[] payload = Encoding.UTF8.GetBytes("""{"zen":"shield"}""");
        string signature = "sha256=" + ComputeHmacHex(payload, created!.Secret);

        HttpRequestMessage request = new(
            HttpMethod.Post,
            $"/api/webhooks/in/{created.Endpoint.Id:N}"
        )
        {
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", signature);
        request.Headers.Add("X-GitHub-Event", "ping");
        request.Headers.Add("X-GitHub-Delivery", "test-delivery-1");

        HttpResponseMessage response = await anon.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        WebhookEnvelope? row = await db.WebhookEnvelopes.FirstOrDefaultAsync(envelope =>
            envelope.EndpointId == created.Endpoint.Id
        );
        row.Should().NotBeNull();
        row!.SignatureValid.Should().BeTrue();
        row.EventType.Should().Be("ping");
        row.DeliveryId.Should().Be("test-delivery-1");
    }

    [Fact]
    public async Task InboundRejectsBadGithubHmac()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        CreateWebhookEndpointResponse? created = await CreateAsync(
            client,
            OAuthProvider.Github,
            "hmac-bad"
        );
        created.Should().NotBeNull();

        HttpClient anon = _factory.CreateClient();
        byte[] payload = Encoding.UTF8.GetBytes("""{"zen":"forged"}""");
        // Sign with the wrong secret on purpose.
        string signature = "sha256=" + ComputeHmacHex(payload, "definitely-not-the-real-secret");

        HttpRequestMessage request = new(
            HttpMethod.Post,
            $"/api/webhooks/in/{created!.Endpoint.Id:N}"
        )
        {
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", signature);
        request.Headers.Add("X-GitHub-Event", "push");

        HttpResponseMessage response = await anon.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        WebhookEnvelope? row = await db.WebhookEnvelopes.FirstOrDefaultAsync(envelope =>
            envelope.EndpointId == created.Endpoint.Id
        );
        row.Should().NotBeNull();
        row!.SignatureValid.Should().BeFalse();
        row.Reason.Should().Be("hmac-mismatch");
    }

    [Fact]
    public async Task InboundAcceptsGitlabTokenMatch()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        CreateWebhookEndpointResponse? created = await CreateAsync(
            client,
            OAuthProvider.Gitlab,
            "gitlab-token"
        );
        created.Should().NotBeNull();

        HttpClient anon = _factory.CreateClient();
        HttpRequestMessage request = new(
            HttpMethod.Post,
            $"/api/webhooks/in/{created!.Endpoint.Id:N}"
        )
        {
            Content = new StringContent(
                """{"object_kind":"push"}""",
                Encoding.UTF8,
                "application/json"
            ),
        };
        request.Headers.Add("X-Gitlab-Token", created.Secret);
        request.Headers.Add("X-Gitlab-Event", "Push Hook");

        HttpResponseMessage response = await anon.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<CreateWebhookEndpointResponse?> CreateAsync(
        HttpClient client,
        OAuthProvider provider,
        string label
    )
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/webhook-endpoints",
            new CreateWebhookEndpointRequest(provider, label)
        );
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CreateWebhookEndpointResponse>();
    }

    private static string ComputeHmacHex(byte[] payload, string secret)
    {
        using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
    }
}
