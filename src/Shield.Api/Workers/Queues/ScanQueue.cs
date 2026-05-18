using System.Threading.Channels;

namespace Shield.Api.Workers.Queues;

public sealed class ScanQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();

    public ChannelReader<int> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(int sourceId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(sourceId, ct);
}
