using Shield.Core.Results;

namespace Shield.Core.Abstractions;

public interface IParser
{
    ValueTask<ParseResult> ParseAsync(Stream content, string filename, CancellationToken ct);
}
