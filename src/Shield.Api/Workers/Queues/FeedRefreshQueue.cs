using System.Threading.Channels;

namespace Shield.Api.Workers.Queues;

public sealed class FeedRefreshQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();

    public ChannelReader<string> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(string feedName, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(feedName, ct);
}
