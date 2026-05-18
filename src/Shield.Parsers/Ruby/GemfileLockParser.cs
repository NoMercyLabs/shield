using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Ruby;

public sealed class GemfileLockParser : IParser
{
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
        HashSet<string> directNames = new(StringComparer.OrdinalIgnoreCase);

        // Pass 1: collect direct dependencies from the DEPENDENCIES section.
        // Gemfile.lock structure:
        //   DEPENDENCIES
        //     rails (~> 7.1)
        //     rspec
        bool inDeps = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                inDeps = false;
                continue;
            }

            if (line.StartsWith("DEPENDENCIES", StringComparison.Ordinal))
            {
                inDeps = true;
                continue;
            }

            if (!inDeps)
                continue;

            // Direct dep lines are indented with 2 spaces.
            if (!line.StartsWith("  ", StringComparison.Ordinal))
            {
                inDeps = false;
                continue;
            }

            string trimmed = line.Trim().TrimEnd('!');
            int paren = trimmed.IndexOf(' ');
            string name = paren > 0 ? trimmed[..paren] : trimmed;
            if (name.Length > 0)
                directNames.Add(name);
        }

        // Pass 2: collect every `name (version)` line under GEM / GIT / PATH specs blocks.
        // Sections look like:
        //   GEM
        //     remote: https://rubygems.org/
        //     specs:
        //       rails (7.1.3)
        //         actioncable (= 7.1.3)
        //       ...
        bool inSpecs = false;
        Dictionary<string, string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                inSpecs = false;
                continue;
            }

            // Top-level section header has no leading whitespace.
            if (!char.IsWhiteSpace(line[0]))
            {
                inSpecs = false;
                continue;
            }

            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("specs:", StringComparison.Ordinal))
            {
                inSpecs = true;
                continue;
            }

            if (!inSpecs)
                continue;

            // Spec lines are indented 4 spaces; sub-dependency lines are indented 6 spaces.
            int indent = line.Length - trimmed.Length;
            if (indent != 4)
                continue;

            // `name (version)` — version is the parenthesised group.
            int openParen = trimmed.IndexOf('(');
            int closeParen = trimmed.LastIndexOf(')');
            if (openParen <= 0 || closeParen <= openParen)
                continue;

            string name = trimmed[..openParen].Trim();
            string version = trimmed.Substring(openParen + 1, closeParen - openParen - 1).Trim();
            if (name.Length == 0 || version.Length == 0)
                continue;

            // Dedup; a gem can appear under GEM and PATH/GIT — keep first.
            if (seen.ContainsKey(name))
                continue;
            seen[name] = version;

            items.Add(
                new InventoryItem
                {
                    Ecosystem = Ecosystem.RubyGems,
                    Name = name,
                    Version = version,
                    ParentChain = "[]",
                    IsDirect = directNames.Contains(name),
                }
            );
        }

        if (items.Count == 0)
            diagnostics["error"] = "noPackagesFound";

        return ParseResult.Ok(items, diagnostics);
    }
}
