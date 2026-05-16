using System.Threading.Channels;

namespace Shield.Api.Workers;

public sealed record MatchRequest(Guid? SnapshotId, int? SourceId, bool MatchAll);

public sealed class ScanQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();

    public ChannelReader<int> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(int sourceId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(sourceId, ct);
}

public sealed class MatchQueue
{
    private readonly Channel<MatchRequest> _channel = Channel.CreateUnbounded<MatchRequest>();

    public ChannelReader<MatchRequest> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(MatchRequest request, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(request, ct);
}

public sealed class FeedRefreshQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();

    public ChannelReader<string> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(string feedName, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(feedName, ct);
}
