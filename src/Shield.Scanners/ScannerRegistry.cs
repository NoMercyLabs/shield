using Shield.Core.Abstractions;
using Shield.Core.Domain;

namespace Shield.Scanners;

public sealed class ScannerRegistry
{
    readonly IReadOnlyDictionary<SourceType, IScanner> _scanners;

    public ScannerRegistry(IEnumerable<IScanner> scanners)
    {
        _scanners = scanners.ToDictionary(scanner => scanner.SourceType);
    }

    public IScanner? FindFor(SourceType sourceType) =>
        _scanners.TryGetValue(sourceType, out IScanner? scanner) ? scanner : null;

    public IScanner Require(SourceType sourceType) =>
        FindFor(sourceType)
        ?? throw new InvalidOperationException(
            $"No scanner registered for SourceType.{sourceType}"
        );
}
