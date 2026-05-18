using System.Text.RegularExpressions;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Elixir;

// mix.lock is an Erlang term map:
//   %{
//     "name" => {:hex, :name, "1.2.3", "<inner-hash>", [...], [...], "hexpm", "<outer-hash>"},
//     "git_dep" => {:git, "https://...", "<ref>", []},
//   }
// We don't need a full Erlang term parser — pull `"name" => {:hex, :atom, "version"`
// triples with a regex. Git-sourced entries are skipped (no semver).
public sealed class MixLockParser : IParser
{
    static readonly Regex HexEntry = new(
        "\"(?<key>[^\"]+)\"\\s*=>\\s*\\{:hex\\s*,\\s*:(?<name>[A-Za-z0-9_]+)\\s*,\\s*\"(?<version>[^\"]+)\"",
        RegexOptions.Compiled
    );

    public async ValueTask<ParseResult> ParseAsync(
        Stream content,
        string filename,
        CancellationToken ct
    )
    {
        using StreamReader reader = new(content, leaveOpen: true);
        string text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        return Parse(text);
    }

    static ParseResult Parse(string text)
    {
        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal);

        foreach (Match match in HexEntry.Matches(text))
        {
            string name = match.Groups["name"].Value;
            string version = match.Groups["version"].Value;
            if (name.Length == 0 || version.Length == 0)
                continue;

            // mix.lock conflates direct + transitive. We mark all as direct=true since
            // mix.exs (the manifest) isn't always shipped to scanners; downstream code
            // can recompute direct flags later if it has mix.exs.
            items.Add(
                new InventoryItem
                {
                    Ecosystem = Ecosystem.Hex,
                    Name = name,
                    Version = version,
                    ParentChain = "[]",
                    IsDirect = true,
                }
            );
        }

        if (items.Count == 0)
            diagnostics["error"] = "noPackagesFound";

        return ParseResult.Ok(items, diagnostics);
    }
}
