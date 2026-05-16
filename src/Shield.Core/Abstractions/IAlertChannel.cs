using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Core.Abstractions;

public interface IAlertChannel
{
    ChannelType ChannelType { get; }
    ValueTask<AlertResult> SendAsync(
        AlertChannel cfg,
        IReadOnlyList<Finding> findings,
        CancellationToken ct
    );
}
