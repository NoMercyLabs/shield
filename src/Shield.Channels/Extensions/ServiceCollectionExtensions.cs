using Microsoft.Extensions.DependencyInjection;
using Shield.Channels.Discord;
using Shield.Channels.Inbox;
using Shield.Core.Abstractions;

namespace Shield.Channels.Extensions;

public static class ServiceCollectionExtensions
{
    // Discord webhook UA is set globally via ConfigureHttpClientDefaults in Shield.Api Program.cs
    // so no per-client UA wiring is needed here.
    public static IServiceCollection AddShieldChannels(this IServiceCollection services)
    {
        services.AddHttpClient(DiscordWebhookChannel.HttpClientName);
        services.AddScoped<IAlertChannel, DiscordWebhookChannel>();
        services.AddScoped<IAlertChannel, InboxChannel>();
        return services;
    }
}
