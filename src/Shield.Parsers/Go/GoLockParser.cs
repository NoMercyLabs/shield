using Shield.Core.Abstractions;
using Shield.Core.Results;
using Shield.Parsers.Go.Formats;

namespace Shield.Parsers.Go;

public sealed class GoLockParser : IParser
{
    public async ValueTask<ParseResult> ParseAsync(
        Stream content,
        string filename,
        CancellationToken ct
    )
    {
        string name = Path.GetFileName(filename).ToLowerInvariant();

        using StreamReader reader = new(content, leaveOpen: true);
        string text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        return name switch
        {
            "go.sum" => GoSumParser.Parse(text),
            "go.mod" => GoModParser.Parse(text),
            _ => ParseResult.Fail($"Unsupported go lockfile filename: {filename}"),
        };
    }
}
