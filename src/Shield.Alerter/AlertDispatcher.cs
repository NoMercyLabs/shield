using Microsoft.Extensions.Logging;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Alerter;

public sealed class AlertDispatcher
{
    private const int DigestThreshold = 5;

    private readonly Dictionary<ChannelType, IAlertChannel> _channels;
    private readonly ILogger<AlertDispatcher> _log;

    public AlertDispatcher(IEnumerable<IAlertChannel> channels, ILogger<AlertDispatcher> log)
    {
        _channels = channels.ToDictionary(channel => channel.ChannelType);
        _log = log;
    }

    public async Task<IReadOnlyList<AlertEvent>> DispatchAsync(
        IReadOnlyList<Finding> pendingFindings,
        IReadOnlyList<AlertChannel> configuredChannels,
        CancellationToken ct
    )
    {
        List<AlertEvent> events = new();

        foreach (AlertChannel channel in configuredChannels)
        {
            if (!channel.Enabled) continue;

            List<Finding> matched = pendingFindings
                .Where(finding => finding.Severity >= channel.MinSeverity)
                .ToList();

            if (matched.Count == 0) continue;

            if (!_channels.TryGetValue(channel.Type, out IAlertChannel? impl))
            {
                _log.LogWarning(
                    "No IAlertChannel registered for {ChannelType} (channel {ChannelId})",
                    channel.Type,
                    channel.Id
                );
                events.AddRange(BuildEvents(matched, channel.Id, AlertStatus.Failed,
                    $"No channel implementation for {channel.Type}"));
                continue;
            }

            if (matched.Count >= DigestThreshold)
            {
                AlertResult result = await SafeSendAsync(impl, channel, matched, ct);
                AlertStatus status = result.Success ? AlertStatus.Sent : AlertStatus.Failed;
                events.AddRange(BuildEvents(matched, channel.Id, status, result.Error));
                continue;
            }

            foreach (Finding finding in matched)
            {
                AlertResult result = await SafeSendAsync(impl, channel, new[] { finding }, ct);
                AlertStatus status = result.Success ? AlertStatus.Sent : AlertStatus.Failed;
                events.Add(new AlertEvent
                {
                    Id = Guid.NewGuid(),
                    FindingId = finding.Id,
                    ChannelId = channel.Id,
                    SentAt = DateTime.UtcNow,
                    Status = status,
                    Error = result.Error,
                });
            }
        }

        return events;
    }

    private async Task<AlertResult> SafeSendAsync(
        IAlertChannel impl,
        AlertChannel channel,
        IReadOnlyList<Finding> findings,
        CancellationToken ct
    )
    {
        try
        {
            return await impl.SendAsync(channel, findings, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(
                ex,
                "Channel {ChannelType} ({ChannelId}) threw while sending {Count} finding(s)",
                channel.Type,
                channel.Id,
                findings.Count
            );
            return AlertResult.Fail(ex.Message);
        }
    }

    private static IEnumerable<AlertEvent> BuildEvents(
        IReadOnlyList<Finding> findings,
        Guid channelId,
        AlertStatus status,
        string? error
    )
    {
        DateTime now = DateTime.UtcNow;
        foreach (Finding finding in findings)
        {
            yield return new AlertEvent
            {
                Id = Guid.NewGuid(),
                FindingId = finding.Id,
                ChannelId = channelId,
                SentAt = now,
                Status = status,
                Error = error,
            };
        }
    }
}
