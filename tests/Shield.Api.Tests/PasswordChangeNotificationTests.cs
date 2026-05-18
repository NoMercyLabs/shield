using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

public sealed class PasswordChangeNotificationTests
{
    [Fact]
    public async Task ChangePasswordCreatesNotificationAndSecurityEvent()
    {
        using MultiUserFactory factory = new();
        HttpClient client = factory.CreateClient();

        // Register + login so we have an authenticated session.
        await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("pwchange-user", "OldPass1!")
        );
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("pwchange-user", "OldPass1!")
        );
        login.IsSuccessStatusCode.Should().BeTrue();

        // Change password.
        HttpResponseMessage change = await client.PostAsJsonAsync(
            "/api/auth/password",
            new ChangePasswordRequest("OldPass1!", "NewPass2!")
        );
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using IServiceScope scope = factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();

        // Exactly one password.changed security event.
        int securityEvents = await db.SecurityEvents.CountAsync(evt =>
            evt.EventType == "password.changed"
        );
        securityEvents.Should().Be(1);

        // Exactly one notification for the user.
        int notifications = await db.Notifications.CountAsync(notif =>
            notif.Title == "Password changed"
        );
        notifications.Should().Be(1);

        Notification? notif = await db.Notifications.FirstOrDefaultAsync(notif =>
            notif.Title == "Password changed"
        );
        notif!.Severity.Should().Be(Severity.High);
        notif.Body.Should().Contain("Your password was changed");
    }

    private sealed class MultiUserFactory : ShieldWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration(
                (_, config) =>
                {
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["Shield:SingleUser"] = "false" }
                    );
                }
            );
        }
    }
}
