using Microsoft.Extensions.Logging;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Channels.Inbox;

public sealed class InboxChannel : IAlertChannel
{
    private const int DigestThreshold = 5;

    private readonly IInboxStore _store;
    private readonly IAdminAudienceProvider _adminAudience;
    private readonly INotificationPublisher _publisher;
    private readonly ILogger<InboxChannel> _log;

    // INotificationPublisher here is Shield.Core.Abstractions.INotificationPublisher.
    // Registered in Shield.Api.Program.cs as AdminAudienceProvider + NotificationPublisher.
    public InboxChannel(
        IInboxStore store,
        IAdminAudienceProvider adminAudience,
        INotificationPublisher publisher,
        ILogger<InboxChannel> log
    )
    {
        _store = store;
        _adminAudience = adminAudience;
        _publisher = publisher;
        _log = log;
    }

    public ChannelType ChannelType => ChannelType.Inbox;

    public async ValueTask<AlertResult> SendAsync(
        AlertChannel cfg,
        IReadOnlyList<Finding> findings,
        CancellationToken ct
    )
    {
        if (findings.Count == 0)
            return AlertResult.Ok(0);

        try
        {
            IReadOnlyList<Guid> adminIds = await _adminAudience.GetAdminUserIdsAsync(ct);

            if (findings.Count >= DigestThreshold)
            {
                Severity max = findings.Max(finding => finding.Severity);
                string digestTitle = $"Shield digest · {findings.Count} findings";
                string digestBody = string.Join(
                    "\n",
                    findings.Select(finding =>
                        $"[{finding.Severity}] {finding.Notes ?? finding.DedupKey}"
                    )
                );

                InboxMessage digest = new()
                {
                    CreatedAt = DateTime.UtcNow,
                    Severity = max,
                    Title = digestTitle,
                    Body = digestBody,
                    FindingId = null,
                };
                await _store.AddAsync(digest, ct);

                foreach (Guid adminId in adminIds)
                {
                    await _publisher.PublishAsync(
                        new()
                        {
                            Id = Guid.NewGuid(),
                            UserId = adminId,
                            Kind = NotificationKind.Alert,
                            Severity = max,
                            Title = digestTitle,
                            Body = digestBody,
                            RelatedType = "Finding",
                            RelatedId = null,
                            CreatedAt = DateTime.UtcNow,
                        },
                        ct
                    );
                }

                return AlertResult.Ok(findings.Count);
            }

            foreach (Finding finding in findings)
            {
                string title = $"Shield · {finding.Severity} finding";
                string body = finding.Notes ?? finding.DedupKey;

                InboxMessage msg = new()
                {
                    CreatedAt = DateTime.UtcNow,
                    Severity = finding.Severity,
                    Title = title,
                    Body = body,
                    FindingId = finding.Id,
                };
                await _store.AddAsync(msg, ct);

                foreach (Guid adminId in adminIds)
                {
                    await _publisher.PublishAsync(
                        new()
                        {
                            Id = Guid.NewGuid(),
                            UserId = adminId,
                            Kind = NotificationKind.Alert,
                            Severity = finding.Severity,
                            Title = title,
                            Body = body,
                            RelatedType = "Finding",
                            RelatedId = finding.Id.ToString(),
                            CreatedAt = DateTime.UtcNow,
                        },
                        ct
                    );
                }
            }

            return AlertResult.Ok(findings.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Inbox channel failed to write {Count} message(s)", findings.Count);
            return AlertResult.Fail(ex.Message);
        }
    }
}
