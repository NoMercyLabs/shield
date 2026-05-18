using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Api.Tests;

public sealed class OnboardingTests
{
    [Fact]
    public async Task StatusReturnsNotCompletedOnFreshDb()
    {
        await using ShieldWebAppFactory factory = new();
        HttpClient client = factory.CreateClient();

        OnboardingStatusResponse? status = await client.GetFromJsonAsync<OnboardingStatusResponse>(
            "/api/onboarding/status"
        );

        status.Should().NotBeNull();
        status!.Completed.Should().BeFalse();
        status.SourceCount.Should().Be(0);
        status.ChannelCount.Should().Be(0);
        status.GithubConnected.Should().BeFalse();
        status.AnyOauthConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task StatusReturnsCompletedWhenAtLeastOneSourceAndChannelExist()
    {
        await using ShieldWebAppFactory factory = new();
        HttpClient client = factory.CreateClient();

        object sourceRequest = new
        {
            type = (int)SourceType.LocalFolder,
            name = "onboarding-source",
            configJson = new { path = "/tmp" },
            scanInterval = "01:00:00",
        };
        HttpResponseMessage createSource = await client.PostAsJsonAsync(
            "/api/sources",
            sourceRequest
        );
        createSource.StatusCode.Should().Be(HttpStatusCode.Created);

        object channelRequest = new
        {
            type = (int)ChannelType.Inbox,
            name = "onboarding-channel",
            configJson = "{}",
            minSeverity = (int)Severity.Low,
            enabled = true,
        };
        HttpResponseMessage createChannel = await client.PostAsJsonAsync(
            "/api/channels",
            channelRequest
        );
        createChannel.StatusCode.Should().Be(HttpStatusCode.Created);

        OnboardingStatusResponse? status = await client.GetFromJsonAsync<OnboardingStatusResponse>(
            "/api/onboarding/status"
        );

        status.Should().NotBeNull();
        status!.Completed.Should().BeTrue();
        status.SourceCount.Should().Be(1);
        status.ChannelCount.Should().Be(1);
    }

    [Fact]
    public async Task DismissMarksCompletedEvenWithoutSourcesOrChannels()
    {
        await using ShieldWebAppFactory factory = new();
        HttpClient client = factory.CreateClient();

        OnboardingStatusResponse? before = await client.GetFromJsonAsync<OnboardingStatusResponse>(
            "/api/onboarding/status"
        );
        before!.Completed.Should().BeFalse();

        HttpResponseMessage dismiss = await client.PostAsync(
            "/api/onboarding/dismiss",
            content: null
        );
        dismiss.StatusCode.Should().Be(HttpStatusCode.OK);
        OnboardingStatusResponse? afterDismiss =
            await dismiss.Content.ReadFromJsonAsync<OnboardingStatusResponse>();
        afterDismiss!.Completed.Should().BeTrue();

        OnboardingStatusResponse? rereadStatus =
            await client.GetFromJsonAsync<OnboardingStatusResponse>("/api/onboarding/status");
        rereadStatus!.Completed.Should().BeTrue();
        rereadStatus.SourceCount.Should().Be(0);
        rereadStatus.ChannelCount.Should().Be(0);
    }
}
