namespace Shield.Api.Contracts;

public sealed record OnboardingStatusResponse(
    bool Completed,
    int SourceCount,
    int ChannelCount,
    bool GithubConnected,
    bool AnyOauthConfigured
);
