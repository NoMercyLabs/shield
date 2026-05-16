using Shield.Core.Abstractions;
using Shield.Core.Results;
using Shield.Parsers.Python.Formats;

namespace Shield.Parsers.Python;

public sealed class PythonLockParser : IParser
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
            "poetry.lock" => PoetryLockParser.Parse(text),
            "pdm.lock" => PoetryLockParser.Parse(text),
            "uv.lock" => PoetryLockParser.Parse(text),
            "pipfile.lock" => PipfileLockParser.Parse(text),
            "requirements.txt" => RequirementsTxtParser.Parse(text),
            _ => ParseResult.Fail($"Unsupported python lockfile filename: {filename}"),
        };
    }
}
