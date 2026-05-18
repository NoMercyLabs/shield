using Shield.Core.Abstractions;
using Shield.Core.Results;
using Shield.Parsers.Npm.Formats;

namespace Shield.Parsers.Npm;

public sealed class NpmLockParser : IParser
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
            "package-lock.json" => PackageLockParser.Parse(text),
            "npm-shrinkwrap.json" => PackageLockParser.Parse(text),
            "yarn.lock" => YarnLockParser.Parse(text),
            "pnpm-lock.yaml" => PnpmLockParser.Parse(text),
            _ => ParseResult.Fail($"Unsupported npm lockfile filename: {filename}"),
        };
    }
}
