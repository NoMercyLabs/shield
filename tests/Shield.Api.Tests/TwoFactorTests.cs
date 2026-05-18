using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Api.Services;
using Shield.Data.Identity;
using Xunit;

namespace Shield.Api.Tests;

public sealed class TwoFactorTests
{
    [Fact]
    public async Task Enrollment_returns_provisioning_uri_and_recovery_codes()
    {
        using MultiUserFactory factory = new();
        HttpClient client = factory.CreateClient();
        await RegisterAndLoginAsync(client, "tfa-enroll", "Correct1!");

        HttpResponseMessage enroll = await client.PostAsync("/api/auth/2fa/enroll", content: null);
        enroll.StatusCode.Should().Be(HttpStatusCode.OK);

        TwoFactorEnrollFullResponse? body =
            await enroll.Content.ReadFromJsonAsync<TwoFactorEnrollFullResponse>();
        body.Should().NotBeNull();
        body!.SharedKey.Should().NotBeNullOrWhiteSpace();
        body.AuthenticatorUri.Should().StartWith("otpauth://totp/Shield:");
        body.AuthenticatorUri.Should().Contain("secret=");
        body.RecoveryCodes.Should().HaveCount(8);
        body.RecoveryCodes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Verify_marks_user_2fa_enabled()
    {
        using MultiUserFactory factory = new();
        HttpClient client = factory.CreateClient();
        await RegisterAndLoginAsync(client, "tfa-verify", "Correct1!");

        // Walk the real flow: enroll via HTTP (server resets the auth key + persists), then
        // re-login so the cookie holds the post-reset SecurityStamp, then compute the TOTP
        // from the stored key via the same UserManager pipeline the server uses.
        HttpResponseMessage enroll = await client.PostAsync("/api/auth/2fa/enroll", content: null);
        enroll.IsSuccessStatusCode.Should().BeTrue();

        HttpResponseMessage relogin = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("tfa-verify", "Correct1!")
        );
        relogin.IsSuccessStatusCode.Should().BeTrue();

        using IServiceScope scope = factory.Services.CreateScope();
        UserManager<ShieldUser> userManager = scope.ServiceProvider.GetRequiredService<
            UserManager<ShieldUser>
        >();
        ShieldUser user = (await userManager.FindByNameAsync("tfa-verify"))!;
        string sharedKey = (await userManager.GetAuthenticatorKeyAsync(user))!;
        string code = ComputeTotp(sharedKey);

        HttpResponseMessage verify = await client.PostAsJsonAsync(
            "/api/auth/2fa/verify",
            new TwoFactorVerifyRequest(code)
        );
        verify.StatusCode.Should().Be(HttpStatusCode.OK);

        // Fresh scope — the test's UserManager cached the pre-verify user (TwoFactorEnabled=false)
        // and FindByNameAsync returns the tracked instance instead of re-reading from DB.
        using IServiceScope freshScope = factory.Services.CreateScope();
        UserManager<ShieldUser> freshManager = freshScope.ServiceProvider.GetRequiredService<
            UserManager<ShieldUser>
        >();
        ShieldUser refreshed = (await freshManager.FindByNameAsync("tfa-verify"))!;
        refreshed.TwoFactorEnabled.Should().BeTrue();
    }

    // Mirrors Identity's Rfc6238AuthenticationService.ComputeTotp — RFC 6238 6-digit code with
    // 30s step, big-endian counter, HMAC-SHA1 over the base32-decoded shared key.
    private static string ComputeTotp(string base32Secret)
    {
        byte[] key = Base32Decode(base32Secret);
        long step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        byte[] counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counter);
        using HMACSHA1 hmac = new(key);
        byte[] hash = hmac.ComputeHash(counter);
        int offset = hash[^1] & 0x0F;
        int binary =
            ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);
        int otp = binary % 1_000_000;
        return otp.ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        string clean = input.TrimEnd('=').ToUpperInvariant();
        int byteCount = clean.Length * 5 / 8;
        byte[] result = new byte[byteCount];
        int buffer = 0;
        int bitsLeft = 0;
        int index = 0;
        foreach (char character in clean)
        {
            int value = alphabet.IndexOf(character);
            if (value < 0)
                continue;
            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                result[index++] = (byte)((buffer >> bitsLeft) & 0xFF);
            }
        }
        return result;
    }

    [Fact]
    public async Task Require2Fa_setting_blocks_non_2fa_user_from_api()
    {
        using MultiUserFactory factory = new();
        HttpClient client = factory.CreateClient();
        await RegisterAndLoginAsync(client, "tfa-blocked", "Correct1!");

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ITwoFactorEnforcement enforcement =
                scope.ServiceProvider.GetRequiredService<ITwoFactorEnforcement>();
            await enforcement.SetRequiredAsync(true, updatedBy: null);
        }

        HttpResponseMessage sources = await client.GetAsync("/api/sources");
        sources.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        string body = await sources.Content.ReadAsStringAsync();
        body.Should().Contain("two_factor_required");

        // The enrollment endpoint is on the allow-list so the user can self-rescue.
        HttpResponseMessage enroll = await client.PostAsync("/api/auth/2fa/enroll", content: null);
        enroll.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task RegisterAndLoginAsync(
        HttpClient client,
        string username,
        string password
    )
    {
        HttpResponseMessage register = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(username, password)
        );
        register.IsSuccessStatusCode.Should().BeTrue();
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(username, password)
        );
        login.IsSuccessStatusCode.Should().BeTrue();
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
