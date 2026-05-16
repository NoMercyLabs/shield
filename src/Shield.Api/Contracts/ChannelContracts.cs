using Shield.Core.Domain;

namespace Shield.Api.Contracts;

public sealed record CreateChannelRequest(
    ChannelType Type,
    string Name,
    string ConfigJson,
    Severity MinSeverity,
    bool Enabled = true
);

public sealed record UpdateChannelRequest(
    string Name,
    string ConfigJson,
    Severity MinSeverity,
    bool Enabled
);

public sealed record ChannelResponse(
    Guid Id,
    ChannelType Type,
    string Name,
    string ConfigJson,
    Severity MinSeverity,
    bool Enabled
)
{
    public static ChannelResponse From(AlertChannel channel) =>
        new(
            channel.Id,
            channel.Type,
            channel.Name,
            channel.ConfigJsonEncrypted,
            channel.MinSeverity,
            channel.Enabled
        );
}

public sealed record TestSendResponse(bool Success, int Delivered, string? Error);
