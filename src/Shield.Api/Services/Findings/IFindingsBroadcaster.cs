using Shield.Core.Domain;

namespace Shield.Api.Services.Findings;

public interface IFindingsBroadcaster
{
    Task PublishNewAsync(IReadOnlyList<Finding> findings, CancellationToken ct);
    Task PublishCountsAsync(int low, int medium, int high, int critical, CancellationToken ct);
}
