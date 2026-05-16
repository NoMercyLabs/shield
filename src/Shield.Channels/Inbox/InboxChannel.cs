using Microsoft.Extensions.Logging;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Channels.Inbox;

public sealed class InboxChannel : IAlertChannel
{
    private const int DigestThreshold = 5;

    private readonly IInboxStore _store;
    private readonly ILogger<InboxChannel> _log;

    public InboxChannel(IInboxStore store, ILogger<InboxChannel> log)
    {
        _store = store;
        _log = log;
    }

    public ChannelType ChannelType => ChannelType.Inbox;

    public async ValueTask<AlertResult> SendAsync(
        AlertChannel cfg,
        IReadOnlyList<Finding> findings,
        CancellationToken ct
    )
    {
        if (findings.Count == 0) return AlertResult.Ok(0);

        try
        {
            if (findings.Count >= DigestThreshold)
            {
                Severity max = findings.Max(finding => finding.Severity);
                InboxMessage digest = new()
                {
                    CreatedAt = DateTime.UtcNow,
                    Severity = max,
                    Title = $"Shield digest · {findings.Count} findings",
                    Body = string.Join(
                        "\n",
                        findings.Select(finding => $"[{finding.Severity}] {finding.DedupKey}")
                    ),
                    FindingId = null,
                };
                await _store.AddAsync(digest, ct);
                return AlertResult.Ok(findings.Count);
            }

            foreach (Finding finding in findings)
            {
                InboxMessage msg = new()
                {
                    CreatedAt = DateTime.UtcNow,
                    Severity = finding.Severity,
                    Title = $"Shield · {finding.Severity} finding",
                    Body = finding.Notes ?? finding.DedupKey,
                    FindingId = finding.Id,
                };
                await _store.AddAsync(msg, ct);
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
