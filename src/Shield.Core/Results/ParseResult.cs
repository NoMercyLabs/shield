using Shield.Core.Domain;

namespace Shield.Core.Results;

public sealed record ParseResult(
    IReadOnlyList<InventoryItem> Items,
    IReadOnlyDictionary<string, string> Diagnostics,
    bool Success,
    string? Error
)
{
    public static ParseResult Ok(
        IReadOnlyList<InventoryItem> items,
        IReadOnlyDictionary<string, string>? diagnostics = null
    ) => new(items, diagnostics ?? new Dictionary<string, string>(), true, null);

    public static ParseResult Fail(string error) =>
        new([], new Dictionary<string, string>(), false, error);
}
