using Shield.Core.Abstractions;
using Shield.Core.Results;
using Shield.Parsers.Rust.Formats;

namespace Shield.Parsers.Rust;

public sealed class RustDependencyParser : IParser
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
            "cargo.lock" => CargoLockParser.Parse(text),
            "cargo.toml" => CargoTomlParser.Parse(text),
            _ => ParseResult.Fail($"Unsupported Rust dependency filename: {filename}"),
        };
    }
}
