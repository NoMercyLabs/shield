using System.Threading.Channels;
using Shield.Api.Services.Updates;

namespace Shield.Api.Workers.Queues;

// One enqueued Updates-apply request. JobId is the correlation id the SPA subscribes to in
// SignalR so it can render per-source progress live.
public sealed record UpdateApplyJob(
    Guid JobId,
    UpdateApplyScope Scope,
    IReadOnlyList<int>? SourceIds,
    bool Force,
    bool ConfirmProduction,
    Guid? RequestedByUserId
);

public sealed class UpdateApplyQueue
{
    private readonly Channel<UpdateApplyJob> _channel = Channel.CreateUnbounded<UpdateApplyJob>();

    public ChannelReader<UpdateApplyJob> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(UpdateApplyJob job, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(job, ct);
}
