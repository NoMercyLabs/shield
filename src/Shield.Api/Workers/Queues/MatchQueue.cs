using System.Threading.Channels;

namespace Shield.Api.Workers.Queues;

public sealed record MatchRequest(Guid? SnapshotId, int? SourceId, bool MatchAll);

public sealed class MatchQueue
{
    private readonly Channel<MatchRequest> _channel = Channel.CreateUnbounded<MatchRequest>();

    public ChannelReader<MatchRequest> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(MatchRequest request, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(request, ct);
}
