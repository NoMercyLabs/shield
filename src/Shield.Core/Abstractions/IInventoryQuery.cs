using Shield.Core.Domain;

namespace Shield.Core.Abstractions;

/// Surfaces the distinct (ecosystem, name, version) tuples that feeds should query against.
/// Implementations read from the latest inventory snapshot per enabled source.
public interface IInventoryQuery
{
    ValueTask<IReadOnlyList<InventoryCoordinate>> GetActiveCoordinatesAsync(CancellationToken ct);
}

public readonly record struct InventoryCoordinate(Ecosystem Ecosystem, string Name, string Version);
