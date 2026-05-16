using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Core.Abstractions;

public interface IScanner
{
    SourceType SourceType { get; }
    ValueTask<ScanResult> ScanAsync(Source source, CancellationToken ct);
}
