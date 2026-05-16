using System.Text.Json;

namespace Shield.Parsers.Npm.Formats;

internal static class ParentChain
{
    public static string Encode(IReadOnlyList<string> chain)
    {
        if (chain.Count == 0)
        {
            return "[]";
        }
        return JsonSerializer.Serialize(chain);
    }
}
